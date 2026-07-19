using System;
using System.Windows;
using System.Windows.Controls;

namespace Eq2Auras.Plugin.Overlay
{
    /// Fixed row-column widths, measured from the window's current font (SPEC Part III
    /// §Rows) — no hardcoded pixels, so a font/size change just re-measures. Number
    /// columns (value + secondary) reserve the wider of the sig-figs cap and a five-digit
    /// count so a rate column and a count column reserve identically; the percent reserves
    /// "100%". A value wider than its reserve clips in its cell (the row never widens).
    internal static class MeterColumns
    {
        private const string RateCap = "9.99M";    // three-sig-figs worst case
        private const string CountCap = "99999";   // five-digit count worst case
        private const string PercentCap = "100%";

        /// Left margin between trailing columns (rows). The header total's inset from the
        /// right edge is one percent-column-width plus this, so the total caps the value column.
        public const double ColumnGap = 6;

        public static double NumberWidth(VisualStyle style, double fontSize)
            => Math.Max(Measure(style, RateCap, fontSize), Measure(style, CountCap, fontSize));

        public static double PercentWidth(VisualStyle style, double fontSize)
            => Measure(style, PercentCap, fontSize);

        private static double Measure(VisualStyle style, string sample, double fontSize)
        {
            var probe = new TextBlock { Text = sample };
            style.ApplyFont(probe, fontSize);
            probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return Math.Ceiling(probe.DesiredSize.Width);
        }
    }
}
