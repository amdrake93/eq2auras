using System.Windows.Controls;
using System.Windows.Media;

namespace Eq2Auras.Plugin.Overlay
{
    /// Per-window display style resolved from PanelSettings (SPEC §Typography,
    /// §Moving the overlay): Scale multiplies GEOMETRY only; typography derives every
    /// text role proportionally from BaseSize and never scales with the window.
    public sealed class VisualStyle
    {
        public double Scale { get; set; } = 1.0;      // clamped 0.5–2.5 upstream
        public FontFamily Font { get; set; }          // null = system default
        public double BaseSize { get; set; } = 13.0;  // WPF DIPs

        // The six text roles (measured defaults: 13, 13, 34, 13, 22, 12).
        public double RowText => BaseSize;
        public double PieName => BaseSize;
        public double PieSeconds => BaseSize * 34.0 / 13.0;
        public double LateTag => BaseSize * 22.0 / 13.0;
        public double LateName => BaseSize * 12.0 / 13.0;

        public void ApplyFont(TextBlock text, double size)
        {
            if (Font != null) text.FontFamily = Font;
            text.FontSize = size;
        }
    }
}
