using System.Runtime.Serialization;

namespace Eq2Auras.Core.Config
{
    /// Parse Meter module settings (SPEC Part III §Assembly split & polling — Settings).
    /// Enabled defaults false (0-value rule): the meter is opt-in while groundwork.
    /// Positions nullable on purpose: null — never zero — means "unset, default placement".
    [DataContract]
    public sealed class MeterSettings
    {
        [DataMember(Name = "enabled")]
        public bool Enabled { get; set; }

        [DataMember(Name = "left")]
        public double? Left { get; set; }

        [DataMember(Name = "top")]
        public double? Top { get; set; }

        [DataMember(Name = "metricKey")]
        public string MetricKey { get; set; }   // null/unknown -> registry default (forward-compat guard)

        [DataMember(Name = "locked")]
        public bool Locked { get; set; }
    }
}
