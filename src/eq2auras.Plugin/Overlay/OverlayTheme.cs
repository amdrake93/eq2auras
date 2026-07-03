using System.Windows.Media;
using Eq2Auras.Core.Timers;

namespace Eq2Auras.Plugin.Overlay
{
    /// Shared palette + color helpers for the overlay windows. All values are
    /// Phase-1 constants (future config knobs).
    internal static class OverlayTheme
    {
        public static readonly Color CalmBackground = Color.FromArgb(150, 18, 24, 34);
        public static readonly Color CalmBorder = Color.FromArgb(200, 51, 64, 79);
        public static readonly Color ImminentAccent = Colors.Gold;
        public static readonly Color OverdueAccent = Colors.Crimson;
        public static readonly Color Text = Colors.WhiteSmoke;

        public static Color AccentFor(TimerUrgency urgency)
        {
            switch (urgency)
            {
                case TimerUrgency.Overdue: return OverdueAccent;
                case TimerUrgency.Imminent: return ImminentAccent;
                default: return CalmBorder;
            }
        }

        public static Color FromArgbInt(int argb) => Color.FromArgb(
            (byte)((argb >> 24) & 0xFF), (byte)((argb >> 16) & 0xFF),
            (byte)((argb >> 8) & 0xFF), (byte)(argb & 0xFF));

        // Note: no soften here — Core's ColorPolicy resolves final display colors
        // (soften applies only to ActColor mode, inside Core). Renderers paint as-is.

        /// Spark: the bright leading-edge color for a draining fill — the timer's own
        /// color pushed toward white so the moving edge pops against the translucent fill.
        public static Color Spark(Color c)
        {
            const double keep = 0.55;
            return Color.FromArgb(255,
                (byte)(c.R * keep + 255 * (1 - keep)),
                (byte)(c.G * keep + 255 * (1 - keep)),
                (byte)(c.B * keep + 255 * (1 - keep)));
        }
    }
}
