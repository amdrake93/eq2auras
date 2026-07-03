using System;
using System.Collections.Generic;
using System.Linq;
using Eq2Auras.Core.Config;

namespace Eq2Auras.Core.Timers
{
    /// The escalation policy for ONE timer group: a per-tick mapping from the group's
    /// readings to display elements, parameterized by the group's PanelSettings. The
    /// palette assigner is injected and SHARED across groups (color identity is global —
    /// SPEC §Timer colors). Every display state still derives from the data ACT reports
    /// this tick, and nothing on screen ever outlives the data.
    public sealed class EscalationTracker
    {
        private const int CenterSlots = 3;          // future config knob

        private readonly PanelSettings _settings;
        private readonly PaletteAssigner _palette;

        public EscalationTracker(PanelSettings settings = null, PaletteAssigner palette = null)
        {
            _settings = settings ?? new PanelSettings();
            _palette = palette ?? new PaletteAssigner();
        }

        public OverlayFrame Tick(IReadOnlyList<TimerReading> readings, IReadOnlyList<int> paletteArgb = null)
        {
            // A re-firing trigger ADDS a SpellTimer instance to the same frame — but ACT's
            // engine kills the WHOLE frame when the soonest instance expires (measured:
            // `removed` fired at tL=2 with a live second instance). So the SOONEST instance
            // per (Name|Combatant) key is the only truthful countdown — the same one ACT's
            // native window shows.
            var governing = readings
                .GroupBy(KeyOf)
                .Select(g => g.OrderBy(TimerMath.PreciseOf).First())
                .Select(r => WithResolvedColor(r, paletteArgb))
                .ToList();

            var live = governing.Where(r => r.TimeLeft > 0).ToList();
            bool inPlace = _settings.EscalationStyle == EscalationStyle.HighlightInPlace;

            // Overdue is CONFIG-DRIVEN: the timer's own RemoveValue decides whether an
            // overdue window exists. Remove-at-0 timers show NOTHING past zero — even
            // while ACT's laggy clock still reports them at -1/-2 pending removal. Only
            // timers configured to linger (negative RemoveValue) earn a LATE count-up.
            var lates = inPlace
                ? new List<CenterElement>()
                : governing
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

            var centered = new List<TimerReading>();
            if (!inPlace)
            {
                var imminent = live
                    .Where(r => r.TimeLeft <= TimerMath.EffectiveWarning(r))
                    .OrderBy(TimerMath.PreciseOf)
                    .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                centered = imminent.Take(Math.Max(0, CenterSlots - lates.Count)).ToList();
            }

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

            var listSource = inPlace ? (IEnumerable<TimerReading>)governing : live.Except(centered);
            return new OverlayFrame
            {
                ListRows = TimerListBuilder.Build(listSource, includeOverdue: inPlace),
                CenterElements = lates.Concat(pies).ToList()
            };
        }

        /// Resolves the final display color into a COPY of the governing reading — never
        /// in place: the engine routes the same reading objects to multiple groups, and
        /// an in-place write would hand the second group the first group's output (e.g.
        /// ActColor softening an already-assigned palette color).
        private TimerReading WithResolvedColor(TimerReading reading, IReadOnlyList<int> paletteArgb)
        {
            return new TimerReading
            {
                Name = reading.Name,
                Combatant = reading.Combatant,
                TimeLeft = reading.TimeLeft,
                RawPreciseTimeLeft = reading.RawPreciseTimeLeft,
                WarningValue = reading.WarningValue,
                RemoveValueSeconds = reading.RemoveValueSeconds,
                TotalSeconds = reading.TotalSeconds,
                ShowInPanelA = reading.ShowInPanelA,
                ShowInPanelB = reading.ShowInPanelB,
                FillArgb = ColorPolicy.Resolve(_settings.ColorSource, _palette.IndexFor(reading.Name), reading.FillArgb, paletteArgb)
            };
        }

        private static string KeyOf(TimerReading r) => r.Name + "|" + r.Combatant;
    }
}
