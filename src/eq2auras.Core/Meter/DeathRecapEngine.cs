using System;
using System.Collections.Generic;

namespace Eq2Auras.Core.Meter
{
    /// The Death Recap surface (SPEC §Death Recap): a victim's incoming events over the last 10 s
    /// → one row per active second, health reconstructed BACKWARD from 0-at-death as a fraction of
    /// the max-health estimate, clamped. NOT BreakdownEngine (ranked) — a chronological HP track.
    public static class DeathRecapEngine
    {
        public const int DmgArgb = unchecked((int)0xFFF2A0A0);   // red
        public const int HealArgb = unchecked((int)0xFF2FBF8F);  // green
        private const int WindowSeconds = 10;
        private static readonly int FillArgb = MeterFamilyColors.ArgbFor("Damage");

        public static List<MeterRow> Build(RecapReading reading)
        {
            var rows = new List<MeterRow>();
            if (reading?.Events == null) return rows;

            // Bucket by whole second before death (0..WindowSeconds-1), summing dmg/heals.
            var dmg = new double[WindowSeconds];
            var heal = new double[WindowSeconds];
            var present = new bool[WindowSeconds];
            foreach (var e in reading.Events)
            {
                int s = (int)Math.Floor(e.SecondsBeforeDeath);
                if (s < 0 || s >= WindowSeconds) continue;
                if (e.IsHeal) heal[s] += e.Amount; else dmg[s] += e.Amount;
                present[s] = true;
            }

            // HP at END of second s = cumulative net damage over seconds [0 .. s-1]
            // (the net damage that then killed them across the later seconds). Death second (0) = 0.
            double max = reading.MaxHealthEstimate;
            double cumulativeNetDamage = 0;
            for (int s = 0; s < WindowSeconds; s++)
            {
                if (present[s])
                {
                    double clamped = Math.Max(0, Math.Min(max, cumulativeNetDamage));
                    double pct = max > 0 ? clamped / max : 0;
                    rows.Add(new MeterRow
                    {
                        Name = s == 0 ? "0s" : "-" + s + "s",
                        Value = clamped,
                        FormattedValue = NumberFormat.Abbreviate(clamped),
                        Percent = pct,
                        FormattedPercent = Math.Round(pct * 100) + "%",
                        BarFraction = pct,
                        FillArgb = FillArgb,
                        Secondaries = BuildSecondaries(dmg[s], heal[s]),
                    });
                }
                cumulativeNetDamage += dmg[s] - heal[s];   // roll back one more second
            }

            rows.Reverse();   // oldest second first, death (0s) last
            return rows;
        }

        private static List<SecondaryValue> BuildSecondaries(double dmg, double heal)
        {
            return new List<SecondaryValue>
            {
                new SecondaryValue { Key = "dmg",  FormattedValue = dmg > 0 ? NumberFormat.SignedAbbreviate(-dmg) : "—", Argb = DmgArgb },
                new SecondaryValue { Key = "heal", FormattedValue = heal > 0 ? NumberFormat.SignedAbbreviate(heal) : "—", Argb = HealArgb },
            };
        }
    }
}
