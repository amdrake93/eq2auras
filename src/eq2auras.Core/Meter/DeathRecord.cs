namespace Eq2Auras.Core.Meter
{
    /// One death event, produced by the Plugin's poll-only capture (SPEC §Deaths & the Death Recap).
    /// No ACT types — the Plugin resolves the killing blow and stamps these fields.
    public sealed class DeathRecord
    {
        public string Victim { get; set; }
        public int Ordinal { get; set; }                 // this victim's Nth death (1-based), always shown
        public double TimeOfDeathSeconds { get; set; }   // encounter-relative time of the Death swing
        public string KillingBlowAbility { get; set; }   // last incoming damage swing's ability; null if none found
        public double KillingBlowDamage { get; set; }    // that swing's damage
        public string DrillKey { get; set; }             // stable per-death identity (Victim + "#" + Ordinal)
    }
}
