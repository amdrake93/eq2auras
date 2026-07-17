using System.Runtime.Serialization;

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

        [DataMember(Name = "left")]
        public double? Left { get; set; }

        [DataMember(Name = "top")]
        public double? Top { get; set; }

        [DataMember(Name = "locked")]
        public bool Locked { get; set; }

        [DataMember(Name = "opacity")]
        public double? Opacity { get; set; }   // 0.3..1.0 multiplier over the baked alphas; null = 1.0 (today's look)

        [DataMember(Name = "rowHeight")]
        public double? RowHeight { get; set; }   // null = VisualStyle.DefaultRowHeight (26); clamped to Settings row-height bounds

        [DataMember(Name = "fontFamily")]
        public string FontFamily { get; set; }        // null = system default

        [DataMember(Name = "fontBaseSize")]
        public double? FontBaseSize { get; set; }      // WPF DIPs; null = 13
    }
}
