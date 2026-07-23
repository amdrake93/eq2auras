using System.Collections.Generic;

namespace Eq2Auras.Core.Meter
{
    /// One display row (SPEC Part III §The metric registry — two-tier): the primary
    /// metric drives Value/bar/sort; Secondaries is the slice-2 growth point — the
    /// shape ships now, slice 1 renders primary-only.
    public sealed class MeterRow
    {
        public string Name { get; set; }
        public double Value { get; set; }
        public string FormattedValue { get; set; }
        public double Percent { get; set; }          // share of ALL allies' total (0..1)
        public string FormattedPercent { get; set; }
        public double BarFraction { get; set; }      // vs. rank-1's value (0..1) — rank 1 = full bar
        public int FillArgb { get; set; }
        public List<SecondaryValue> Secondaries { get; set; }
        public string Detail { get; set; }     // muted suffix after Name (Deaths: "(N) · killing blow + dmg"); null on normal rows
        public string DrillKey { get; set; }   // per-row drill identity; null → drill by Name (Deaths: two rows can share a victim name)
    }

    public sealed class SecondaryValue
    {
        public string Key { get; set; }
        public string FormattedValue { get; set; }
        public int? Argb { get; set; }         // optional column color (recap dmg=red/heals=green); null → subordinate grey
    }

    /// Everything a meter window renders for one poll: header + rows. No ACT/WPF types.
    public sealed class MeterFrame
    {
        public List<MeterRow> Rows { get; set; }
        public string DurationText { get; set; }   // "3:24"
        public string MetricLabel { get; set; }    // "DPS" — primary metric name (header: white, the left identity)
        public string SecondaryLabel { get; set; } // "HPS" — secondary metric label (header: subordinate grey); "" when none
        public string TotalText { get; set; }      // all-allies total, metric-formatted
    }
}
