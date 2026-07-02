namespace Eq2Auras.Core.Timers
{
    /// One raw reading of one live timer INSTANCE (a TimerFrame can hold several —
    /// measured: concurrent triggers share a frame's SpellTimers list). Colors travel
    /// as ARGB ints so Core stays free of any drawing assembly.
    public sealed class TimerReading
    {
        public string Name { get; set; }
        public string Combatant { get; set; }
        public int TimeLeft { get; set; }        // int seconds, negative after expiry (measured)
        /// Sub-second remaining time computed by the adapter from StartTime + duration.
        /// May drift from ACT's log-derived clock — consume via TimerMath.PreciseOf,
        /// which clamps it to agree with the integer TimeLeft on display.
        public double RawPreciseTimeLeft { get; set; }
        public int WarningValue { get; set; }    // the timer's own "alert at N seconds left"
        public int TotalSeconds { get; set; }    // post-mod duration (TimerFinalDuration)
        public int FillArgb { get; set; }        // TimerData.FillColor.ToArgb()
    }
}
