using System.Collections.Generic;
using Eq2Auras.Core.Meter;
using Xunit;

public class BreakdownEngineTests
{
    private static MetricDef Dps => MetricRegistry.All[0];         // encdps — a rate
    private static MetricDef Cures =>
        System.Linq.Enumerable.Single(MetricRegistry.All, m => m.Key == "cures");   // a count/total (IsRate false)

    private static List<BreakdownEntry> Entries(params (string, double)[] xs)
    {
        var list = new List<BreakdownEntry>();
        foreach (var (label, value) in xs) list.Add(new BreakdownEntry { Label = label, Value = value });
        return list;
    }

    [Fact]
    public void Rows_sort_descending_with_a_name_tiebreak()
    {
        var rows = BreakdownEngine.Build(Entries(("Ice Comet", 100), ("Auto Attack", 100), ("Fiery Blast", 300)), Dps, durationSeconds: 1);
        Assert.Equal(new[] { "Fiery Blast", "Auto Attack", "Ice Comet" },
            rows.ConvertAll(r => r.Name).ToArray());   // 300 first; tie 100/100 -> ordinal name (Auto < Ice)
    }

    [Fact]
    public void Percent_is_share_of_the_lists_own_sum()
    {
        var rows = BreakdownEngine.Build(Entries(("A", 300), ("B", 100)), Cures, durationSeconds: 1);
        Assert.Equal(0.75, rows[0].Percent, 3);
        Assert.Equal("75%", rows[0].FormattedPercent);
        Assert.Equal("25%", rows[1].FormattedPercent);
    }

    [Fact]
    public void A_single_entry_is_one_hundred_percent_and_a_full_bar()
    {
        var rows = BreakdownEngine.Build(Entries(("Only", 42)), Cures, durationSeconds: 1);
        Assert.Single(rows);
        Assert.Equal("100%", rows[0].FormattedPercent);
        Assert.Equal(1.0, rows[0].BarFraction, 3);
    }

    [Fact]
    public void An_empty_entry_list_yields_zero_rows()
    {
        Assert.Empty(BreakdownEngine.Build(Entries(), Dps, durationSeconds: 1));
    }

    [Fact]
    public void A_zero_sum_list_gives_zero_percent_and_no_divide_by_zero()
    {
        var rows = BreakdownEngine.Build(Entries(("A", 0), ("B", 0)), Cures, durationSeconds: 1);
        Assert.Equal("0%", rows[0].FormattedPercent);
        Assert.Equal(0.0, rows[0].BarFraction, 3);
    }

    [Fact]
    public void Bar_fraction_is_relative_to_the_top_entry()
    {
        var rows = BreakdownEngine.Build(Entries(("Top", 200), ("Half", 100)), Cures, durationSeconds: 1);
        Assert.Equal(1.0, rows[0].BarFraction, 3);
        Assert.Equal(0.5, rows[1].BarFraction, 3);
    }

    [Fact]
    public void A_rate_metric_divides_each_value_by_duration_and_formats_via_the_metric()
    {
        // encdps: raw 50000 damage over 100s -> 500 DPS; format is the K/M/B abbreviation.
        var rows = BreakdownEngine.Build(Entries(("Fiery Blast", 50_000)), Dps, durationSeconds: 100);
        Assert.Equal(500, rows[0].Value);
        Assert.Equal("500", rows[0].FormattedValue);
    }

    [Fact]
    public void A_total_metric_is_the_raw_value_never_divided()
    {
        // cures: IsRate false -> the count is the value regardless of duration.
        var rows = BreakdownEngine.Build(Entries(("Cure Ward", 7)), Cures, durationSeconds: 100);
        Assert.Equal(7, rows[0].Value);
        Assert.Equal("7", rows[0].FormattedValue);
    }

    [Fact]
    public void Percent_is_duration_independent_for_a_rate_metric()
    {
        // Duration cancels in the share — the same percents at any duration.
        var rows = BreakdownEngine.Build(Entries(("A", 300), ("B", 100)), Dps, durationSeconds: 37);
        Assert.Equal("75%", rows[0].FormattedPercent);
    }

    [Fact]
    public void Every_row_takes_the_metrics_family_color()
    {
        var rows = BreakdownEngine.Build(Entries(("A", 1)), Dps, durationSeconds: 1);
        Assert.Equal(MeterFamilyColors.ArgbFor(Dps.Category), rows[0].FillArgb);
        Assert.Empty(rows[0].Secondaries);   // breakdown rows carry no secondary
    }

    [Fact]
    public void Duration_seconds_matches_the_engines_live_and_final_policy()
    {
        Assert.Equal(0, MeterEngine.DurationSeconds(new EncounterReading { Exists = false }));
        Assert.Equal(100, MeterEngine.DurationSeconds(
            new EncounterReading { Exists = true, Active = true, LiveDurationSeconds = 100, FinalDurationSeconds = 0 }));
        Assert.Equal(90, MeterEngine.DurationSeconds(
            new EncounterReading { Exists = true, Active = false, LiveDurationSeconds = 120, FinalDurationSeconds = 90 }));
        Assert.Equal(0, MeterEngine.DurationSeconds(       // degenerate pre-first-swing clamps at 0
            new EncounterReading { Exists = true, Active = true, LiveDurationSeconds = -1e15, FinalDurationSeconds = 0 }));
    }
}
