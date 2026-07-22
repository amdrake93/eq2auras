namespace Eq2Auras.Core.Meter
{
    /// Which of ACT's per-combatant DamageTypeData buckets a metric's by-ability
    /// drill-down reads (SPEC Part III §The metric registry — breakdownSource). A Core
    /// enum only: the Plugin maps each value to the concrete CombatantData.DamageTypeData…
    /// alias-static bucket + the per-AttackType value accessor. Required per-metric because
    /// the total alone does not determine it (encdps vs damagetaken read the same Damage
    /// total off opposite buckets). None = the metric has no by-ability breakdown (0-value,
    /// DCJS-safe default, though every registry metric today names a real bucket).
    public enum MetricBreakdownSource
    {
        None = 0,
        OutgoingDamage,
        IncomingDamage,
        OutgoingHealing,
        IncomingHealing,
        PowerReplenish,
        Cures,
    }
}
