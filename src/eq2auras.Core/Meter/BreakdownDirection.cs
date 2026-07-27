namespace Eq2Auras.Core.Meter
{
    /// The by-counterpart direction rule (SPEC Part III §Row drill-down — the by-row mouseover).
    /// An INCOMING bucket groups the combatant's swings by the swing's ATTACKER (who hit/healed
    /// me — "by source"); every other bucket by the swing's VICTIM (whom I hit/healed/cured/fed
    /// — "by target"). Pure and WPF-free; the Plugin reads MasterSwing.Attacker/Victim per this
    /// rule (docs/act-parse-engine.md:47). None/Deaths never reach it (cleared primary / event
    /// metric publish no hover request), so the by-victim default is harmless for them.
    public static class BreakdownDirection
    {
        public static bool IsIncoming(MetricBreakdownSource source)
            => source == MetricBreakdownSource.IncomingDamage
            || source == MetricBreakdownSource.IncomingHealing;
    }
}
