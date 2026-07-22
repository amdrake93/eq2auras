using System;
using System.Collections.Generic;

namespace Eq2Auras.Core.Meter
{
    /// Turns a drilled combatant's (label, raw-value) list into ranked MeterRows — the
    /// surface-agnostic sibling of MeterEngine's row math (SPEC Part III §Row drill-down).
    /// Reuses the MeterRow DTO (same row/bar visual, no secondary column). The same duration
    /// policy as MeterEngine (rate ÷ wall-clock, total raw), so per-ability values sum to the
    /// combatant's own total; percent = share of THAT sum (duration cancels for rates).
    public static class BreakdownEngine
    {
        public static List<MeterRow> Build(IReadOnlyList<BreakdownEntry> entries, MetricDef metric, double durationSeconds)
        {
            var rows = new List<MeterRow>();
            if (entries == null || metric == null) return rows;

            double total = 0;
            foreach (var entry in entries)
            {
                double value = metric.IsRate
                    ? (durationSeconds > 0 ? entry.Value / durationSeconds : 0)
                    : entry.Value;
                total += value;
                rows.Add(new MeterRow
                {
                    Name = entry.Label ?? "",
                    Value = value,
                    Secondaries = new List<SecondaryValue>(),   // drill rows carry no secondary (SPEC §Row drill-down)
                });
            }

            rows.Sort((a, b) => a.Value != b.Value
                ? b.Value.CompareTo(a.Value)
                : string.CompareOrdinal(a.Name, b.Name));   // same deterministic tie-break as the list
            double top = rows.Count > 0 ? rows[0].Value : 0;

            foreach (var row in rows)
            {
                row.Percent = total > 0 ? row.Value / total : 0;
                row.FormattedPercent = Math.Round(row.Percent * 100) + "%";
                row.BarFraction = top > 0 ? row.Value / top : 0;
                row.FormattedValue = metric.Format(row.Value);
                row.FillArgb = MeterFamilyColors.ArgbFor(metric.Category);
            }
            return rows;
        }
    }
}
