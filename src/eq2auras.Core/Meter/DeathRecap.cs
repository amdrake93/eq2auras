using System.Collections.Generic;

namespace Eq2Auras.Core.Meter
{
    /// One incoming event in a death's recap window (SPEC §Death Recap). The Plugin flattens the
    /// victim's incoming damage/heal MasterSwings into these; Core buckets + reconstructs.
    public sealed class RecapEvent
    {
        public double SecondsBeforeDeath { get; set; }   // >= 0; 0 = the death second
        public double Amount { get; set; }               // positive magnitude
        public bool IsHeal { get; set; }                 // true = healing received; false = damage taken
    }

    public sealed class RecapReading
    {
        public string DrillKey { get; set; }             // which death (Victim#Ordinal) this recap is for
        public double MaxHealthEstimate { get; set; }    // CombatantData.GetMaxHealth() at read time
        public List<RecapEvent> Events { get; set; }
    }

    /// One incoming event for the recap-second hover (SPEC §Deaths — the recap-second hover): a
    /// single swing in the hovered second, keeping its source + ability for the event-log row.
    public sealed class RecapEventDetail
    {
        public string Source { get; set; }   // attacker (damage) or healer (heal); may be null/empty (environmental)
        public string Ability { get; set; }
        public double Amount { get; set; }   // positive magnitude
        public bool IsHeal { get; set; }
        public int Order { get; set; }       // MasterSwing.TimeSorter — chronological sort key
    }
}
