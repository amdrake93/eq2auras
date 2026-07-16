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

        private static string Scaled(double value)
            => value.ToString("0.#", CultureInfo.InvariantCulture);   // one decimal, trailing zero dropped
    }
}
