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
        private const double FallbackWarningFractionOfTotal = 0.25;
        private const int FallbackWarningAbsoluteSeconds = 10;

        public static List<TimerRow> Build(IEnumerable<TimerReading> readings)
        {
            return readings
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
                FillFraction = FillFraction(reading),
                FillArgb = reading.FillArgb,
                Urgency = UrgencyOf(reading)
            };
        }

        private static TimerUrgency UrgencyOf(TimerReading reading)
        {
            if (reading.TimeLeft <= 0) return TimerUrgency.Overdue;
            return reading.TimeLeft <= EffectiveWarning(reading) ? TimerUrgency.Imminent : TimerUrgency.Calm;
        }

        private static int EffectiveWarning(TimerReading reading)
        {
            if (reading.WarningValue > 0 && reading.WarningValue < reading.TotalSeconds)
                return reading.WarningValue;
            if (reading.TotalSeconds > 0)
                return Math.Max(1, (int)(reading.TotalSeconds * FallbackWarningFractionOfTotal));
            return FallbackWarningAbsoluteSeconds;
        }

        private static double FillFraction(TimerReading reading)
        {
            if (reading.TotalSeconds <= 0) return 0;
            var fraction = reading.TimeLeft / (double)reading.TotalSeconds;
            return Math.Max(0, Math.Min(1, fraction));
        }
    }
}
