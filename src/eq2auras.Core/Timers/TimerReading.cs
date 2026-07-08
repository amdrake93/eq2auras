using System;

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
        /// Sub-second remaining time computed by the adapter from StartTime + duration
        /// (wall clock). The wall clock OWNS the visuals: ACT's integer TimeLeft advances
        /// only when log lines arrive, so it lags/lurches when the log is quiet.
        public double RawPreciseTimeLeft { get; set; }
        public int WarningValue { get; set; }    // the timer's own "alert at N seconds left"
        public int RemoveValueSeconds { get; set; } // the timer's own overdue window (0 = gone at zero; negative = linger)
        public int TotalSeconds { get; set; }    // post-mod duration (TimerFinalDuration)
        public int FillArgb { get; set; }        // TimerData.FillColor.ToArgb()
        public bool ShowInPanelA { get; set; }   // TimerData.Panel1Display — group routing
        public bool ShowInPanelB { get; set; }   // TimerData.Panel2Display
        public bool IsMaster { get; set; }       // SpellTimer.MasterTimer — display candidacy; non-masters are diagnostics-only (SPEC §Timer identity)
        public DateTime StartTime { get; set; }  // SpellTimer.StartTime — governing order: newest master wins
    }
}
