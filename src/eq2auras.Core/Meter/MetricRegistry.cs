using System.Collections.Generic;
using System.Linq;

namespace Eq2Auras.Core.Meter
{
    /// The meter's entire vocabulary: ACT's ExportVariables names, our plumbing
    /// (SPEC Part III §The metric registry). Adding a metric = appending a definition.
    public static class MetricRegistry
    {
        public const string DefaultKey = "encdps";

        public static readonly IReadOnlyList<MetricDef> All = new List<MetricDef>
        {
            new MetricDef("encdps", "DPS", "Damage", isRate: true, r => r.Damage, NumberFormat.Abbreviate),
            new MetricDef("enchps", "HPS", "Healing", isRate: true, r => r.Healed, NumberFormat.Abbreviate),
            new MetricDef("cures", "Cures", "Utility", isRate: false, r => r.CureDispels, NumberFormat.Integer),
        };

        /// Null/unknown keys resolve to the DPS default — the forward-compat guard
        /// for settings files written by newer versions (SPEC Part III §Settings).
        public static MetricDef Resolve(string key)
            => All.FirstOrDefault(m => m.Key == key) ?? All.First(m => m.Key == DefaultKey);

        /// The PRIMARY metric's resolver, distinct from Resolve: a *null* key is a user-cleared
        /// primary (→ null, the window shows nothing, SPEC Part III §Configuration); a non-null but
        /// unknown key still falls back to the DPS default (forward-compat for a newer version's
        /// key). All window-creation paths seed a non-null key, so null arises only from a clear.
        public static MetricDef ResolvePrimary(string key)
            => key == null ? null : (All.FirstOrDefault(m => m.Key == key) ?? All.First(m => m.Key == DefaultKey));

        /// The secondary's resolver: the matching def, or null for null/unknown — no
        /// DPS fallback, because an unresolved secondary means "off", not "show DPS"
        /// (SPEC Part III §Settings — the secondary key's forward-compat guard).
        public static MetricDef Find(string key)
            => All.FirstOrDefault(m => m.Key == key);
    }
}
