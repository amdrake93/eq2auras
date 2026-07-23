using System.Collections.Generic;

namespace Eq2Auras.Core.Meter
{
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
        public List<BreakdownEntry> Entries { get; set; }
    }

    /// The host→probe drill request: which combatant + which bucket a drilled window
    /// needs deep-read this poll. Transient — never persisted (SPEC Part III §Settings).
    public sealed class DrillRequest
    {
        public string CombatantName { get; set; }
        public MetricBreakdownSource Source { get; set; }
        public string DeathKey { get; set; }   // set when Source == Deaths — which death (Victim#Ordinal) to recap; null otherwise
    }
}
