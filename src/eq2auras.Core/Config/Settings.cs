using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Eq2Auras.Core.Timers;

namespace Eq2Auras.Core.Config
{
    // ⚠ Knob enums: the DEFAULT must be the 0-value. DCJS creates objects without
    // running initializers, so a field missing from an old settings.json comes back
    // as 0 — which must mean "the default".
    public enum ColorSource { Palette = 0, Greyscale = 1, ActColor = 2 }
    public enum EscalationStyle { CenterRadial = 0, HighlightInPlace = 1, None = 2 }
    public enum GrowDirection { Down = 0, Up = 1 }

    /// The knob store (SPEC §Configuration): one plain object, every tunable a typed
    /// member with a baked-in default. Serialized with DCJS (never System.Web.Extensions
    /// — it breaks the WPF markup compiler). Unknown fields in the file are ignored;
    /// missing fields fall back to defaults — settings files survive version skew both ways.
    [DataContract]
    public sealed class Settings
    {
        [DataMember(Name = "colorSource")]
        public ColorSource ColorSource { get; set; } = ColorSource.Palette;

        [DataMember(Name = "escalationStyle")]
        public EscalationStyle EscalationStyle { get; set; } = EscalationStyle.CenterRadial;

        [DataMember(Name = "debugLogging")]
        public bool DebugLogging { get; set; }   // global knob (SPEC §Diagnostic logging): off = lifecycle events only

        [DataMember(Name = "betaChannel")]
        public bool BetaChannel { get; set; }    // global knob (SPEC §Two channels): false (0-value) = stable channel

        [DataMember(Name = "buffEscalationReset")]
        public bool BuffEscalationReset { get; set; }   // one-shot: false in pre-amendment files -> migrate the buff window's escalation once

        public const int GroupCount = 3;   // the three SEEDED groups: panel:1, panel:2, category:"eq2auras Buffs"
        public const int MaxPaletteSize = 16;
        public const double MinRowWidth = 100, MaxRowWidth = 800;
        public const double MinRowHeight = 16, MaxRowHeight = 100;
        public const double MinRadialSize = 40, MaxRadialSize = 400;
        public const double MinRowSpacing = 0, MaxRowSpacing = 50;

        [DataMember(Name = "paletteArgb")]
        public List<int> PaletteArgb { get; set; } = DefaultPalette();

        private static List<int> DefaultPalette()
            => new List<int>(Timers.ColorPolicy.DefaultPaletteArgb);

        [DataMember(Name = "panels")]
        public List<PanelSettings> Panels { get; set; } = DefaultPanels();

        [DataMember(Name = "meter")]
        public MeterSettings Meter { get; set; } = new MeterSettings();

        // Field initializer (like Panels) so a directly-constructed Settings — new Settings() from
        // SettingsStore.Load()'s missing-file branch and Parse()'s corrupt-file catch — defaults
        // all-on. DCJS skips the initializer on deserialize, so a loaded file still flows through
        // Normalize's null->all-on backfill (SPEC §Buff tracking).
        [DataMember(Name = "buffPrefs")]
        public List<BuffPref> BuffPrefs { get; set; } = DefaultBuffPrefs();

        private static List<BuffPref> DefaultBuffPrefs()
            => BuffCatalog.All.Select(b => new BuffPref { Id = b.Id, Enabled = true }).ToList();

        public IEnumerable<string> EnabledBuffIds()
            => (BuffPrefs ?? new List<BuffPref>()).Where(p => p != null && p.Enabled).Select(p => p.Id);

        public int EffectiveDuration(string id)
        {
            var pref = (BuffPrefs ?? new List<BuffPref>()).FirstOrDefault(p => p != null && p.Id == id);
            var def = BuffCatalog.Find(id);
            return pref?.DurationOverride ?? def?.DurationSeconds ?? 0;
        }

        private static List<PanelSettings> DefaultPanels() => SeededGroups(new List<PanelSettings>());

        // Pad UP to the three seeded groups and seed each seeded group's source when unset. Does NOT
        // truncate: a hand-authored 4th+ group survives and routes by its own rule (SPEC §Timer groups —
        // "a new destination is a new config entry"; v1 withholds only the authoring UI). Runs on BOTH
        // construction (DefaultPanels) and load (Normalize) so new Settings() never routes nothing.
        // Only indices 0-2 get a DEFAULT source: a 4th+ group with no Sources of its own matches
        // nothing (intended — it's the author's job to give it a rule).
        private static List<PanelSettings> SeededGroups(List<PanelSettings> panels)
        {
            while (panels.Count < GroupCount) panels.Add(new PanelSettings());
            SeedSources(panels[0], SourceRule.Panel(1));
            SeedSources(panels[1], SourceRule.Panel(2));
            SeedSources(panels[2], SourceRule.OfCategory(BuffCatalog.Category));
            return panels;
        }

        private static void SeedSources(PanelSettings group, SourceRule seed)
        {
            if (group.Sources == null || group.Sources.Count == 0)
                group.Sources = new List<SourceRule> { seed };
        }

