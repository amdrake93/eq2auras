using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Eq2Auras.Core.Config
{
    // ⚠ Knob enums: the DEFAULT must be the 0-value. DCJS creates objects without
    // running initializers, so a field missing from an old settings.json comes back
    // as 0 — which must mean "the default".
    public enum ColorSource { Palette = 0, Greyscale = 1, ActColor = 2 }
    public enum EscalationStyle { CenterRadial = 0, HighlightInPlace = 1 }

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

        public const int GroupCount = 2;
        public const int MaxPaletteSize = 16;
        public const double MinScale = 0.5;
        public const double MaxScale = 2.5;

        [DataMember(Name = "paletteArgb")]
        public List<int> PaletteArgb { get; set; } = DefaultPalette();

        private static List<int> DefaultPalette()
            => new List<int>(Timers.ColorPolicy.DefaultPaletteArgb);

        [DataMember(Name = "panels")]
        public List<PanelSettings> Panels { get; set; } = DefaultPanels();

        private static List<PanelSettings> DefaultPanels()
        {
            var panels = new List<PanelSettings>();
            for (int i = 0; i < GroupCount; i++)
            {
                panels.Add(new PanelSettings());
            }
            return panels;
        }

        /// DCJS skips initializers, so a deserialized instance may carry a null or
        /// wrong-length panel list. Normalizes to exactly GroupCount entries. A legacy
        /// flat file (no panels key at all) seeds Panel A from its top-level knobs;
        /// Panel B starts at defaults (SPEC §Configuration).
        private void Normalize()
        {
            bool legacyFile = Panels == null;

            Panels = (Panels ?? new List<PanelSettings>()).Where(p => p != null).ToList();
            while (Panels.Count < GroupCount) Panels.Add(new PanelSettings());
            if (Panels.Count > GroupCount) Panels = Panels.Take(GroupCount).ToList();

            if (legacyFile)
            {
                Panels[0].ColorSource = ColorSource;
                Panels[0].EscalationStyle = EscalationStyle;
            }

            if (PaletteArgb == null || PaletteArgb.Count == 0) PaletteArgb = DefaultPalette();
            if (PaletteArgb.Count > MaxPaletteSize) PaletteArgb = PaletteArgb.Take(MaxPaletteSize).ToList();

            // Assign only when out of range: the engine reads these fields per tick /
            // per restyle on other threads — a valid value must never be rewritten.
            foreach (var panel in Panels)
            {
                if (OutOfRange(panel.ListScale)) panel.ListScale = ClampScale(panel.ListScale);
                if (OutOfRange(panel.CenterScale)) panel.CenterScale = ClampScale(panel.CenterScale);
            }
        }

        private static bool OutOfRange(double? scale)
            => scale.HasValue && (scale.Value < MinScale || scale.Value > MaxScale);

        private static double? ClampScale(double? scale)
            => scale.HasValue ? Math.Min(MaxScale, Math.Max(MinScale, scale.Value)) : (double?)null;

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
