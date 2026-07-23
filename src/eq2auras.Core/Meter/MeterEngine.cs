using System;
using System.Collections.Generic;
using System.Linq;

namespace Eq2Auras.Core.Meter
{
    /// The meter-side sibling of OverlayEngine (SPEC Part III §Assembly split):
    /// per-poll readings in, one renderable frame out. Row fill is the primary metric's
    /// family color (MeterFamilyColors, keyed by MetricDef.Category) — monochromatic per
    /// window, no palette (SPEC Part III §Rows).
    public sealed class MeterEngine
    {
        public MeterFrame Tick(EncounterReading encounter, List<CombatantReading> combatants,
            string metricKey, string secondaryKey = null, MeterScope scope = MeterScope.Allies)
        {
            var metric = MetricRegistry.ResolvePrimary(metricKey);
            if (metric == null)
            {
                // Cleared primary (SPEC Part III §Meter display defaults): show nothing but the
                // backdrop — no rows, blank metric/total. The window still renders its header.
                return new MeterFrame
                {
                    Rows = new List<MeterRow>(),
                    DurationText = FormatDuration(DurationSeconds(encounter)),
                    MetricLabel = "",
                    SecondaryLabel = "",
                    TotalText = "",
                };
            }
            var secondary = MetricRegistry.Find(secondaryKey);   // null -> no secondary
            if (secondary != null && secondary.IsEvent) secondary = null;   // event metrics (Deaths) are primary-only — never a per-row secondary (SPEC §Deaths)

            // The primary's identity is the SELECTION label (e.g. "Enemy Damage Taken"),
            // not the bare metric name — SPEC Part III §Header. Falls back to the metric's
            // own label when no selection matches (forward-compat for an unknown (scope, key)).
            var selection = MeterSelections.Resolve(scope, metricKey);
            string metricLabel = selection?.Label ?? metric.Label;

            // Live wall clock while active, finalized log time once ended (SPEC Part III
            // §Rates come from our wall clock). Clamp defends the degenerate
            // fresh-encounter poll: StartTime == DateTime.MaxValue makes the live
            // estimate hugely negative before the first swing lands.
            double duration = DurationSeconds(encounter);

            // One duration-policy site for the primary and the secondary alike (SPEC Part
            // III §The metric registry — rate ÷ wall-clock duration, or raw count).
            double Compute(MetricDef def, CombatantReading c)
            {
                double raw = def.Select(c);
                return def.IsRate ? (duration > 0 ? raw / duration : 0) : raw;
            }

            // Scope selects the population (SPEC Part III §Displayed combatants): Allies mirrors
            // ACT's ShowOnlyAllies filter (hide non-allies only when the ally set is non-empty —
            // ACT's escape hatch, so pre-engage shows everyone); Enemies is the exact inverse
            // (show only non-allies). "Unknown" is always dropped. An unrecognized scope value
            // degrades to Allies (forward-compat, read-site — no persisted clamp).
            var all = combatants ?? new List<CombatantReading>();
            bool enemyScope = scope == MeterScope.Enemies;
            bool anyAlly = all.Any(c => c.IsAlly);

            var rows = new List<MeterRow>();
            double total = 0;
            foreach (var combatant in all)
            {
                if (combatant.Name == "Unknown") continue;
                if (enemyScope)
                {
                    if (combatant.IsAlly) continue;
                }
                else if (anyAlly && !combatant.IsAlly)
                {
                    continue;
                }

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
                row.FillArgb = MeterFamilyColors.ArgbFor(metric.Category);
            }

            return new MeterFrame
            {
                Rows = rows,
                DurationText = FormatDuration(duration),
                MetricLabel = metricLabel,
                SecondaryLabel = secondary != null ? secondary.Label : "",
                TotalText = metric.Format(total),
            };
        }

        /// Live wall clock while active, finalized log time once ended (SPEC Part III §Rates come
        /// from our wall clock). Clamp defends the degenerate fresh-encounter poll where
        /// StartTime == DateTime.MaxValue makes the live estimate hugely negative before the first swing.
        /// Public + static so the drill-down's BreakdownEngine shares the one duration policy.
        public static double DurationSeconds(EncounterReading encounter)
        {
            if (encounter == null || !encounter.Exists) return 0;
            return Math.Max(0, encounter.Active ? encounter.LiveDurationSeconds : encounter.FinalDurationSeconds);
        }

        internal static string FormatDuration(double seconds) => NumberFormat.Mmss(seconds);
    }
}
