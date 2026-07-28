using System.Collections.Generic;

namespace Eq2Auras.Core.Meter
{
    /// The recap-second hover's event log (SPEC §Deaths — the recap-second hover): one hovered
    /// second's incoming events → one MeterRow per swing, CHRONOLOGICAL (by TimeSorter), each a
    /// `Source · Ability` label + a signed amount (red damage / green heal) and a bar scaled to the
    /// second's largest-magnitude event; no percent. A sibling to DeathRecapEngine — a narrative, not
    /// the ranked breakdown engine. Colors are shared with the recap's dmg/heal columns.
    public static class RecapSecondEngine
    {
        public static List<MeterRow> Build(IReadOnlyList<RecapEventDetail> events)
        {
            var rows = new List<MeterRow>();
            if (events == null || events.Count == 0) return rows;

            double top = 0;
            foreach (var e in events)
                if (e.Amount > top) top = e.Amount;

            var ordered = new List<RecapEventDetail>(events);
            ordered.Sort((a, b) => a.Order.CompareTo(b.Order));   // chronological within the second

            foreach (var e in ordered)
            {
                rows.Add(new MeterRow
                {
                    Name = string.IsNullOrEmpty(e.Source) ? (e.Ability ?? "") : e.Source + " · " + e.Ability,
                    FormattedValue = NumberFormat.SignedAbbreviate(e.IsHeal ? e.Amount : -e.Amount),
                    FormattedPercent = "",
                    Percent = 0,
                    BarFraction = top > 0 ? e.Amount / top : 0,
                    FillArgb = e.IsHeal ? DeathRecapEngine.HealArgb : DeathRecapEngine.DmgArgb,
                    Secondaries = new List<SecondaryValue>(),
                });
            }
            return rows;
        }
    }
}
