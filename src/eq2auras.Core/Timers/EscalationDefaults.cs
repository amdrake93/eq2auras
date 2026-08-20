using System.Linq;
using Eq2Auras.Core.Config;

namespace Eq2Auras.Core.Timers
{
    /// The source-keyed default for a group's nullable EscalationStyle (SPEC §Configuration):
    /// null resolves to None for the buff-category group (a duration tracker, not a cooldown),
    /// CenterRadial for every other group. An explicit value is always used as-is.
    public static class EscalationDefaults
    {
        public static EscalationStyle Resolve(PanelSettings panel)
        {
            if (panel.EscalationStyle.HasValue) return panel.EscalationStyle.Value;
            bool isBuffGroup = panel.Sources != null
                && panel.Sources.Any(r => r != null
                    && r.Type == SourceRuleType.Category
                    && string.Equals(r.Value, BuffCatalog.Category, System.StringComparison.OrdinalIgnoreCase));
            return isBuffGroup ? EscalationStyle.None : EscalationStyle.CenterRadial;
        }
    }
}
