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
    public enum EscalationStyle { CenterRadial = 0, HighlightInPlace = 1 }
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

        [DataMember(Name = "buffPrefs")]
        public List<BuffPref> BuffPrefs { get; set; }   // null = never set -> backfilled all-on (SPEC §Buff tracking)

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

            // null = never set -> default the whole curated set on (harmless without macros), no
            // overrides. A non-null list is the raider's choices (incl. an explicit empty = all off).
            if (BuffPrefs == null)
                BuffPrefs = BuffCatalog.All.Select(b => new BuffPref { Id = b.Id, Enabled = true }).ToList();

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
            ColorSource = Panels[0].ColorSource;         // legacy mirror: an older build
            EscalationStyle = Panels[0].EscalationStyle; // reads the flat knobs as Panel A's

            var serializer = new DataContractJsonSerializer(typeof(Settings));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, this);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
    }
}
