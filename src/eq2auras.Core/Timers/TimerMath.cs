using System;

namespace Eq2Auras.Core.Timers
{
    /// Shared escalation math: one source of truth for the warning threshold and
    /// the smooth remaining-time value.
    public static class TimerMath
    {
        private const double FallbackWarningFractionOfTotal = 0.25;
        private const int FallbackWarningAbsoluteSeconds = 10;

        public static int EffectiveWarning(TimerReading reading)
        {
            if (reading.WarningValue > 0 && reading.WarningValue < reading.TotalSeconds)
                return reading.WarningValue;
            if (reading.TotalSeconds > 0)
                return Math.Max(1, (int)(reading.TotalSeconds * FallbackWarningFractionOfTotal));
            return FallbackWarningAbsoluteSeconds;
        }

        /// Smooth remaining seconds, clamped into [TimeLeft, TimeLeft + 0.999] so the
        /// pie's drain always agrees with the integer second being displayed.
        public static double PreciseOf(TimerReading reading)
        {
            return Math.Max(reading.TimeLeft, Math.Min(reading.TimeLeft + 0.999, reading.RawPreciseTimeLeft));
        }
    }
}
