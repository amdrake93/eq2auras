using System;
using System.Collections.Generic;
using System.Linq;

namespace Eq2Auras.Core.Timers
{
    /// Turns raw per-instance readings into the sorted calm-list rows.
    /// Escalation pivots on each timer's own WarningValue (SPEC: we do not invent
    /// thresholds); fallbacks per SPEC when the timer lacks a usable one.
    public static class TimerListBuilder
    {
        public static List<TimerRow> Build(IEnumerable<TimerReading> readings)
        {
            // TimeLeft <= 0 is excluded: ACT drops the frame <1s after zero (measured),
            // so a data-driven LATE state is a sub-second flicker. Overdue presentation
            // returns deliberately with the slice-2 minimum-display floor.
            return readings
                .Where(r => r.TimeLeft > 0)
                .Select(ToRow)
                .OrderBy(r => r.TimeLeft)
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static TimerRow ToRow(TimerReading reading)
        {
            return new TimerRow
            {
                Name = reading.Name,
                Combatant = reading.Combatant,
                TimeLeft = reading.TimeLeft,
                PreciseTimeLeft = TimerMath.PreciseOf(reading),
                TotalSeconds = reading.TotalSeconds,
                FillFraction = FillFraction(reading),
                FillArgb = reading.FillArgb,
                Urgency = UrgencyOf(reading)
            };
        }

        private static TimerUrgency UrgencyOf(TimerReading reading)
        {
            if (reading.TimeLeft <= 0) return TimerUrgency.Overdue;
            return reading.TimeLeft <= TimerMath.EffectiveWarning(reading) ? TimerUrgency.Imminent : TimerUrgency.Calm;
        }

        private static double FillFraction(TimerReading reading)
        {
            if (reading.TotalSeconds <= 0) return 0;
            var fraction = reading.TimeLeft / (double)reading.TotalSeconds;
            return Math.Max(0, Math.Min(1, fraction));
        }
    }
}