        /// DCJS skips initializers, so a deserialized instance may carry a null or
        /// wrong-length panel list. Normalizes to exactly GroupCount entries. A legacy
        /// flat file (no panels key at all) seeds Panel A from its top-level knobs;
        /// Panel B starts at defaults (SPEC §Configuration).
        private void Normalize()
        {
            bool legacyFile = Panels == null;

            Panels = SeededGroups((Panels ?? new List<PanelSettings>()).Where(p => p != null).ToList());

            if (legacyFile)
            {
                Panels[0].ColorSource = ColorSource;
                Panels[0].EscalationStyle = EscalationStyle;
            }

            if (PaletteArgb == null || PaletteArgb.Count == 0) PaletteArgb = DefaultPalette();
            if (PaletteArgb.Count > MaxPaletteSize) PaletteArgb = PaletteArgb.Take(MaxPaletteSize).ToList();

            if (Meter == null) Meter = new MeterSettings();   // DCJS skips initializers
            Meter.Normalize();

            if (BuffPrefs == null)
            {
                BuffPrefs = DefaultBuffPrefs();   // never-set -> the whole curated set on (harmless without macros)
            }
            else
            {
                // Forward-compat: a catalog buff absent from the raider's saved list defaults OFF —
                // they've customized, so a newer version's new buff isn't auto-enabled (distinct from
                // the never-set all-on default). Guarantees every catalog id has a pref, so BuffPrefFor
                // (the tab UI) is a pure lookup that can't miss.
                BuffPrefs = BuffPrefs.Where(p => p != null).ToList();
                var have = new HashSet<string>(BuffPrefs.Select(p => p.Id), StringComparer.OrdinalIgnoreCase);
                foreach (var def in BuffCatalog.All)
                    if (!have.Contains(def.Id)) BuffPrefs.Add(new BuffPref { Id = def.Id, Enabled = false });
            }

            // Clamp discipline (like the panel dimensions): an absurd/hand-edited override would throw
            // in the tab's NumericUpDown (Min 1, Max 3600) during InitPlugin -> the whole plugin fails
            // to load. Out of range -> revert to the catalog base (null).
            foreach (var pref in BuffPrefs)
                if (pref.DurationOverride.HasValue && (pref.DurationOverride.Value < 1 || pref.DurationOverride.Value > 3600))
                    pref.DurationOverride = null;

            // One-time: a pre-amendment buff window carries the escalating default (escalationStyle:0),
            // indistinguishable by value from a later explicit CenterRadial pick — so the MARKER, not the
            // value, decides. First load (marker false): null the buff group's escalation (-> resolves to
            // None) and set the marker; thereafter leave it, so a user's later pick persists (SPEC §Configuration).
            if (!BuffEscalationReset)
            {
                var buffGroup = Panels.FirstOrDefault(p => p.Sources != null
                    && p.Sources.Any(r => r != null && r.Type == SourceRuleType.Category
                        && string.Equals(r.Value, BuffCatalog.Category, StringComparison.OrdinalIgnoreCase)));
                if (buffGroup != null) buffGroup.EscalationStyle = null;
                BuffEscalationReset = true;
            }

            // Assign only when out of range: the engine reads these fields per tick /
            // per restyle on other threads — a valid value must never be rewritten.
            foreach (var panel in Panels)
            {
                if (OutOfRange(panel.RowWidth, MinRowWidth, MaxRowWidth))
                    panel.RowWidth = Math.Min(MaxRowWidth, Math.Max(MinRowWidth, panel.RowWidth.Value));
                if (OutOfRange(panel.RowHeight, MinRowHeight, MaxRowHeight))
                    panel.RowHeight = Math.Min(MaxRowHeight, Math.Max(MinRowHeight, panel.RowHeight.Value));
                if (OutOfRange(panel.RadialSize, MinRadialSize, MaxRadialSize))
                    panel.RadialSize = Math.Min(MaxRadialSize, Math.Max(MinRadialSize, panel.RadialSize.Value));
                if (OutOfRange(panel.RowSpacing, MinRowSpacing, MaxRowSpacing))
                    panel.RowSpacing = Math.Min(MaxRowSpacing, Math.Max(MinRowSpacing, panel.RowSpacing.Value));
            }
        }

        private static bool OutOfRange(double? value, double min, double max)
            => value.HasValue && (value.Value < min || value.Value > max);

        public static Settings Parse(string json)
        {
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(Settings));
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    var settings = (Settings)serializer.ReadObject(stream) ?? new Settings();
                    settings.Normalize();
                    return settings;
                }
            }
            catch
            {
                return new Settings();   // empty/corrupt/foreign file -> defaults
            }
        }

        public string ToJson()
        {
            Normalize();
            ColorSource = Panels[0].ColorSource;                            // legacy mirror: an older build
            EscalationStyle = Panels[0].EscalationStyle ?? EscalationStyle.CenterRadial; // reads the flat knobs as Panel A's

            var serializer = new DataContractJsonSerializer(typeof(Settings));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, this);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
    }
}
