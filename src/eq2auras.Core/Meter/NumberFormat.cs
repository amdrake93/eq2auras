using System;
using System.Globalization;

namespace Eq2Auras.Core.Meter
{
    /// The K/M/B abbreviation family (SPEC Part III §The metric registry — format).
    public static class NumberFormat
    {
        public static string Abbreviate(double value)
        {
            double abs = Math.Abs(value);
            if (abs >= 1_000_000_000) return Scaled(value / 1_000_000_000) + "B";
            if (abs >= 1_000_000) return Scaled(value / 1_000_000) + "M";
            if (abs >= 1_000) return Scaled(value / 1_000) + "K";
            return Math.Round(value).ToString(CultureInfo.InvariantCulture);
        }

        public static string Integer(double value)
            => Math.Round(value).ToString(CultureInfo.InvariantCulture);

        // Three significant figures: the decimal count falls as the leading part grows
        // (1.24 -> 12.4 -> 124), so the band string caps at four characters before the
        // suffix. Trailing zeros drop (0.## / 0.#), matching the abbreviation house style.
        private static string Scaled(double value)
        {
            double abs = Math.Abs(value);
            string format = abs < 10 ? "0.##" : abs < 100 ? "0.#" : "0";
            return value.ToString(format, CultureInfo.InvariantCulture);
        }
    }
}
