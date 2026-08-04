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
        private readonly Func<IReadOnlyList<SegmentSelection>> _segmentRequests;   // each window's live selection (SPEC §Segments)
        private readonly Action<SegmentSampleSet, List<BreakdownReading>, List<RecapReading>> _onSample;
        private readonly Func<string, bool> _isClassCommitted;   // skip the ability-name read for allies confirmed this encounter (SPEC §Class colors)
        private int _tick;

        // Deaths capture (SPEC §Deaths — poll-only, count-delta triggers a bounded killing-blow scan).
        private readonly List<DeathRecord> _deathStore = new List<DeathRecord>();
        private readonly Dictionary<string, int> _deathsSeen = new Dictionary<string, int>();
        private DateTime _encounterStartKey = DateTime.MinValue;

        public EncounterProbe(Func<bool> enabled, Func<IReadOnlyList<DrillRequest>> drillRequests,
            Func<IReadOnlyList<SegmentSelection>> segmentRequests,
            Action<SegmentSampleSet, List<BreakdownReading>, List<RecapReading>> onSample,
            Func<string, bool> isClassCommitted = null)
        {
            _enabled = enabled;
            _drillRequests = drillRequests;
            _segmentRequests = segmentRequests;
            _onSample = onSample;
            _isClassCommitted = isClassCommitted ?? (_ => false);
        }

        /// Called once per TimerProbe poll tick, on ACT's UI thread.
        public void OnTick()
        {
            if (++_tick % SampleEveryNthTick != 0) return;
            if (!_enabled()) return;

            var requests = _drillRequests?.Invoke();   // O(1) volatile read — before the lock
            var selections = _segmentRequests?.Invoke() ?? (IReadOnlyList<SegmentSelection>)new[] { SegmentSelection.Current() };
            var samples = new List<SegmentSample>();
            var breakdowns = new List<BreakdownReading>();
            var recaps = new List<RecapReading>();
            string currentZoneKey = null;
            try
            {
                var form = ActGlobals.oFormActMain;
                lock (form.AfterCombatActionDataLock)
                {
                    currentZoneKey = SegmentResolver.CurrentZoneKey(form);

                    // Resolve the distinct requested segments once each (SegmentKeys.Distinct always
                    // includes "C", the fallback target). One snapshot per key; all-Current collapses to one.
                    EncounterData currentEncounter = null;
                    var bySegKeyEncounter = new Dictionary<string, EncounterData>();
                    foreach (var key in SegmentKeys.Distinct(selections, currentZoneKey))
                    {
                        var encounter = SegmentResolver.ResolveByKey(form, key, out bool unavailable);
                        if (key == "C") currentEncounter = encounter;
                        bySegKeyEncounter[key] = encounter;
                        samples.Add(ReadSegment(form, encounter, key, key == "C", unavailable));
                    }

                    // Deep reads (drill / hover / recap) each target the requesting window's segment
                    // (SPEC §Segments — every read follows the window's segment), resolved this poll.
                    if (requests != null && requests.Count > 0)
                    {
                        string killingKey = ActGlobals.ActLocalization.LocalizationStrings["specialAttackTerm-killing"].DisplayedText;
                        foreach (var request in requests)
                        {
                            var reqKey = SegmentKeys.Of(request.Selection ?? SegmentSelection.Current(), currentZoneKey);
                            var reqEnc = (bySegKeyEncounter.TryGetValue(reqKey, out var re) && re != null) ? re : currentEncounter;
                            if (reqEnc == null) continue;

                            // At most one CombatantData per request — never a per-combatant fan-out
                            // (plan-watch #2). Items is keyed UPPERCASE.
                            if (request.Source == MetricBreakdownSource.Deaths)
                            {
                                var recap = ReadRecap(reqEnc, request, killingKey);
                                if (recap != null) recaps.Add(recap);
                                continue;
                            }
                            if (request.Source == MetricBreakdownSource.None) continue;
                            if (!reqEnc.Items.TryGetValue((request.CombatantName ?? "").ToUpper(), out var combatant)) continue;
                            var entries = request.Grouping == BreakdownGrouping.ByCounterpart
                                ? ReadByCounterpart(combatant, request.Source)
                                : ReadBreakdown(combatant, request.Source);
                            if (entries != null)
                                breakdowns.Add(new BreakdownReading { CombatantName = request.CombatantName, Source = request.Source, Grouping = request.Grouping, Entries = entries });
                        }
                    }
                }
            }
            catch
            {
                return;   // same defensive stance as TimerProbe's GetTimerFrames read
            }

            _onSample(new SegmentSampleSet { CurrentZoneKey = currentZoneKey, Samples = samples }, breakdowns, recaps);   // outside the lock
        }

        /// Read one resolved segment's encounter into a Core sample. `isCurrent` gates the two
        /// live-only reads — class-inference ability names and the death-capture store — which ride
        /// the live current encounter regardless of what any window displays (SPEC §Segments).
        private SegmentSample ReadSegment(FormActMain form, EncounterData encounter, string key, bool isCurrent, bool unavailable)
        {
            var sample = new SegmentSample
            {
                Key = key,
                Unavailable = unavailable,
                Combatants = new List<CombatantReading>(),
                Deaths = new List<DeathRecord>(),
            };
            if (unavailable || encounter == null)
            {
                sample.Encounter = new EncounterReading { Exists = false };
                return sample;
            }

            bool active = encounter.Active;
            sample.Encounter = new EncounterReading
            {
                Exists = true,
                Active = active,
                // Degenerate pre-first-swing polls (StartTime == DateTime.MaxValue) produce a hugely
                // negative estimate here — MeterEngine clamps.
                LiveDurationSeconds = (form.LastEstimatedTime - encounter.StartTime).TotalSeconds,
                FinalDurationSeconds = active ? 0 : encounter.Duration.TotalSeconds,
            };
            sample.EncounterStartTicks = encounter.StartTime.Ticks;

            // Mirror ACT's mini parse: base set is EVERY combatant (Items.Values); the ally set only
            // *filters* it, in Core, via the ShowOnlyAllies-with-escape-hatch rule (SPEC §Displayed combatants).
            var allySet = new HashSet<CombatantData>(encounter.GetAllies());
            foreach (var combatant in encounter.Items.Values)
            {
                var reading = new CombatantReading
                {
                    Name = combatant.Name,
                    Damage = combatant.Damage,
                    Healed = combatant.Healed,
                    CureDispels = combatant.CureDispels,
                    DamageTaken = combatant.DamageTaken,
                    HealsTaken = combatant.HealsTaken,
                    PowerReplenish = combatant.PowerReplenish,
                    IsAlly = allySet.Contains(combatant),
                };
                if (isCurrent && reading.IsAlly && !_isClassCommitted(reading.Name))
                    reading.AbilityNames = ReadOutgoingAbilityNames(combatant);
                sample.Combatants.Add(reading);
            }

            if (isCurrent)
                sample.Deaths = CaptureDeaths(encounter, allySet);
            return sample;
        }

        /// Poll-only death capture for the current encounter (SPEC §Deaths): a count-delta triggers a
        /// bounded killing-blow scan into the store; returns the store snapshot for the sample.
        private List<DeathRecord> CaptureDeaths(EncounterData encounter, HashSet<CombatantData> allySet)
        {
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
                if (deathCount <= seen) continue;

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
            return new List<DeathRecord>(_deathStore);   // snapshot for the WPF thread
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

        /// Synchronous one-second per-event read for the recap-second hover (SPEC §Deaths — the
        /// recap-second hover). Runs on the overlay thread under the same AfterCombatActionDataLock —
        /// one victim, one second, never a fan-out. Mirrors ReadRecap's death lookup; emits one detail
        /// per incoming damage/heal swing in the hovered second, keeping its source + ability. Returns
        /// false (card stays absent) when the encounter/death is gone or the DeathKey is malformed.
        public static bool TryReadRecapSecondNow(DrillRequest request, out List<RecapEventDetail> events)
        {
            events = null;
            if (request == null || string.IsNullOrEmpty(request.DeathKey)) return false;
            try
            {
                var form = ActGlobals.oFormActMain;
                lock (form.AfterCombatActionDataLock)
                {
                    var encounter = ResolveForRequest(form, request);
                    if (encounter == null) return false;

                    int hash = request.DeathKey.LastIndexOf('#');
                    if (hash < 0) return false;
                    string victimName = request.DeathKey.Substring(0, hash);
                    if (!int.TryParse(request.DeathKey.Substring(hash + 1), out int ordinal)) return false;
                    if (!encounter.Items.TryGetValue(victimName.ToUpper(), out var victim)) return false;

                    string killingKey = ActGlobals.ActLocalization.LocalizationStrings["specialAttackTerm-killing"].DisplayedText;
                    var deathSwings = new List<MasterSwing>();
                    if (victim.AllInc.TryGetValue(killingKey, out var killingAt))
                        foreach (var sw in killingAt.Items)
                            if (sw.Damage == Dnum.Death) deathSwings.Add(sw);
                    deathSwings.Sort((a, b) => a.TimeSorter.CompareTo(b.TimeSorter));
                    if (ordinal < 1 || ordinal > deathSwings.Count) return false;
                    var death = deathSwings[ordinal - 1];

                    string allKey = ActGlobals.ActLocalization.LocalizationStrings["attackTypeTerm-all"].DisplayedText;
                    var list = new List<RecapEventDetail>();
                    CollectRecapSecond(victim, CombatantData.DamageTypeDataIncomingDamage, isHeal: false, death, allKey, request.Second, list);
                    CollectRecapSecond(victim, CombatantData.DamageTypeDataIncomingHealing, isHeal: true, death, allKey, request.Second, list);
                    events = list;
                }
            }
            catch
            {
                return false;
            }
            return events != null;
        }

        /// One bucket's incoming swings in a single recap second → per-event details (SPEC §Deaths).
        /// Same window/positive-only filter as CollectRecap, narrowed to the hovered second, keeping
        /// each swing's Attacker (source) + AttackType (ability) + TimeSorter (order).
        private static void CollectRecapSecond(CombatantData victim, string bucketName, bool isHeal,
            MasterSwing death, string allKey, int second, List<RecapEventDetail> details)
        {
            if (!victim.Items.TryGetValue(bucketName, out var bucket)) return;
            foreach (var pair in bucket.Items)
            {
                if (pair.Key == allKey) continue;
                foreach (var sw in pair.Value.Items)
                {
                    double secondsBefore = (death.Time - sw.Time).TotalSeconds;
                    if (sw.TimeSorter > death.TimeSorter || secondsBefore < 0 || secondsBefore >= 10) continue;
                    if ((int)System.Math.Floor(secondsBefore) != second) continue;   // only the hovered second
                    long amt = (long)sw.Damage;
                    if (amt <= 0) continue;   // real damage/heal only
                    details.Add(new RecapEventDetail { Source = sw.Attacker, Ability = sw.AttackType, Amount = amt, IsHeal = isHeal, Order = sw.TimeSorter });
                }
            }
        }

        /// Resolve the segment a synchronous hover read targets (SPEC §Segments — every read follows
        /// the window's segment): the request's selection → its EncounterData, or Current on a culled
        /// historical handle. Called under the lock, so CurrentZoneKey is consistent with the resolve.
        private static EncounterData ResolveForRequest(FormActMain form, DrillRequest request)
        {
            var sel = request?.Selection ?? SegmentSelection.Current();
            var enc = SegmentResolver.ResolveByKey(form, SegmentKeys.Of(sel, SegmentResolver.CurrentZoneKey(form)), out _);
            return enc ?? form.ActiveZone?.ActiveEncounter;
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

        /// A combatant's own cast ability NAMES for class inference (SPEC §Class colors): the KEYS of
        /// its outgoing buckets only — the player's own casts (incoming would misattribute the attacker's
        /// class). Keys-only, so it does NOT trigger the lazy per-ability metric folds. Read the bucket
        /// alias-statics at CALL time — the EQ2 parser reassigns them at ITS init and plugin order is
        /// user-controlled (as BucketName does, above), so a type-init static array would freeze stale
        /// values and silently read nothing all session.
        private static List<string> ReadOutgoingAbilityNames(CombatantData combatant)
        {
            var buckets = new[] { CombatantData.DamageTypeDataOutgoingDamage, CombatantData.DamageTypeDataOutgoingHealing };
            string allKey = ActGlobals.ActLocalization.LocalizationStrings["attackTypeTerm-all"].DisplayedText;
            var names = new List<string>();
            foreach (var bucketName in buckets)
            {
                if (!combatant.Items.TryGetValue(bucketName, out var damageType)) continue;
                foreach (var key in damageType.Items.Keys)
                    if (key != allKey) names.Add(key);
            }
            return names;
        }

        /// One combatant's by-counterpart entries for a bucket, read under the ACT lock (SPEC
        /// Part III §Row drill-down — the by-row mouseover). Iterates the bucket's raw MasterSwings
        /// (skipping the aggregate "All" AttackType, docs/act-parse-engine.md:69-71) and folds them
        /// by counterpart. Returns an EMPTY list (not null) when the bucket is absent, so a
        /// zero-valued row still opens an honest empty card; null only for an unmapped source.
        private static List<BreakdownEntry> ReadByCounterpart(CombatantData combatant, MetricBreakdownSource source)
        {
            var bucketName = BucketName(source);
            if (bucketName == null) return null;
            var entries = new List<BreakdownEntry>();
            if (!combatant.Items.TryGetValue(bucketName, out var damageType)) return entries;

            string allKey = ActGlobals.ActLocalization.LocalizationStrings["attackTypeTerm-all"].DisplayedText;
            bool byAttacker = BreakdownDirection.IsIncoming(source);
            bool countMode = source == MetricBreakdownSource.Cures;   // cures = a swing COUNT (CombatantData.CureDispels is a count)
            var acc = new Dictionary<string, double>();
            foreach (var pair in damageType.Items)
            {
                if (pair.Key == allKey) continue;
                GroupByCounterpart(pair.Value.Items, byAttacker, countMode, acc);
            }
            foreach (var kv in acc)
                entries.Add(new BreakdownEntry { Label = kv.Key, Value = kv.Value });
            return entries;
        }

        /// Fold a swing list into a counterpart accumulator — the granularity-agnostic helper the
        /// reserved recap-second per-source breakdown reuses (SPEC §Reserved seams), fed one second's
        /// swings instead of a whole bucket. Value mode (damage/heal/power) sums positive Dnums only
        /// (skips misses/avoids/sentinels). Count mode (cures) counts every swing — mirroring the
        /// shipped drill's cures path (ReadValue reads at.Swings, a count); cure swings carry
        /// damage=1 (docs/act-parse-engine.md:326), so a value-mode sum would coincide here, but
        /// count mode keeps the count explicit and independent of that Dnum value.
        private static void GroupByCounterpart(IEnumerable<MasterSwing> swings, bool byAttacker, bool countMode, Dictionary<string, double> acc)
        {
            foreach (var sw in swings)
            {
                long amt = (long)sw.Damage;
                if (!countMode && amt <= 0) continue;
                string counterpart = (byAttacker ? sw.Attacker : sw.Victim) ?? "";
                acc.TryGetValue(counterpart, out double cur);
                acc[counterpart] = cur + (countMode ? 1 : amt);
            }
        }

        /// Synchronous one-shot breakdown read for the hover card's INSTANT first paint (SPEC Part
        /// III §Row drill-down — the by-row mouseover). Runs on the CALLER's thread (the overlay
        /// thread) but takes the same AfterCombatActionDataLock, so it serializes against ACT's
        /// writes rather than racing them — one combatant, never a fan-out. Returns false (and the
        /// per-poll path fills the card a beat later) when there's no encounter or the combatant is
        /// absent; the duration matches OnTick's live/frozen policy so rate metrics read identically.
        public static bool TryReadNow(DrillRequest request, out List<BreakdownEntry> entries, out double durationSeconds)
        {
            entries = null;
            durationSeconds = 0;
            if (request == null
                || request.Source == MetricBreakdownSource.None
                || request.Source == MetricBreakdownSource.Deaths) return false;
            try
            {
                var form = ActGlobals.oFormActMain;
                lock (form.AfterCombatActionDataLock)
                {
                    var encounter = ResolveForRequest(form, request);
                    if (encounter == null) return false;
                    bool active = encounter.Active;
                    durationSeconds = MeterEngine.DurationSeconds(new EncounterReading
                    {
                        Exists = true,
                        Active = active,
                        LiveDurationSeconds = (form.LastEstimatedTime - encounter.StartTime).TotalSeconds,
                        FinalDurationSeconds = active ? 0 : encounter.Duration.TotalSeconds,
                    });
                    if (!encounter.Items.TryGetValue((request.CombatantName ?? "").ToUpper(), out var combatant)) return false;
                    entries = request.Grouping == BreakdownGrouping.ByCounterpart
                        ? ReadByCounterpart(combatant, request.Source)
                        : ReadBreakdown(combatant, request.Source);
                }
            }
            catch
            {
                return false;
            }
            return entries != null;
        }
    }
}
