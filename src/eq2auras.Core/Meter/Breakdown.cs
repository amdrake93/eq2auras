using System.Collections.Generic;

namespace Eq2Auras.Core.Meter
{
    /// Which data-grouping dimension a breakdown request/reading is keyed by — the drill's
    /// by-ability breakdown vs. the hover's by-counterpart breakdown (SPEC Part III §Row
    /// drill-down). One channel, two read shapes; a window is only ever drilling OR hovering,
    /// so it issues at most one request. ByAbility = 0 keeps a request built without it on the
    /// shipped drill path. Transient — never persisted.
    public enum BreakdownGrouping
    {
        ByAbility = 0,
        ByCounterpart,
        RecapSecond,   // the recap-second hover — a per-event log, read via the ReadHoverNow seam (SPEC §Deaths)
    }

    /// One by-ability entry the drill-down deep-read produces: an ability label and its
    /// RAW value (per-ability AttackType total — the Plugin reads it, Core divides by
    /// duration for rate metrics). No ACT types (SPEC Part III §The one data rule).
    public sealed class BreakdownEntry
    {
        public string Label { get; set; }
        public double Value { get; set; }
    }

    /// The probe→host drill snapshot: one combatant's by-ability entries for one
    /// breakdown bucket, read under the ACT lock (SPEC Part III §Assembly split).
    public sealed class BreakdownReading
    {
        public string CombatantName { get; set; }
        public MetricBreakdownSource Source { get; set; }
        public BreakdownGrouping Grouping { get; set; }
        public List<BreakdownEntry> Entries { get; set; }
    }

    /// The host→probe drill request: which combatant + which bucket a drilled window
    /// needs deep-read this poll. Transient — never persisted (SPEC Part III §Settings).
    public sealed class DrillRequest
    {
        public string CombatantName { get; set; }
        public MetricBreakdownSource Source { get; set; }
        public BreakdownGrouping Grouping { get; set; }   // ByAbility (default) = the drill; ByCounterpart = the hover
        public string DeathKey { get; set; }   // set when Source == Deaths — which death (Victim#Ordinal) to recap; null otherwise
        public int Second { get; set; }   // RecapSecond grouping: which recap second (0..9) to read; else unused
        public SegmentSelection Selection { get; set; }   // the requesting window's segment; the probe deep-reads that segment (SPEC §Segments), null = Current
    }
}
