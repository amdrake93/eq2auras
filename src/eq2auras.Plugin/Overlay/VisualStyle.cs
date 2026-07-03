using System;
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
        public const double RowTextPadding = 6;   // text-fit floor = line height + this

        public double RowWidth { get; set; } = DefaultRowWidth;
        public double RowHeight { get; set; } = DefaultRowHeight;
        public double RadialSize { get; set; } = DefaultRadialSize;
        public FontFamily Font { get; set; }          // null = system default
        public double BaseSize { get; set; } = 13.0;  // WPF DIPs

        // The six text roles (measured defaults: 13, 13, 34, 13, 22, 12).
        public double RowText => BaseSize;
        public double PieName => BaseSize;
        public double PieSeconds => BaseSize * 34.0 / 13.0;
        public double LateTag => BaseSize * 22.0 / 13.0;
        public double LateName => BaseSize * 12.0 / 13.0;

        /// Text-fit floor (SPEC §Element dimensions): a row is never shorter than its
        /// own text line plus padding, whatever the configured height says.
        public double EffectiveRowHeight
            => Math.Max(RowHeight, TextLineHeight + RowTextPadding);

        public double HeightRatio => EffectiveRowHeight / DefaultRowHeight;
        public double RadialRatio => RadialSize / DefaultRadialSize;

        private double TextLineHeight
            => (Font ?? SystemFonts.MessageFontFamily).LineSpacing * RowText;

        public void ApplyFont(TextBlock text, double size)
        {
            if (Font != null) text.FontFamily = Font;
            text.FontSize = size;
        }
    }
}
