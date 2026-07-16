namespace Eq2Auras.Core.Meter
{
    /// One ally's per-poll totals, snapshotted from ACT's computed model under the
    /// data lock (SPEC Part III §The one data rule): corrections already applied,
    /// no ACT types, no WPF types.
    public sealed class CombatantReading
    {
        public string Name { get; set; }
        public long Damage { get; set; }
        public long Healed { get; set; }      // includes wards — the EQ2 parser folds absorbs in
        public int CureDispels { get; set; }
    }

    /// The current segment's per-poll identity/duration. Both duration branches
    /// travel so the live-vs-final selection is Core policy, testable on the Mac
    /// (SPEC Part III §Rates come from our wall clock).
    public sealed class EncounterReading
    {
        public bool Exists { get; set; }               // false: session start / after a clear
        public bool Active { get; set; }
        public string Title { get; set; }              // strongest-enemy-so-far; may flip mid-fight
        public double LiveDurationSeconds { get; set; }    // LastEstimatedTime - StartTime (may be garbage pre-first-swing; engine clamps)
        public double FinalDurationSeconds { get; set; }   // ACT's finalized log-time Duration
    }
}
