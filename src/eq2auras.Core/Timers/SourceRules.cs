using System;
using System.Collections.Generic;
using System.Linq;

namespace Eq2Auras.Core.Timers
{
    /// The one generic routing predicate. A reading feeds a group iff it matches ANY of
    /// the group's rules. No code knows what "the buff window" or "panel A" is — the only
    /// difference between groups is their rule DATA (SPEC §Timer groups; the anti-panel-C core).
    public static class SourceRules
    {
        public static bool Matches(TimerReading reading, SourceRule rule)
        {
            if (reading == null || rule == null) return false;
            switch (rule.Type)
            {
                case SourceRuleType.Panel:
                    return (rule.Value == "1" && reading.ShowInPanelA)
                        || (rule.Value == "2" && reading.ShowInPanelB);
                case SourceRuleType.Category:
                    return string.Equals(reading.Category, rule.Value, StringComparison.OrdinalIgnoreCase);
                case SourceRuleType.Name:
                    return string.Equals(reading.Name, rule.Value, StringComparison.OrdinalIgnoreCase);
                default:
                    return false;
            }
        }

        public static bool MatchesAny(IEnumerable<SourceRule> rules, TimerReading reading)
            => rules != null && rules.Any(rule => Matches(reading, rule));
    }
}
