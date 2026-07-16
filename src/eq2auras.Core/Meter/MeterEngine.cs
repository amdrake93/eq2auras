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
            string metricKey, IReadOnlyList<int> paletteArgb)
        {
            var metric = MetricRegistry.Resolve(metricKey);

            // Live wall clock while active, finalized log time once ended (SPEC Part III
            // §Rates come from our wall clock). Clamp defends the degenerate
            // fresh-encounter poll: StartTime == DateTime.MaxValue makes the live
            // estimate hugely negative before the first swing lands.
            double duration = 0;
            if (encounter != null && encounter.Exists)
            {
                duration = Math.Max(0, encounter.Active
                    ? encounter.LiveDurationSeconds
                    : encounter.FinalDurationSeconds);
            }

            // Mirror ACT's mini parse combatant selection (SPEC Part III §Displayed
            // combatants). Base set = every combatant; ShowOnlyAllies hides non-allies
            // BUT only when the ally set is non-empty (ACT's escape hatch) — so before
            // the user acts (no allies classified) every combatant shows, mob included,
            // which self-heals the instant the user engages. "Unknown" is always dropped.
            var all = combatants ?? new List<CombatantReading>();
            bool anyAlly = false;
            foreach (var c in all)
            {
                if (c.IsAlly) { anyAlly = true; break; }
            }

            var rows = new List<MeterRow>();
            double total = 0;
            foreach (var combatant in all)
            {
                if (combatant.Name == "Unknown") continue;
                if (anyAlly && !combatant.IsAlly) continue;

                double raw = metric.Select(combatant);
                double value = metric.IsRate
                    ? (duration > 0 ? raw / duration : 0)
                    : raw;
                total += value;
                rows.Add(new MeterRow { Name = combatant.Name ?? "", Value = value });
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
                row.Secondaries = new List<SecondaryValue>();        // shape ships in slice 1; selection UX is slice 2
            }

            return new MeterFrame
            {
                Rows = rows,
                DurationText = FormatDuration(duration),
                Title = encounter != null && encounter.Exists ? (encounter.Title ?? "") : "",
                MetricLabel = metric.Label,
                TotalText = metric.Format(total),
            };
        }

        internal static string FormatDuration(double seconds)
        {
            int t = (int)Math.Max(0, seconds);
            return (t / 60) + ":" + (t % 60).ToString("00");
        }
    }
}
