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
}
