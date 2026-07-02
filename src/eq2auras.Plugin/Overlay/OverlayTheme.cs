using System.Windows.Media;
using Eq2Auras.Core.Timers;

namespace Eq2Auras.Plugin.Overlay
{
    /// Shared palette + color helpers for the overlay windows. All values are
    /// Phase-1 constants (future config knobs).
    internal static class OverlayTheme
    {
        /// Session-stable timer palette (SPEC §Timer colors): 5 distinguishable, pleasant
        /// hues — blue/orange anchor pair for colorblind separation, then teal/pink/violet
        /// split by lightness. Deliberately avoids Gold (imminent accent) and Crimson
        /// (overdue accent) territory. Rendered as-is (no soften — these are designed).
        public static readonly Color[] Palette =
        {
            Color.FromRgb(0x56, 0xB4, 0xE9), // sky
            Color.FromRgb(0xE6, 0x9F, 0x00), // amber
            Color.FromRgb(0x00, 0x9E, 0x73), // teal
            Color.FromRgb(0xE3, 0x7D, 0xA4), // rose
            Color.FromRgb(0x5E, 0x6B, 0xD8), // indigo
        };

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

        /// ACT timer colors are user data and default to maximum-saturation primaries
        /// (pure #0000FF blue) — blend toward slate so fills read pleasant while the
        /// timer's hue stays recognizable.
        public static Color Soften(Color c)
        {
            const double keep = 0.65;
            const byte slateR = 110, slateG = 118, slateB = 130;
            return Color.FromArgb(255,
                (byte)(c.R * keep + slateR * (1 - keep)),
                (byte)(c.G * keep + slateG * (1 - keep)),
                (byte)(c.B * keep + slateB * (1 - keep)));
        }

        public static Color SoftTimerColor(int fillArgb) => Soften(FromArgbInt(fillArgb));
    }
}
