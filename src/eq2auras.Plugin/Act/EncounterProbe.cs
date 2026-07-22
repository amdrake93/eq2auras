using System;
using System.Collections.Generic;
using Advanced_Combat_Tracker;
using Eq2Auras.Core.Meter;

namespace Eq2Auras.Plugin.Act
{
    /// The encounter adapter (SPEC Part III §Assembly split & polling): samples ACT's
    /// computed combat model on a divider of the existing 100 ms poll tick — briefly
    /// under AfterCombatActionDataLock, snapshot into Core DTOs, release, hand off.
    /// Reads only the cheap shapes: every combatant's totals + its ally flag (from
    /// GetAllies membership), the live title, StartTime (live branch) / Duration
    /// (frozen branch). Never holds an EncounterData reference across ticks; never
    /// touches EncId/GetHashCode.
    public sealed class EncounterProbe
    {
        public const int SampleEveryNthTick = 3;   // 100 ms tick -> ~300 ms effective (SPEC: ~2-4 Hz)

        private readonly Func<bool> _enabled;
        private readonly Func<IReadOnlyList<DrillRequest>> _drillRequests;
        private readonly Action<EncounterReading, List<CombatantReading>, List<BreakdownReading>> _onSample;
        private int _tick;

        public EncounterProbe(Func<bool> enabled, Func<IReadOnlyList<DrillRequest>> drillRequests,
            Action<EncounterReading, List<CombatantReading>, List<BreakdownReading>> onSample)
        {
            _enabled = enabled;
            _drillRequests = drillRequests;
            _onSample = onSample;
        }

        /// Called once per TimerProbe poll tick, on ACT's UI thread.
        public void OnTick()
        {
            if (++_tick % SampleEveryNthTick != 0) return;
            if (!_enabled()) return;

            EncounterReading encounterReading;
            var combatants = new List<CombatantReading>();
            var breakdowns = new List<BreakdownReading>();
            var requests = _drillRequests?.Invoke();   // O(1) volatile read — before the lock
            try
            {
                var form = ActGlobals.oFormActMain;
                lock (form.AfterCombatActionDataLock)
                {
                    var encounter = form.ActiveZone?.ActiveEncounter;
                    if (encounter == null)
                    {
                        encounterReading = new EncounterReading { Exists = false };
                    }
                    else
                    {
                        bool active = encounter.Active;
                        encounterReading = new EncounterReading
                        {
                            Exists = true,
                            Active = active,
                            // Degenerate pre-first-swing polls (StartTime == DateTime.MaxValue)
                            // produce a hugely negative estimate here — MeterEngine clamps.
                            LiveDurationSeconds = (form.LastEstimatedTime - encounter.StartTime).TotalSeconds,
                            FinalDurationSeconds = active ? 0 : encounter.Duration.TotalSeconds,
                        };

                        // Mirror ACT's mini parse: base set is EVERY combatant
                        // (Items.Values); the ally set only *filters* it, in Core,
                        // via the same ShowOnlyAllies-with-escape-hatch rule ACT uses
                        // (SPEC Part III §Displayed combatants). GetAllies() is
                        // you-relative, so an un-linked groupmate isn't an ally yet —
                        // that's the flag Core keys the filter on, not who we include.
                        var allySet = new HashSet<CombatantData>(encounter.GetAllies());
                        foreach (var combatant in encounter.Items.Values)
                        {
                            combatants.Add(new CombatantReading
                            {
                                Name = combatant.Name,
                                Damage = combatant.Damage,
                                Healed = combatant.Healed,
                                CureDispels = combatant.CureDispels,
                                DamageTaken = combatant.DamageTaken,
                                HealsTaken = combatant.HealsTaken,
                                PowerReplenish = combatant.PowerReplenish,
                                IsAlly = allySet.Contains(combatant),
                            });
                        }

                        if (requests != null && requests.Count > 0)
                        {
                            foreach (var request in requests)
                            {
                                // At most one CombatantData per request — never a per-combatant fan-out
                                // (plan-watch #2). GetAllies is already resolved above; Items is keyed UPPERCASE.
                                if (request.Source == MetricBreakdownSource.None) continue;
                                if (!encounter.Items.TryGetValue((request.CombatantName ?? "").ToUpper(), out var combatant)) continue;
                                var entries = ReadBreakdown(combatant, request.Source);
                                if (entries != null)
                                    breakdowns.Add(new BreakdownReading { CombatantName = request.CombatantName, Source = request.Source, Entries = entries });
                            }
                        }
                    }
                }
            }
            catch
            {
                return;   // same defensive stance as TimerProbe's GetTimerFrames read
            }

            _onSample(encounterReading, combatants, breakdowns);   // outside the lock — hold it briefly
        }

        /// Enum → ACT bucket alias-static. The statics are set at the EQ2 parser's init
        /// (ThirdParty/ACT_English_Parser.cs:2082-2088), so read them at call time, not at type init.
        private static string BucketName(MetricBreakdownSource source)
        {
            switch (source)
            {
                case MetricBreakdownSource.OutgoingDamage:  return CombatantData.DamageTypeDataOutgoingDamage;
                case MetricBreakdownSource.IncomingDamage:  return CombatantData.DamageTypeDataIncomingDamage;
                case MetricBreakdownSource.OutgoingHealing: return CombatantData.DamageTypeDataOutgoingHealing;
                case MetricBreakdownSource.IncomingHealing: return CombatantData.DamageTypeDataIncomingHealing;
                case MetricBreakdownSource.PowerReplenish:  return CombatantData.DamageTypeDataOutgoingPowerReplenish;
                case MetricBreakdownSource.Cures:           return CombatantData.DamageTypeDataOutgoingCures;
                default: return null;
            }
        }

        /// Per-ability value: the positive-Dnum sum for damage/heal/power buckets; the swing
        /// COUNT for cures (the count metric — CombatantData.CureDispels is a count). Field-gate:
        /// confirm the cures column reads sensibly on the box (the sole count breakdown).
        private static double ReadValue(MetricBreakdownSource source, AttackType at)
            => source == MetricBreakdownSource.Cures ? at.Swings : at.Damage;

        /// One combatant's by-ability entries for a bucket, read under the ACT lock. Skips the
        /// aggregate "All" AttackType (docs/act-parse-engine.md:69-71). Returns null if the bucket
        /// is absent (nothing of that kind happened) — the caller adds no reading, the window shows
        /// an empty detail until data arrives.
        private static List<BreakdownEntry> ReadBreakdown(CombatantData combatant, MetricBreakdownSource source)
        {
            var bucketName = BucketName(source);
            if (bucketName == null) return null;
            if (!combatant.Items.TryGetValue(bucketName, out var damageType)) return null;

            string allKey = ActGlobals.ActLocalization.LocalizationStrings["attackTypeTerm-all"].DisplayedText;
            var entries = new List<BreakdownEntry>();
            foreach (var pair in damageType.Items)
            {
                if (pair.Key == allKey) continue;   // the category aggregate, not a real ability
                entries.Add(new BreakdownEntry { Label = pair.Key, Value = ReadValue(source, pair.Value) });
            }
            return entries;
        }
    }
}
