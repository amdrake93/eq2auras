using System.Collections.Generic;
using System.Linq;

namespace Eq2Auras.Core.Meter
{
    /// One entry of the primary picker's vocabulary (SPEC Part III §The metric registry —
    /// Predefined primary selections): a curated pairing of a scope and a metric under one
    /// label, chosen together in a single click. The secondary picker uses scope-free
    /// MetricDefs directly, so there is no secondary-selection type.
    public sealed class PrimarySelection
    {
        public string Label { get; }
        public MeterScope Scope { get; }
        public string MetricKey { get; }

        public PrimarySelection(string label, MeterScope scope, string metricKey)
        {
            Label = label;
            Scope = scope;
            MetricKey = metricKey;
        }
    }

    public static class MeterSelections
    {
        public static readonly IReadOnlyList<PrimarySelection> Primary = new List<PrimarySelection>
        {
            new PrimarySelection("DPS", MeterScope.Allies, "encdps"),
            new PrimarySelection("Damage Taken", MeterScope.Allies, "damagetaken"),
            new PrimarySelection("Enemy Damage Taken", MeterScope.Enemies, "damagetaken"),
            new PrimarySelection("HPS", MeterScope.Allies, "enchps"),
            new PrimarySelection("Total Healing", MeterScope.Allies, "totalhealing"),
            new PrimarySelection("Enemy Healing Done", MeterScope.Enemies, "totalhealing"),
            new PrimarySelection("Healing Taken", MeterScope.Allies, "healstaken"),
            new PrimarySelection("Cures", MeterScope.Allies, "cures"),
            new PrimarySelection("Power Replenish", MeterScope.Allies, "powerheal"),
            new PrimarySelection("Deaths", MeterScope.Allies, "deaths"),
        };

        /// The selection a window's (scope, metric) state names — its label is the header
        /// identity (SPEC §Header). Null when no selection matches (forward-compat: the
        /// engine falls back to the bare metric label).
        public static PrimarySelection Resolve(MeterScope scope, string metricKey)
            => Primary.FirstOrDefault(s => s.Scope == scope && s.MetricKey == metricKey);
    }
}
