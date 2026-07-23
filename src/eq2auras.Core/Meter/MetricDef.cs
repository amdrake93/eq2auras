using System;

namespace Eq2Auras.Core.Meter
{
    /// One entry of the flat metric registry (SPEC Part III §The metric registry).
    /// Select returns the raw TOTAL; the engine divides by wall-clock duration when
    /// IsRate — the duration policy lives in one place, never in selectors.
    public sealed class MetricDef
    {
        public string Key { get; }
        public string Label { get; }
        public string Category { get; }        // picker grouping + family color (MeterFamilyColors) — a display attribute, never a dispatch axis
        public bool IsRate { get; }
        public Func<CombatantReading, double> Select { get; }
        public Func<double, string> Format { get; }
        public MetricBreakdownSource BreakdownSource { get; }   // which ACT bucket the by-ability drill-down reads (SPEC §The metric registry)
        public bool IsEvent { get; }   // true = a special event metric (Deaths) — rows come from its own engine, not Select/Tick (SPEC §Deaths)

        public MetricDef(string key, string label, string category, bool isRate,
            Func<CombatantReading, double> select, Func<double, string> format,
            MetricBreakdownSource breakdownSource, bool isEvent = false)
        {
            Key = key;
            Label = label;
            Category = category;
            IsRate = isRate;
            Select = select;
            Format = format;
            BreakdownSource = breakdownSource;
            IsEvent = isEvent;
        }
    }
}
