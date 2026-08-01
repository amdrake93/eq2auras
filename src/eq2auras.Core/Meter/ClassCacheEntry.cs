using System.Runtime.Serialization;

namespace Eq2Auras.Core.Meter
{
    /// One persisted learned record (SPEC Part III §Class colors): a combatant name and the class it
    /// resolved to. The subclass drives the color; the final is enrichment (Unknown when only a SHARED
    /// tell fired). Enum defaults at the 0-value (DCJS skips field initializers on deserialize).
    [DataContract]
    public sealed class ClassCacheEntry
    {
        [DataMember] public string Name { get; set; }
        [DataMember] public Subclass Subclass { get; set; }
        [DataMember] public FinalClass Final { get; set; }
    }
}
