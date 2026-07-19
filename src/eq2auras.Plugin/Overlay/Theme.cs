using System.Windows.Media;

namespace Eq2Auras.Plugin.Overlay
{
    /// The overlay's chrome vocabulary — one source of truth for dark-chrome colors
    /// (SPEC Part I §The theme system). Semantic tokens named by ROLE, not value;
    /// frozen SolidColorBrushes so callers share one instance rather than allocating a
    /// brush per element. Timer-only colors stay in OverlayTheme, which aliases the
    /// shared value. Increment 1 lands the surface, text, accent, and divider tokens; the
    /// interactive-state tokens (LinkNormal/LinkHover/ItemSelected, SPEC §The theme system)
    /// land with the control that consumes them — links with the button (increment 2),
    /// ItemSelected with the selectable list-item (increment 4), a hover/selected form being
    /// kit-coupled — as do the font-weight, spacing, and radius scales.
    internal static class Theme
    {
        // The single dark blue-grey backdrop tint; opacity is applied per surface
        // (translucent timer / knob-driven meter / solid chrome, §The theme system).
        public static readonly Color SurfaceTint = Color.FromRgb(0x14, 0x17, 0x1D);   // 20,23,29

        public static readonly SolidColorBrush TextPrimary = Frozen(Color.FromRgb(0xF5, 0xF5, 0xF5));   // values, titles (== WhiteSmoke)
        public static readonly SolidColorBrush TextLabel   = Frozen(Color.FromRgb(0xC4, 0xCA, 0xD6));   // field labels, percent column
        public static readonly SolidColorBrush TextMuted   = Frozen(Color.FromRgb(0x8B, 0x93, 0xA3));   // dim/subordinate text, links

        public static readonly SolidColorBrush Divider      = Frozen(Color.FromRgb(0x33, 0x40, 0x4F));   // 51,64,79 (== OverlayTheme.CalmBorder rgb)
        public static readonly SolidColorBrush AccentAmber  = Frozen(Colors.Gold);
        public static readonly SolidColorBrush AccentCrimson = Frozen(Colors.Crimson);
        public static readonly SolidColorBrush AccentBlue   = Frozen(Color.FromRgb(0x56, 0xB4, 0xE9));

        /// A frozen backdrop brush at a given alpha over SurfaceTint. Increment 1 paints
        /// no backdrop with it — it is the surface-brush factory the settings window and
        /// the persistent backdrop consume in later increments.
        public static SolidColorBrush Surface(byte alpha)
            => Frozen(Color.FromArgb(alpha, SurfaceTint.R, SurfaceTint.G, SurfaceTint.B));

        private static SolidColorBrush Frozen(Color c)
        {
            var brush = new SolidColorBrush(c);
            brush.Freeze();
            return brush;
        }
    }
}
