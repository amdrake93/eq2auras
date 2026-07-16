using System.Collections.Generic;
using Eq2Auras.Core.Meter;
using Xunit;

public class MeterEngineTests
{
    private static readonly List<int> Palette = new() { 0x11, 0x22, 0x33 };   // 3 slots

    private static EncounterReading Live(double seconds) => new()
        { Exists = true, Active = true, Title = "Vithnok", LiveDurationSeconds = seconds, FinalDurationSeconds = 0 };

    private static CombatantReading Ally(string name, long damage = 0, long healed = 0, int cures = 0)
        => new() { Name = name, Damage = damage, Healed = healed, CureDispels = cures };

    [Fact]
    public void Rates_divide_totals_by_the_live_wall_clock_while_active()
    {
        var frame = new MeterEngine().Tick(Live(100),
            new List<CombatantReading> { Ally("A", damage: 50_000) }, "encdps", Palette);

        Assert.Equal(500, frame.Rows[0].Value);          // 50k / 100s
        Assert.Equal("500", frame.Rows[0].FormattedValue);
        Assert.Equal("DPS", frame.MetricLabel);
        Assert.Equal("1:40", frame.DurationText);
        Assert.Equal("Vithnok", frame.Title);
    }

    [Fact]
    public void Frozen_encounter_switches_to_the_finalized_duration()
    {
        // Plan-watch item 2: the branch flip IS the intended display behavior — the
        // finalized log-time duration is generally shorter, so the rate steps up.
        var frozen = new EncounterReading
            { Exists = true, Active = false, Title = "Vithnok", LiveDurationSeconds = 120, FinalDurationSeconds = 100 };

        var frame = new MeterEngine().Tick(frozen,
            new List<CombatantReading> { Ally("A", damage: 50_000) }, "encdps", Palette);

        Assert.Equal(500, frame.Rows[0].Value);          // divides by Final (100), not Live (120)
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]              // pre-first-swing: StartTime == DateTime.MaxValue makes the live estimate hugely negative
    [InlineData(-1e15)]
    public void Degenerate_durations_yield_zero_rates_never_NaN_or_infinity(double liveSeconds)
    {
        // Plan-watch item 1: the degenerate fresh-encounter poll must render, not explode.
        var frame = new MeterEngine().Tick(Live(liveSeconds),
            new List<CombatantReading> { Ally("A", damage: 50_000) }, "encdps", Palette);

        Assert.Equal(0, frame.Rows[0].Value);
        Assert.Equal("0", frame.Rows[0].FormattedValue);
        Assert.Equal("0:00", frame.DurationText);
    }

    [Fact]
    public void Empty_encounter_and_empty_ally_list_render_an_empty_frame()
    {
        var none = new MeterEngine().Tick(new EncounterReading { Exists = false },
            new List<CombatantReading>(), "encdps", Palette);

        Assert.Empty(none.Rows);
        Assert.Equal("", none.Title);
        Assert.Equal("0:00", none.DurationText);
        Assert.Equal("DPS", none.MetricLabel);   // header still names the selected metric
        Assert.Equal("0", none.TotalText);
    }

    [Fact]
    public void Counts_never_divide_and_format_as_integers()
    {
        var frame = new MeterEngine().Tick(Live(100),
            new List<CombatantReading> { Ally("A", cures: 7) }, "cures", Palette);

        Assert.Equal(7, frame.Rows[0].Value);            // NOT 0.07
        Assert.Equal("7", frame.Rows[0].FormattedValue);
        Assert.Equal("Cures", frame.MetricLabel);
    }

    [Fact]
    public void Sorts_descending_with_ordinal_name_tiebreak_and_keeps_every_ally()
    {
        var allies = new List<CombatantReading>();
        for (int i = 0; i < 12; i++) allies.Add(Ally("P" + i.ToString("00"), damage: 1000 - i * 50));
        allies.Add(Ally("Aardvark", damage: 1000));      // ties with P00 — name breaks it, deterministically

        var frame = new MeterEngine().Tick(Live(10), allies, "encdps", Palette);

        Assert.Equal(13, frame.Rows.Count);              // NO truncation — the window scrolls (SPEC Part III)
        Assert.Equal("Aardvark", frame.Rows[0].Name);    // tie -> ordinal ascending
        Assert.Equal("P00", frame.Rows[1].Name);
        Assert.True(frame.Rows[1].Value >= frame.Rows[2].Value);
    }

    [Fact]
    public void Percent_is_share_of_ALL_allies_and_bar_is_share_of_top()
    {
        var allies = new List<CombatantReading>
            { Ally("A", damage: 600), Ally("B", damage: 300), Ally("C", damage: 100) };

        var frame = new MeterEngine().Tick(Live(10), allies, "encdps", Palette);

        Assert.Equal(0.6, frame.Rows[0].Percent, 3);
        Assert.Equal("60%", frame.Rows[0].FormattedPercent);
        Assert.Equal(1.0, frame.Rows[0].BarFraction, 3);   // rank 1 = full bar
        Assert.Equal(0.5, frame.Rows[1].BarFraction, 3);   // 300/600
        Assert.Equal("100", frame.TotalText);              // (600+300+100)/10s
    }

    [Fact]
    public void Percents_and_the_header_total_cover_the_full_ally_set()
    {
        var allies = new List<CombatantReading>();
        for (int i = 0; i < 20; i++) allies.Add(Ally("P" + i.ToString("00"), damage: 100));

        var frame = new MeterEngine().Tick(Live(10), allies, "encdps", Palette);

        Assert.Equal(20, frame.Rows.Count);                // every ally in the frame
        Assert.Equal(0.05, frame.Rows[0].Percent, 3);      // 1/20 of the ALL-allies total
        Assert.Equal("200", frame.TotalText);              // all 20 counted: 2000/10s
    }

    [Fact]
    public void Ally_colors_are_first_seen_stable_and_cycle_past_the_palette()
    {
        var engine = new MeterEngine();
        var allies = new List<CombatantReading>
            { Ally("A", damage: 400), Ally("B", damage: 300), Ally("C", damage: 200), Ally("D", damage: 100) };

        var first = engine.Tick(Live(10), allies, "encdps", Palette);
        Assert.Equal(0x11, first.Rows[0].FillArgb);
        Assert.Equal(0x22, first.Rows[1].FillArgb);
        Assert.Equal(0x33, first.Rows[2].FillArgb);
        Assert.Equal(0x11, first.Rows[3].FillArgb);        // 4th name cycles onto slot 0

        // B overtakes A next tick: colors follow the NAME, not the rank.
        allies[1].Damage = 900;
        var second = engine.Tick(Live(10), allies, "encdps", Palette);
        Assert.Equal("B", second.Rows[0].Name);
        Assert.Equal(0x22, second.Rows[0].FillArgb);
    }

    [Fact]
    public void Rows_carry_the_secondaries_shape_empty_in_slice_1()
    {
        var frame = new MeterEngine().Tick(Live(10),
            new List<CombatantReading> { Ally("A", damage: 100) }, "encdps", Palette);

        Assert.NotNull(frame.Rows[0].Secondaries);
        Assert.Empty(frame.Rows[0].Secondaries);
    }

    [Fact]
    public void Null_metric_key_falls_back_to_dps()
    {
        var frame = new MeterEngine().Tick(Live(10),
            new List<CombatantReading> { Ally("A", damage: 100) }, null, Palette);

        Assert.Equal("DPS", frame.MetricLabel);
    }
}
