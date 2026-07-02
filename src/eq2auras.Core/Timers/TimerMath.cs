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

        /// Smooth remaining seconds — the raw wall-clock value, unclamped. (Clamping to
        /// ACT's integer TimeLeft was tried and produced a sawtooth: ACT's log-derived
        /// clock lags when the log is quiet and kept yanking animations back up.)
        public static double PreciseOf(TimerReading reading) => reading.RawPreciseTimeLeft;
    }
}
