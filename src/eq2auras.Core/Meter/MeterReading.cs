using System.Collections.Generic;

namespace Eq2Auras.Core.Meter
{
    /// One combatant's per-poll totals, snapshotted from ACT's computed model under
    /// the data lock (SPEC Part III §The one data rule): corrections already applied,
    /// no ACT types, no WPF types. Carries every combatant (allies and not — see
    /// IsAlly); the mini-parse filter in MeterEngine decides visibility.
    public sealed class CombatantReading
    {
        public string Name { get; set; }
        public long Damage { get; set; }
        public long Healed { get; set; }      // includes wards — the EQ2 parser folds absorbs in
        public int CureDispels { get; set; }
        public long DamageTaken { get; set; }
        public long HealsTaken { get; set; }
        public long PowerReplenish { get; set; }   // power restored to others (ACT swing type 13)
        public bool IsAlly { get; set; }      // was this combatant in ACT's GetAllies()? — drives the mini-parse ShowOnlyAllies filter
        public System.Collections.Generic.List<string> AbilityNames { get; set; }   // unconfirmed allies only; the class-inference read (SPEC §Class colors); null otherwise
    }

    /// The current segment's per-poll identity/duration. Both duration branches
    /// travel so the live-vs-final selection is Core policy, testable on the Mac
    /// (SPEC Part III §Rates come from our wall clock).
    public sealed class EncounterReading
    {
        public bool Exists { get; set; }               // false: session start / after a clear
        public bool Active { get; set; }
        public double LiveDurationSeconds { get; set; }    // LastEstimatedTime - StartTime (may be garbage pre-first-swing; engine clamps)
        public double FinalDurationSeconds { get; set; }   // ACT's finalized log-time Duration
    }

    /// One resolved segment's per-poll snapshot (SPEC §Segments — the probe emits one per
    /// distinct requested segment key). Deaths ride the sample so a Deaths window reads its
    /// own segment's timeline. Unavailable = Zonewide with PopulateAll off (dormant body).
    public sealed class SegmentSample
    {
        public string Key { get; set; }
        public EncounterReading Encounter { get; set; }
        public List<CombatantReading> Combatants { get; set; }
        public List<DeathRecord> Deaths { get; set; }
        public bool Unavailable { get; set; }
        public long EncounterStartTicks { get; set; }   // the new-combat edge signal (host snaps non-pinned windows)
    }

    /// The probe→host per-poll payload: the samples plus the current zone key the probe resolved
    /// under the lock, so the host maps each window→sample by the SAME snapshot (no zone-change race).
    public sealed class SegmentSampleSet
    {
        public string CurrentZoneKey { get; set; }
        public List<SegmentSample> Samples { get; set; }
    }
}
