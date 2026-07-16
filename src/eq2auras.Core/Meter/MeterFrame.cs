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
    }

    public sealed class SecondaryValue
    {
        public string Key { get; set; }
        public string FormattedValue { get; set; }
    }

    /// Everything a meter window renders for one poll: header + rows. No ACT/WPF types.
    public sealed class MeterFrame
    {
        public List<MeterRow> Rows { get; set; }
        public string DurationText { get; set; }   // "3:24"
        public string Title { get; set; }
        public string MetricLabel { get; set; }    // "DPS"
        public string TotalText { get; set; }      // all-allies total, metric-formatted
    }
}
