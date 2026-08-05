using System.Runtime.Serialization;
using Eq2Auras.Core.Meter;

namespace Eq2Auras.Core.Config
{
    /// One meter window's persisted config (SPEC Part III §Multiple windows, §Settings).
    /// Positions nullable on purpose — DCJS materializes a missing numeric as 0, a real
    /// screen corner, so null (never zero) means "unset, use the default placement",
    /// same convention as PanelSettings. Increment 1 carries metric/position/lock; the
    /// appearance knobs (row height, font, opacity) and explicit size (width, visible
    /// rows) arrive with their own increments.
    [DataContract]
    public sealed class MeterWindowConfig
    {
        [DataMember(Name = "metricKey")]
        public string MetricKey { get; set; }   // null/unknown -> registry default at resolve time

        [DataMember(Name = "secondaryKey")]
        public string SecondaryKey { get; set; }   // null/unknown -> no secondary (off), resolved at the engine via MetricRegistry.Find

        [DataMember(Name = "scope")]
        public MeterScope Scope { get; set; } = MeterScope.Allies;   // the PRIMARY's scope; 0-value survives DCJS (no initializer on deserialize). Unknown values degrade to Allies at the engine read site.

        [DataMember(Name = "segmentMode")]
        public SegmentMode SegmentMode { get; set; } = SegmentMode.Current;   // 0-value Current survives DCJS; a legacy/absent value lands on Current.

        // Inverted per the DCJS 0-value rule: false = knob ON (auto-return, the default);
        // true = pinned (the window stays on its selection). SPEC §Settings.
        [DataMember(Name = "pinnedToSegment")]
        public bool PinnedToSegment { get; set; }

        [DataMember(Name = "left")]
        public double? Left { get; set; }

        [DataMember(Name = "top")]
        public double? Top { get; set; }

        [DataMember(Name = "locked")]
        public bool Locked { get; set; }

        [DataMember(Name = "opacity")]
        public double? Opacity { get; set; }   // 0.3..1.0 multiplier over the baked alphas; null = 1.0 (today's look)

        [DataMember(Name = "backdropOpacity")]
        public double? BackdropOpacity { get; set; }   // 0.0..1.0; null = DefaultBackdropOpacity (1.0). Rendering + knob land in increment 3.

        [DataMember(Name = "rowHeight")]
        public double? RowHeight { get; set; }   // null = VisualStyle.DefaultRowHeight (26); clamped to Settings row-height bounds

        [DataMember(Name = "fontFamily")]
        public string FontFamily { get; set; }        // null = system default

        [DataMember(Name = "fontBaseSize")]
        public double? FontBaseSize { get; set; }      // WPF DIPs; null = 13

        [DataMember(Name = "fontBold")]
        public bool FontBold { get; set; }             // the picker's Bold; 0-value false = normal (field-2026-08-03)

        [DataMember(Name = "fontItalic")]
        public bool FontItalic { get; set; }           // the picker's Italic

        [DataMember(Name = "width")]
        public double? Width { get; set; }             // null = VisualStyle.DefaultRowWidth (250); clamped to Settings row-width bounds

        [DataMember(Name = "visibleRows")]
        public int? VisibleRows { get; set; }          // null = MeterWindow.DefaultVisibleRows (10); clamped to [Min,Max]VisibleRows

        // Inverted per the DCJS 0-value rule: false = class colours ON (the default); true =
        // grey rows (the pre-class-color monochrome look). SPEC §Class colors. The cog checkbox
        // is its inverse view; the plugin gates MeterEngine's colour resolver on this.
        [DataMember(Name = "disableClassColors")]
        public bool DisableClassColors { get; set; }
    }
}
