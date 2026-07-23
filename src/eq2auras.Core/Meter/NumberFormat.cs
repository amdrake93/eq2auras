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

        /// "M:SS" — the single M:SS formatter (MeterEngine.FormatDuration delegates here).
        public static string Mmss(double seconds)
        {
            int t = (int)Math.Max(0, seconds);
            return (t / 60) + ":" + (t % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        /// Signed K/M/B for the recap's per-second net columns: "+1K" / "-4.2K"; "0" at zero.
        public static string SignedAbbreviate(double value)
        {
            if (Math.Round(value) == 0) return "0";
            string sign = value > 0 ? "+" : "-";
            return sign + Abbreviate(Math.Abs(value));
        }

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
