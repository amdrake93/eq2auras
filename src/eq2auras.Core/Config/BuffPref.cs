using System.Runtime.Serialization;

namespace Eq2Auras.Core.Config
{
    /// One raider preference for one catalog buff: tracked or not, and an optional per-character
    /// duration override (null = use the catalog base). DCJS: Enabled's 0-value is false, so the
    /// backfill sets it explicitly (SPEC §Buff tracking — the tracked set and per-buff overrides).
    [DataContract]
    public sealed class BuffPref
    {
        [DataMember(Name = "id")]
        public string Id { get; set; }

        [DataMember(Name = "enabled")]
        public bool Enabled { get; set; }

        [DataMember(Name = "durationOverride")]
        public int? DurationOverride { get; set; }

        [DataMember(Name = "warnOverride")]
        public int? WarnOverride { get; set; }     // null = base 0 (no explicit warning point)

        [DataMember(Name = "removeOverride")]
        public int? RemoveOverride { get; set; }   // null = base 0 (remove at zero, no linger)
    }
}
