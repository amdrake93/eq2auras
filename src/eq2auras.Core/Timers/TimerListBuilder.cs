using System;
using System.Collections.Generic;
using System.Linq;

namespace Eq2Auras.Core.Timers
{
    /// Turns raw per-instance readings into the sorted calm-list rows.
    /// Escalation pivots on each timer's own WarningValue (SPEC: we do not invent
    /// thresholds); fallbacks per SPEC when the timer lacks a usable one.
    public static class TimerListBuilder
    {
        public static List<TimerRow> Build(IEnumerable<TimerReading> readings, bool includeOverdue = false)
        {
            // TimeLeft <= 0 excluded by default (CenterRadial shows overdue as center
            // LATE cards). HighlightInPlace mode includes linger-configured overdue
            // timers as rows; ascending sort naturally puts them (negative) first.
            return readings
                .Where(r => r.TimeLeft > 0 || (includeOverdue && r.RemoveValueSeconds < 0))
                .Select(ToRow)
                .OrderBy(r => r.TimeLeft)
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// The row's displayed label. A buff in OUR category renders in one of two mutually-exclusive
        /// formats (SPEC §Display) — single-target `{Buff} → {Target}`, group-wide `{Caster}: {Buff}`.
        /// The format is **category-scoped**, not name-scoped: a raider's own timer that merely shares
        /// a catalog buff's name (their own `Holy Shield`) is NOT relabeled — only our injected readings
        /// (category `eq2auras Buffs`) are. Everything else renders its bare name unchanged.
        public static string Label(string name, string combatant, string category)
        {
            if (!string.Equals(category, BuffCatalog.Category, StringComparison.OrdinalIgnoreCase)) return name;
            var def = BuffCatalog.FindByName(name);
            if (def == null) return name;
            var who = TitleCase(combatant);
            if (string.IsNullOrEmpty(who)) return name;   // defensive: no captured name
            return def.IsTargeted ? name + " → " + who : who + ": " + name;
        }

        // Single-token EQ2 names: upper first, lower rest. "none"/"" -> "" so callers treat it as absent.
        private static string TitleCase(string s)
        {
            if (string.IsNullOrWhiteSpace(s) || s.Equals("none", StringComparison.OrdinalIgnoreCase)) return "";
            return char.ToUpperInvariant(s[0]) + s.Substring(1).ToLowerInvariant();
        }

        private static TimerRow ToRow(TimerReading reading)
        {
            return new TimerRow
            {
                Name = reading.Name,
                Combatant = reading.Combatant,
                Category = reading.Category,
                TimeLeft = reading.TimeLeft,
                PreciseTimeLeft = TimerMath.PreciseOf(reading),
                TotalSeconds = reading.TotalSeconds,
                FillFraction = FillFraction(reading),
                FillArgb = reading.FillArgb,
                Urgency = UrgencyOf(reading)
            };
        }

        private static TimerUrgency UrgencyOf(TimerReading reading)
        {
            if (reading.TimeLeft <= 0) return TimerUrgency.Overdue;
            return reading.TimeLeft <= TimerMath.EffectiveWarning(reading) ? TimerUrgency.Imminent : TimerUrgency.Calm;
        }

        private static double FillFraction(TimerReading reading)
        {
            if (reading.TotalSeconds <= 0) return 0;
            var fraction = reading.TimeLeft / (double)reading.TotalSeconds;
            return Math.Max(0, Math.Min(1, fraction));
        }
    }
}
