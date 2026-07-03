using System.Runtime.Serialization;

namespace Eq2Auras.Core.Config
{
    /// Per-group knobs + window positions (SPEC §Timer groups, §Moving the overlay).
    /// Positions are nullable on purpose: DCJS materializes missing numerics as 0 — a
    /// real screen corner — so null (never zero) means "unset, use the default layout".
    [DataContract]
    public sealed class PanelSettings
    {
        [DataMember(Name = "colorSource")]
        public ColorSource ColorSource { get; set; } = ColorSource.Palette;

        [DataMember(Name = "escalationStyle")]
        public EscalationStyle EscalationStyle { get; set; } = EscalationStyle.CenterRadial;

        [DataMember(Name = "listLeft")]
        public double? ListLeft { get; set; }

        [DataMember(Name = "listTop")]
        public double? ListTop { get; set; }

        [DataMember(Name = "centerLeft")]
        public double? CenterLeft { get; set; }

        [DataMember(Name = "centerTop")]
        public double? CenterTop { get; set; }

        [DataMember(Name = "fontFamily")]
        public string FontFamily { get; set; }        // null = system default

        [DataMember(Name = "fontBaseSize")]
        public double? FontBaseSize { get; set; }     // WPF DIPs; null = 13 (today's look)

        [DataMember(Name = "listScale")]
        public double? ListScale { get; set; }        // null = 1.0; geometry-only

        [DataMember(Name = "centerScale")]
        public double? CenterScale { get; set; }
    }
}
