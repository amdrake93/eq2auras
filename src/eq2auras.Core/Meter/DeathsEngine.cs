using System;
using System.Collections.Generic;
using System.Globalization;

namespace Eq2Auras.Core.Meter
{
    /// The Deaths special metric's list path (SPEC §Deaths & the Death Recap): death records →
    /// a chronological event timeline, reusing MeterFrame/MeterRow. Not MeterEngine.Tick — rows
    /// are events (value = time-of-death), not ranked combatants.
    public static class DeathsEngine
    {
        private const string Category = "Damage";   // red family color, SPEC §Deaths

        public static MeterFrame BuildList(IReadOnlyList<DeathRecord> deaths, double durationSeconds)
        {
            var rows = new List<MeterRow>();
            int fill = MeterFamilyColors.ArgbFor(Category);

            if (deaths != null)
            {
                foreach (var d in deaths)
                {
                    double frac = durationSeconds > 0
                        ? Math.Max(0, Math.Min(1, d.TimeOfDeathSeconds / durationSeconds))
                        : 0;
                    string blow = string.IsNullOrEmpty(d.KillingBlowAbility)
                        ? "—"
                        : d.KillingBlowAbility + " " + NumberFormat.Abbreviate(d.KillingBlowDamage);
                    rows.Add(new MeterRow
                    {
                        Name = d.Victim,
                        Detail = "(" + d.Ordinal + ") · " + blow,
                        DrillKey = d.DrillKey,
                        Value = d.TimeOfDeathSeconds,
                        FormattedValue = NumberFormat.Mmss(d.TimeOfDeathSeconds),
                        Percent = frac,
                        FormattedPercent = Math.Round(frac * 100) + "%",
                        BarFraction = frac,
                        FillArgb = fill,
                        Secondaries = new List<SecondaryValue>(),
                    });
                }
            }

            rows.Sort((a, b) => a.Value != b.Value
                ? a.Value.CompareTo(b.Value)                       // chronological ASC (earliest first)
                : string.CompareOrdinal(a.DrillKey, b.DrillKey));  // stable tie-break

            return new MeterFrame
            {
                Rows = rows,
                DurationText = NumberFormat.Mmss(durationSeconds),
                MetricLabel = "Deaths",
                SecondaryLabel = "",
                TotalText = rows.Count.ToString(CultureInfo.InvariantCulture),
            };
        }
    }
}
