namespace Eq2Auras.Core.Timers
{
    public enum TimerUrgency { Calm, Imminent, Overdue }

    /// One display row of the calm list, ready for a renderer: no ACT types, no WPF types.
    public sealed class TimerRow
    {
        public string Name { get; set; }
        public string Combatant { get; set; }
        public int TimeLeft { get; set; }
        public double FillFraction { get; set; }  // 0..1 share of total duration remaining
        public int FillArgb { get; set; }
        public TimerUrgency Urgency { get; set; }
    }
}
