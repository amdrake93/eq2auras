using System.Collections.Generic;

namespace Eq2Auras.Core.Timers
{
    public enum CenterElementKind { Pie, Late }

    /// One element of the center escalation zone.
    public sealed class CenterElement
    {
        public CenterElementKind Kind { get; set; }
        public string Name { get; set; }
        public string Combatant { get; set; }
        public int SecondsLeft { get; set; }      // Pie: seconds remaining
        public double PreciseSecondsLeft { get; set; } // Pie: sub-second, drives the smooth drain
        public double PieFraction { get; set; }   // Pie: remaining share of the warning window
        public int LateSeconds { get; set; }      // Late: seconds since it went overdue
        public int FillArgb { get; set; }
    }

    /// Everything the overlay renders for one tick.
    public sealed class OverlayFrame
    {
        public List<TimerRow> ListRows { get; set; } = new List<TimerRow>();
        public List<CenterElement> CenterElements { get; set; } = new List<CenterElement>();
    }
}
