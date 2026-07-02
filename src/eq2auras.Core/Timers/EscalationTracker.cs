using System;
using System.Collections.Generic;
using System.Linq;

namespace Eq2Auras.Core.Timers
{
    /// The escalation policy: a pure per-tick mapping from ACT's readings to display
    /// elements. Deliberately STATELESS — every state (calm / imminent pie / overdue
    /// LATE) derives from the data ACT reports this tick, and nothing on screen ever
    /// outlives the data.
    public sealed class EscalationTracker
    {
        private const int CenterSlots = 3;          // future config knob

        public OverlayFrame Tick(IReadOnlyList<TimerReading> readings)
        {
            // A re-firing trigger ADDS a SpellTimer instance to the same frame — but ACT's
            // engine kills the WHOLE frame when the soonest instance expires (measured:
            // `removed` fired at tL=2 with a live second instance). So the SOONEST instance
            // per (Name|Combatant) key is the only truthful countdown — the same one ACT's
            // native window shows.
            var governing = readings
                .GroupBy(KeyOf)
                .Select(g => g.OrderBy(TimerMath.PreciseOf).First())
                .ToList();
            var live = governing.Where(r => r.TimeLeft > 0).ToList();

            // Overdue is CONFIG-DRIVEN: the timer's own RemoveValue decides whether an
            // overdue window exists. Remove-at-0 timers show NOTHING past zero — even
            // while ACT's laggy clock still reports them at -1/-2 pending removal. Only
            // timers configured to linger (negative RemoveValue) earn a LATE count-up.
            var lates = governing
                .Where(r => r.TimeLeft <= 0 && r.RemoveValueSeconds < 0)
                .OrderBy(r => -r.TimeLeft)
                .Select(r => new CenterElement
                {
                    Kind = CenterElementKind.Late,
                    Name = r.Name,
                    Combatant = r.Combatant,
                    LateSeconds = -r.TimeLeft,
                    FillArgb = r.FillArgb
                })
                .ToList();

            var imminent = live
                .Where(r => r.TimeLeft <= TimerMath.EffectiveWarning(r))
                .OrderBy(TimerMath.PreciseOf)
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var pieSlots = Math.Max(0, CenterSlots - lates.Count);
            var centered = imminent.Take(pieSlots).ToList();
            var pies = centered.Select(r => new CenterElement
            {
                Kind = CenterElementKind.Pie,
                Name = r.Name,
                Combatant = r.Combatant,
                SecondsLeft = (int)Math.Max(0, Math.Ceiling(TimerMath.PreciseOf(r))),
                PreciseSecondsLeft = Math.Max(0, TimerMath.PreciseOf(r)),
                PieFraction = Math.Max(0, Math.Min(1.0, TimerMath.PreciseOf(r) / TimerMath.EffectiveWarning(r))),
                WarningSeconds = TimerMath.EffectiveWarning(r),
                FillArgb = r.FillArgb
            });

            return new OverlayFrame
            {
                ListRows = TimerListBuilder.Build(live.Except(centered)),
                CenterElements = lates.Concat(pies).ToList()
            };
        }

        private static string KeyOf(TimerReading r) => r.Name + "|" + r.Combatant;
    }
}
