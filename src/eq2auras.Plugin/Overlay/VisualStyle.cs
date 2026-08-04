using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Eq2Auras.Plugin.Overlay
{
    /// Per-panel display style resolved from PanelSettings (SPEC §Element dimensions,
    /// §Typography): elements own their size; text derives from BaseSize only and
    /// never changes with element dimensions. One instance serves both of a panel's
    /// windows.
    public sealed class VisualStyle
    {
        public const double DefaultRowWidth = 250;
        public const double DefaultRowHeight = 26;
        public const double DefaultRadialSize = 110;

        public double RowWidth { get; set; } = DefaultRowWidth;
        public double RowHeight { get; set; } = DefaultRowHeight;
        public double RadialSize { get; set; } = DefaultRadialSize;
        public double RowSpacing { get; set; } = 4.0; // flat DIPs — never derived (SPEC §Element dimensions)
        public FontFamily Font { get; set; }          // null = system default
        public double BaseSize { get; set; } = 13.0;  // WPF DIPs
        public FontWeight FontWeight { get; set; } = FontWeights.Normal;   // the picker's Bold (field-2026-08-03 — respect the dialog's style)
        public FontStyle FontStyle { get; set; } = FontStyles.Normal;      // the picker's Italic

        // The five text roles (13, 13, 34, 13, 13 — row, pie name, pie seconds,
        // LATE tag, LATE name). LATE respects the font as-is (field verdict, SPEC
        // §Typography); only the radial's seconds keep a boost — the escalation
        // focal glyph.
        public double RowText => BaseSize;
        public double PieName => BaseSize;
        public double PieSeconds => BaseSize * 34.0 / 13.0;
        public double LateTag => BaseSize;
        public double LateName => BaseSize;

        // The configured dimension always wins (SPEC §Element dimensions): text that
        // doesn't fit clips at the row bounds — a floor here would silently contradict
        // the tab's number. Field-rejected, same lesson as the Overdue display floor.
        public double HeightRatio => RowHeight / DefaultRowHeight;
        public double RadialRatio => RadialSize / DefaultRadialSize;

        public void ApplyFont(TextBlock text, double size)
        {
            if (Font != null) text.FontFamily = Font;
            text.FontSize = size;
            // Only override the weight when the user actually picked Bold — a Normal style must NOT clobber
            // a visual's own accent (timer pie/LATE Bold, meter value SemiBold), which the initializer set
            // before this call. Italic is always safe (no visual sets FontStyle in its initializer).
            if (FontWeight != FontWeights.Normal) text.FontWeight = FontWeight;
            text.FontStyle = FontStyle;
        }
    }
}
