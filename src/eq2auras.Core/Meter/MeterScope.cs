namespace Eq2Auras.Core.Meter
{
    /// Which combatants a meter window draws its rows from — an axis independent of the
    /// metric (SPEC Part III §The metric registry — Scope). Allies is the 0-value so a
    /// scope-less (missing) config field deserializes to it under DCJS.
    public enum MeterScope
    {
        Allies = 0,
        Enemies = 1,
    }
}
