using System;
using System.Collections.Generic;
using System.Linq;
using Advanced_Combat_Tracker;
using Eq2Auras.Core.Config;
using Eq2Auras.Core.Timers;

namespace Eq2Auras.Plugin.Act
{
    /// Registers each enabled buff as a TimerData def (Name = the buff's display name, so the
    /// frame renders the buff; our reserved Category is the management namespace) + a transient
    /// ActiveCustomTriggers entry keyed by a string WE own. Defs persist; triggers are runtime-only
    /// and re-ensured against zone-rebuild eviction (SPEC §Buff tracking; decompile-verified ACT 3.8.5.288).
    public sealed class BuffInjector
    {
        private const string Category = BuffCatalog.Category;            // "eq2auras Buffs" — our namespace
        private const string KeyPrefix = "eq2auras:";                    // our ActiveCustomTriggers key namespace
        private static string DictKey(BuffDef b) => KeyPrefix + b.Id;    // OUR key — never a reconstructed CustomTrigger.Key

        /// Reconcile ACT's live state to the enabled prefs at their EFFECTIVE durations. Called on
        /// init (after SweepAll) and on every toggle/override change. Idempotent.
        public void SyncTo(Settings settings)
        {
            var desired = BuffSync.Desired(settings.EnabledBuffIds());
            var desiredNames = new HashSet<string>(desired.Select(b => b.DisplayName), StringComparer.OrdinalIgnoreCase);

            // Withdraw any of OUR category defs no longer desired (matched by name — no catalog lookup).
            foreach (var td in OurDefs().Where(t => !desiredNames.Contains(t.Name)).ToList())
                Withdraw(td);

            // Upsert EVERY desired def at its effective duration — AddEditTimerDef edits-or-adds, so an
            // override change on an already-tracked buff propagates (not just newly-enabled ones) — and
            // ensure its trigger.
            foreach (var def in desired)
            {
                ActGlobals.oFormSpellTimers.AddEditTimerDef(new TimerData(def.DisplayName, false, settings.EffectiveDuration(def.Id), false, false, "", "", 5, true) { Category = Category });
                ActGlobals.oFormActMain.ActiveCustomTriggers[DictKey(def)] = BuildTrigger(def);
            }
            ActGlobals.oFormSpellTimers.RebuildSpellTreeView();
        }

        /// Cheap per-poll self-heal: re-add any desired trigger a zone rebuild evicted (defs persist).
        public void EnsurePresent(Settings settings)
        {
            foreach (var def in BuffSync.Desired(settings.EnabledBuffIds()))
                if (!ActGlobals.oFormActMain.ActiveCustomTriggers.ContainsKey(DictKey(def)))
                    ActGlobals.oFormActMain.ActiveCustomTriggers[DictKey(def)] = BuildTrigger(def);
        }

        /// Remove EVERY def in our reserved category + every trigger we keyed — regardless of whether
        /// a def's name still maps to a current catalog id — so a renamed/removed buff's leftover is
        /// swept, never a zombie. Used on InitPlugin (clean slate before SyncTo) and DeInitPlugin.
        public void SweepAll()
        {
            foreach (var td in OurDefs().ToList())
                ActGlobals.oFormSpellTimers.RemoveTimerDef(td);
            foreach (var key in ActGlobals.oFormActMain.ActiveCustomTriggers.Keys.Where(k => k.StartsWith(KeyPrefix)).ToList())
                ActGlobals.oFormActMain.ActiveCustomTriggers.Remove(key);
            ActGlobals.oFormSpellTimers.RebuildSpellTreeView();
        }

        // Case-insensitive: TimerData defs round-trip through ACT's persisted XML across sessions,
        // so we don't assume ACT preserves the exact casing we wrote.
        private static IEnumerable<TimerData> OurDefs()
            => ActGlobals.oFormSpellTimers.TimerDefs.Values
                .Where(td => string.Equals(td.Category, Category, StringComparison.OrdinalIgnoreCase));

        private static void Withdraw(TimerData td)
        {
            ActGlobals.oFormSpellTimers.RemoveTimerDef(td);
            foreach (var key in ActGlobals.oFormActMain.ActiveCustomTriggers
                        .Where(e => string.Equals(e.Value.Category, Category, StringComparison.OrdinalIgnoreCase)
                                 && string.Equals(e.Value.TimerName, td.Name, StringComparison.OrdinalIgnoreCase))
                        .Select(e => e.Key).ToList())
                ActGlobals.oFormActMain.ActiveCustomTriggers.Remove(key);
        }

        private static CustomTrigger BuildTrigger(BuffDef def)
            => new CustomTrigger(def.Pattern, 0, "", true, def.DisplayName, false) { Category = Category };
    }
}
