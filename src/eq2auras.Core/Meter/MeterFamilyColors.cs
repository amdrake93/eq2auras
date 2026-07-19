using System.Collections.Generic;

namespace Eq2Auras.Core.Meter
{
    /// The single source of truth for metric family colors (SPEC Part III §The metric
    /// registry), keyed by MetricDef.Category. Consumed by the row fill (MeterEngine) and
    /// the popup's family-column headers so the two can never drift. The interim color
    /// model, pending row-color-by-class (SPEC Part III §Rows).
    public static class MeterFamilyColors
    {
        private static readonly int Fallback = unchecked((int)0xFF8B93A3);   // neutral grey for an uncategorized metric

        private static readonly IReadOnlyDictionary<string, int> ByCategory = new Dictionary<string, int>
        {
            { "Damage",  unchecked((int)0xFFE05A5A) },   // red
            { "Healing", unchecked((int)0xFF2FBF8F) },   // green/teal
            { "Utility", unchecked((int)0xFF56B4E9) },   // blue/sky
        };

        public static int ArgbFor(string category)
            => category != null && ByCategory.TryGetValue(category, out var argb) ? argb : Fallback;
    }
}
