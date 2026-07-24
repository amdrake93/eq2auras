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
        private readonly Func<IReadOnlyList<string>> _hoverRequests;   // SPIKE (mouseover-spike): combatant names to deep-read damage-by-target
        private readonly Action<EncounterReading, List<CombatantReading>, List<BreakdownReading>, List<DeathRecord>, List<RecapReading>, List<BreakdownReading>> _onSample;
        private int _tick;

        // Deaths capture (SPEC §Deaths — poll-only, count-delta triggers a bounded killing-blow scan).
        private readonly List<DeathRecord> _deathStore = new List<DeathRecord>();
        private readonly Dictionary<string, int> _deathsSeen = new Dictionary<string, int>();
        private DateTime _encounterStartKey = DateTime.MinValue;

        public EncounterProbe(Func<bool> enabled, Func<IReadOnlyList<DrillRequest>> drillRequests,
            Func<IReadOnlyList<string>> hoverRequests,
            Action<EncounterReading, List<CombatantReading>, List<BreakdownReading>, List<DeathRecord>, List<RecapReading>, List<BreakdownReading>> onSample)
        {
            _enabled = enabled;
            _drillRequests = drillRequests;
            _hoverRequests = hoverRequests;
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
            var recaps = new List<RecapReading>();
            var hoverBreakdowns = new List<BreakdownReading>();   // SPIKE (mouseover-spike)
            var requests = _drillRequests?.Invoke();   // O(1) volatile read — before the lock
            var hoverNames = _hoverRequests?.Invoke();   // SPIKE — same lock-free snapshot pattern
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

                        // Deaths capture (SPEC §Deaths): poll-only count-delta → bounded killing-blow scan.
                        if (encounter.StartTime != _encounterStartKey)   // new encounter → reset the store
                        {
                            _encounterStartKey = encounter.StartTime;
                            _deathStore.Clear();
                            _deathsSeen.Clear();
                        }
                        string killingKey = ActGlobals.ActLocalization.LocalizationStrings["specialAttackTerm-killing"].DisplayedText;
                        foreach (var combatant in encounter.Items.Values)
                        {
                            if (!allySet.Contains(combatant)) continue;      // Allies-only (SPEC §Deaths)
                            int deathCount = combatant.Deaths;               // boolean-cached, cheap (verified ACT 3.8.5.288)
                            _deathsSeen.TryGetValue(combatant.Name, out int seen);
                            if (deathCount <= seen) continue;                // no new death for this victim

                            // The victim's Death swings live as the incoming "Killing" AttackType (AllInc);
                            // enumerate chronologically and record the un-seen ordinals.
                            var deathSwings = new List<MasterSwing>();
                            if (combatant.AllInc.TryGetValue(killingKey, out var killingAt))
                                foreach (var sw in killingAt.Items)
                                    if (sw.Damage == Dnum.Death) deathSwings.Add(sw);
                            deathSwings.Sort((a, b) => a.TimeSorter.CompareTo(b.TimeSorter));

                            for (int ordinal = seen + 1; ordinal <= deathCount && ordinal <= deathSwings.Count; ordinal++)
                            {
                                var deathSwing = deathSwings[ordinal - 1];
                                FindKillingBlow(combatant, deathSwing.TimeSorter, out string blowAbility, out double blowDamage);
                                _deathStore.Add(new DeathRecord
                                {
                                    Victim = combatant.Name,
                                    Ordinal = ordinal,
                                    TimeOfDeathSeconds = (deathSwing.Time - encounter.StartTime).TotalSeconds,
                                    KillingBlowAbility = blowAbility,
                                    KillingBlowDamage = blowDamage,
                                    DrillKey = combatant.Name + "#" + ordinal,
                                });
                            }
                            _deathsSeen[combatant.Name] = deathCount;
                        }

                        if (requests != null && requests.Count > 0)
                        {
                            foreach (var request in requests)
                            {
                                // At most one CombatantData per request — never a per-combatant fan-out
                                // (plan-watch #2). GetAllies is already resolved above; Items is keyed UPPERCASE.
                                if (request.Source == MetricBreakdownSource.Deaths)
                                {
                                    var recap = ReadRecap(encounter, request, killingKey);
                                    if (recap != null) recaps.Add(recap);
                                    continue;
                                }
                                if (request.Source == MetricBreakdownSource.None) continue;
                                if (!encounter.Items.TryGetValue((request.CombatantName ?? "").ToUpper(), out var combatant)) continue;
                                var entries = ReadBreakdown(combatant, request.Source);
                                if (entries != null)
                                    breakdowns.Add(new BreakdownReading { CombatantName = request.CombatantName, Source = request.Source, Entries = entries });
                            }
                        }

                        // SPIKE (mouseover-spike): the by-target hover surface. One deep-read per hovered
                        // combatant (never a fan-out), same on-hover-under-lock stance as the drill.
                        if (hoverNames != null && hoverNames.Count > 0)
                        {
                            foreach (var name in hoverNames)
                            {
                                if (!encounter.Items.TryGetValue((name ?? "").ToUpper(), out var combatant)) continue;
                                var entries = ReadByTarget(combatant);
                                if (entries != null)
                                    hoverBreakdowns.Add(new BreakdownReading { CombatantName = name, Source = MetricBreakdownSource.OutgoingDamage, Entries = entries });
                            }
                        }
                    }
                }
            }
            catch
            {
                return;   // same defensive stance as TimerProbe's GetTimerFrames read
            }

            var deaths = new List<DeathRecord>(_deathStore);       // snapshot for the WPF thread
            _onSample(encounterReading, combatants, breakdowns, deaths, recaps, hoverBreakdowns);   // outside the lock — hold it briefly
        }

        /// SPIKE (mouseover-spike): one combatant's OUTGOING DAMAGE grouped by target (Victim),
        /// read under the ACT lock. Sums positive Dnums over the category "All" AttackType's raw
        /// swings — the "All" bucket is written unconditionally (robust to ActGlobals.restrictToAll;
        /// ACT 3.8.5.288 CombatantData.AddCombatAction). Reuses BreakdownEntry; the real by-target
        /// data pipe is a future brainstorm (docs/backlog.md), unbiased by this expedient read.
        private static List<BreakdownEntry> ReadByTarget(CombatantData combatant)
        {
            if (!combatant.Items.TryGetValue(CombatantData.DamageTypeDataOutgoingDamage, out var outgoing)) return null;
            string allKey = ActGlobals.ActLocalization.LocalizationStrings["attackTypeTerm-all"].DisplayedText;
            if (!outgoing.Items.TryGetValue(allKey, out var allAttackType)) return null;

            var byVictim = new Dictionary<string, double>();
            foreach (var sw in allAttackType.Items)
            {
                long damage = (long)sw.Damage;
                if (damage <= 0) continue;   // real damage only (skip misses/avoids/sentinels)
                byVictim.TryGetValue(sw.Victim, out double accumulated);
                byVictim[sw.Victim] = accumulated + damage;
            }

            var entries = new List<BreakdownEntry>();
            foreach (var pair in byVictim)
                entries.Add(new BreakdownEntry { Label = pair.Key, Value = pair.Value });
            return entries;
        }

        /// The killing blow = the victim's last INCOMING DAMAGE swing at/before the death's TimeSorter
        /// (SPEC §Deaths). ability=null / damage=0 when none is found (unsourced/absorbed) → the row shows "—".
        private static void FindKillingBlow(CombatantData victim, int deathTimeSorter, out string ability, out double damage)
        {
            ability = null;
            damage = 0;
            if (!victim.Items.TryGetValue(CombatantData.DamageTypeDataIncomingDamage, out var incoming)) return;
            string allKey = ActGlobals.ActLocalization.LocalizationStrings["attackTypeTerm-all"].DisplayedText;
            MasterSwing best = null;
            foreach (var pair in incoming.Items)
            {
                if (pair.Key == allKey) continue;
                foreach (var sw in pair.Value.Items)
                {
                    if ((long)sw.Damage <= 0) continue;             // real damage only (skip misses/avoids/the death sentinel)
                    if (sw.TimeSorter > deathTimeSorter) continue;  // at/before the death
                    if (best == null || sw.TimeSorter > best.TimeSorter) best = sw;
                }
            }
            if (best != null) { ability = best.AttackType; damage = (long)best.Damage; }
        }

        /// The recap for one death (SPEC §Death Recap): the victim's incoming damage + heal swings in
        /// the 10 s before the death, flattened into Core RecapEvents + the max-health estimate. Returns
        /// null if the death is gone (host auto-exits) or the DeathKey is malformed.
        private static RecapReading ReadRecap(EncounterData encounter, DrillRequest request, string killingKey)
        {
            int hash = (request.DeathKey ?? "").LastIndexOf('#');
            if (hash < 0) return null;
            string victimName = request.DeathKey.Substring(0, hash);
            if (!int.TryParse(request.DeathKey.Substring(hash + 1), out int ordinal)) return null;
            if (!encounter.Items.TryGetValue(victimName.ToUpper(), out var victim)) return null;

            var deathSwings = new List<MasterSwing>();
            if (victim.AllInc.TryGetValue(killingKey, out var killingAt))
                foreach (var sw in killingAt.Items)
                    if (sw.Damage == Dnum.Death) deathSwings.Add(sw);
            deathSwings.Sort((a, b) => a.TimeSorter.CompareTo(b.TimeSorter));
            if (ordinal < 1 || ordinal > deathSwings.Count) return null;   // death gone → host auto-exits
            var death = deathSwings[ordinal - 1];

            string allKey = ActGlobals.ActLocalization.LocalizationStrings["attackTypeTerm-all"].DisplayedText;
            var events = new List<RecapEvent>();
            CollectRecap(victim, CombatantData.DamageTypeDataIncomingDamage, isHeal: false, death, allKey, events);
            CollectRecap(victim, CombatantData.DamageTypeDataIncomingHealing, isHeal: true, death, allKey, events);

            return new RecapReading
            {
                DrillKey = request.DeathKey,
                MaxHealthEstimate = victim.GetMaxHealth(),   // running-min estimate (ACT 3.8.5.288 CombatantData:815)
                Events = events,
            };
        }

        private static void CollectRecap(CombatantData victim, string bucketName, bool isHeal,
            MasterSwing death, string allKey, List<RecapEvent> events)
        {
            if (!victim.Items.TryGetValue(bucketName, out var bucket)) return;
            foreach (var pair in bucket.Items)
            {
                if (pair.Key == allKey) continue;
                foreach (var sw in pair.Value.Items)
                {
                    double secondsBefore = (death.Time - sw.Time).TotalSeconds;
                    if (sw.TimeSorter > death.TimeSorter || secondsBefore < 0 || secondsBefore >= 10) continue;
                    long amt = (long)sw.Damage;
                    if (amt <= 0) continue;   // real damage/heal only
                    events.Add(new RecapEvent { SecondsBeforeDeath = secondsBefore, Amount = amt, IsHeal = isHeal });
                }
            }
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
