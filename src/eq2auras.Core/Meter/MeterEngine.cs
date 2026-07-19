using System;
using System.Collections.Generic;
using System.Linq;
using Eq2Auras.Core.Timers;

namespace Eq2Auras.Core.Meter
{
    /// The meter-side sibling of OverlayEngine (SPEC Part III §Assembly split):
    /// per-poll readings in, one renderable frame out. Owns its OWN PaletteAssigner —
    /// ally names and timer names are disjoint namespaces; sharing one first-fired
    /// sequence would let ally names shift the shipped timer slot assignments
    /// (SPEC Part III §The meter window — Rows).
    public sealed class MeterEngine
    {
        private readonly PaletteAssigner _palette = new PaletteAssigner();

        public MeterFrame Tick(EncounterReading encounter, List<CombatantReading> combatants,
            string metricKey, IReadOnlyList<int> paletteArgb, string secondaryKey = null)
        {
            var metric = MetricRegistry.ResolvePrimary(metricKey);
            if (metric == null)
            {
                // Cleared primary (SPEC Part III §Meter display defaults): show nothing but the
                // backdrop — no rows, blank metric/total. The window still renders its header.
                return new MeterFrame
                {
                    Rows = new List<MeterRow>(),
                    DurationText = FormatDuration(EncounterDuration(encounter)),
                    Title = encounter != null && encounter.Exists ? (encounter.Title ?? "") : "",
                    MetricLabel = "",
                    SecondaryLabel = "",
                    TotalText = "",
                };
            }
            var secondary = MetricRegistry.Find(secondaryKey);   // null -> no secondary

            // Live wall clock while active, finalized log time once ended (SPEC Part III
            // §Rates come from our wall clock). Clamp defends the degenerate
            // fresh-encounter poll: StartTime == DateTime.MaxValue makes the live
            // estimate hugely negative before the first swing lands.
            double duration = EncounterDuration(encounter);

            // One duration-policy site for the primary and the secondary alike (SPEC Part
            // III §The metric registry — rate ÷ wall-clock duration, or raw count).
            double Compute(MetricDef def, CombatantReading c)
            {
                double raw = def.Select(c);
                return def.IsRate ? (duration > 0 ? raw / duration : 0) : raw;
            }

            // Mirror ACT's mini parse combatant selection (SPEC Part III §Displayed
            // combatants). Base set = every combatant; ShowOnlyAllies hides non-allies
            // BUT only when the ally set is non-empty (ACT's escape hatch) — so before
            // the user acts (no allies classified) every combatant shows, mob included,
            // which self-heals the instant the user engages. "Unknown" is always dropped.
            var all = combatants ?? new List<CombatantReading>();
            bool anyAlly = all.Any(c => c.IsAlly);   // ACT's `list2.Count > 0` escape-hatch guard

            var rows = new List<MeterRow>();
            double total = 0;
            foreach (var combatant in all)
            {
                if (combatant.Name == "Unknown") continue;
                if (anyAlly && !combatant.IsAlly) continue;

                double value = Compute(metric, combatant);
                total += value;
                rows.Add(new MeterRow
                {
                    Name = combatant.Name ?? "",
                    Value = value,
                    Secondaries = secondary != null
                        ? new List<SecondaryValue> { new SecondaryValue { Key = secondary.Key, FormattedValue = secondary.Format(Compute(secondary, combatant)) } }
                        : new List<SecondaryValue>(),
                });
            }

            rows.Sort((a, b) => a.Value != b.Value
                ? b.Value.CompareTo(a.Value)
                : string.CompareOrdinal(a.Name, b.Name));   // deterministic tie-break, never epsilon-seeding
            double top = rows.Count > 0 ? rows[0].Value : 0;
            // NO truncation: every ally travels; visibility is the window's scroll
            // concern (SPEC Part III §The meter window — Scrolling), never the data's.

            foreach (var row in rows)
            {
                row.Percent = total > 0 ? row.Value / total : 0;     // share of the displayed set
                row.FormattedPercent = Math.Round(row.Percent * 100) + "%";
                row.BarFraction = top > 0 ? row.Value / top : 0;     // rank 1 = full bar
                row.FormattedValue = metric.Format(row.Value);
                row.FillArgb = paletteArgb[_palette.IndexFor(row.Name) % paletteArgb.Count];
            }

            return new MeterFrame
            {
                Rows = rows,
                DurationText = FormatDuration(duration),
                Title = encounter != null && encounter.Exists ? (encounter.Title ?? "") : "",
                MetricLabel = metric.Label,
                SecondaryLabel = secondary != null ? secondary.Label : "",
                TotalText = metric.Format(total),
            };
        }

        /// Live wall clock while active, finalized log time once ended (SPEC Part III §Rates come
        /// from our wall clock). Clamp defends the degenerate fresh-encounter poll where
        /// StartTime == DateTime.MaxValue makes the live estimate hugely negative before the first swing.
        private static double EncounterDuration(EncounterReading encounter)
        {
            if (encounter == null || !encounter.Exists) return 0;
            return Math.Max(0, encounter.Active ? encounter.LiveDurationSeconds : encounter.FinalDurationSeconds);
        }

        internal static string FormatDuration(double seconds)
        {
            int t = (int)Math.Max(0, seconds);
            return (t / 60) + ":" + (t % 60).ToString("00");
        }
    }
}
