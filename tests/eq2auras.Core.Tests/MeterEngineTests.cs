using System.Collections.Generic;
using Eq2Auras.Core.Meter;
using Xunit;

public class MeterEngineTests
{

    private static EncounterReading Live(double seconds) => new()
        { Exists = true, Active = true, LiveDurationSeconds = seconds, FinalDurationSeconds = 0 };

    private static CombatantReading Ally(string name, long damage = 0, long healed = 0, int cures = 0, bool isAlly = true)
        => new() { Name = name, Damage = damage, Healed = healed, CureDispels = cures, IsAlly = isAlly };

    [Fact]
    public void Rates_divide_totals_by_the_live_wall_clock_while_active()
    {
        var frame = new MeterEngine().Tick(Live(100),
            new List<CombatantReading> { Ally("A", damage: 50_000) }, "encdps");

        Assert.Equal(500, frame.Rows[0].Value);          // 50k / 100s
        Assert.Equal("500", frame.Rows[0].FormattedValue);
        Assert.Equal("DPS", frame.MetricLabel);
        Assert.Equal("1:40", frame.DurationText);
    }

    [Fact]
    public void Frozen_encounter_switches_to_the_finalized_duration()
    {
        // Plan-watch item 2: the branch flip IS the intended display behavior — the
        // finalized log-time duration is generally shorter, so the rate steps up.
        var frozen = new EncounterReading
            { Exists = true, Active = false, LiveDurationSeconds = 120, FinalDurationSeconds = 100 };

        var frame = new MeterEngine().Tick(frozen,
            new List<CombatantReading> { Ally("A", damage: 50_000) }, "encdps");

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
            new List<CombatantReading> { Ally("A", damage: 50_000) }, "encdps");

        Assert.Equal(0, frame.Rows[0].Value);
        Assert.Equal("0", frame.Rows[0].FormattedValue);
        Assert.Equal("0:00", frame.DurationText);
    }

    [Fact]
    public void Empty_encounter_and_empty_ally_list_render_an_empty_frame()
    {
        var none = new MeterEngine().Tick(new EncounterReading { Exists = false },
            new List<CombatantReading>(), "encdps");

        Assert.Empty(none.Rows);
        Assert.Equal("0:00", none.DurationText);
        Assert.Equal("DPS", none.MetricLabel);   // header still names the selected metric
        Assert.Equal("0", none.TotalText);
    }

    [Fact]
    public void Counts_never_divide_and_format_as_integers()
    {
        var frame = new MeterEngine().Tick(Live(100),
            new List<CombatantReading> { Ally("A", cures: 7) }, "cures");

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

        var frame = new MeterEngine().Tick(Live(10), allies, "encdps");

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

        var frame = new MeterEngine().Tick(Live(10), allies, "encdps");

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

        var frame = new MeterEngine().Tick(Live(10), allies, "encdps");

        Assert.Equal(20, frame.Rows.Count);                // every ally in the frame
        Assert.Equal(0.05, frame.Rows[0].Percent, 3);      // 1/20 of the ALL-allies total
        Assert.Equal("200", frame.TotalText);              // all 20 counted: 2000/10s
    }

    [Fact]
    public void All_rows_take_the_primary_metric_family_color()
    {
        var allies = new List<CombatantReading>
            { Ally("A", damage: 300), Ally("B", damage: 200), Ally("C", damage: 100) };

        var frame = new MeterEngine().Tick(Live(10), allies, "encdps");   // DPS -> Damage family

        var red = MeterFamilyColors.ArgbFor("Damage");
        Assert.All(frame.Rows, r => Assert.Equal(red, r.FillArgb));
    }

    [Fact]
    public void The_fill_color_follows_the_metric_family_not_the_ally()
    {
        var allies = new List<CombatantReading> { Ally("A", damage: 300, healed: 500) };

        var dps = new MeterEngine().Tick(Live(10), allies, "encdps");
        var hps = new MeterEngine().Tick(Live(10), allies, "enchps");

        Assert.Equal(MeterFamilyColors.ArgbFor("Damage"), dps.Rows[0].FillArgb);
        Assert.Equal(MeterFamilyColors.ArgbFor("Healing"), hps.Rows[0].FillArgb);
    }

    [Fact]
    public void No_secondary_key_leaves_the_secondaries_list_empty()
    {
        var frame = new MeterEngine().Tick(Live(10),
            new List<CombatantReading> { Ally("A", damage: 100) }, "encdps");   // no secondaryKey arg

        Assert.NotNull(frame.Rows[0].Secondaries);
        Assert.Empty(frame.Rows[0].Secondaries);
    }

    [Fact]
    public void A_selected_secondary_rides_each_row_computed_like_the_primary()
    {
        var frame = new MeterEngine().Tick(Live(100),
            new List<CombatantReading> { Ally("A", damage: 50_000, healed: 30_000) },
            "encdps", "enchps");

        var secondary = Assert.Single(frame.Rows[0].Secondaries);
        Assert.Equal("enchps", secondary.Key);
        Assert.Equal("300", secondary.FormattedValue);   // 30_000 / 100s, HPS is a rate
    }

    [Fact]
    public void A_count_secondary_is_not_divided_and_formats_as_an_integer()
    {
        var frame = new MeterEngine().Tick(Live(100),
            new List<CombatantReading> { Ally("A", damage: 50_000, cures: 7) },
            "encdps", "cures");

        Assert.Equal("7", Assert.Single(frame.Rows[0].Secondaries).FormattedValue);   // NOT 0.07
    }

    [Fact]
    public void The_secondary_may_equal_the_primary_and_simply_renders_twice()
    {
        var frame = new MeterEngine().Tick(Live(100),
            new List<CombatantReading> { Ally("A", damage: 50_000) },
            "encdps", "encdps");

        Assert.Equal(frame.Rows[0].FormattedValue,
            Assert.Single(frame.Rows[0].Secondaries).FormattedValue);   // same DPS, twice — by design
    }

    [Fact]
    public void An_unknown_secondary_key_leaves_the_list_empty()
    {
        var frame = new MeterEngine().Tick(Live(100),
            new List<CombatantReading> { Ally("A", damage: 50_000) },
            "encdps", "no-such-metric");

        Assert.Empty(frame.Rows[0].Secondaries);   // Find -> null -> off
    }

    [Fact]
    public void A_secondary_does_not_change_the_primary_sort_order()
    {
        // B has less DPS but more HPS; sort must stay by DPS (primary) regardless of the secondary.
        var allies = new List<CombatantReading>
        {
            Ally("A", damage: 900, healed: 100),
            Ally("B", damage: 100, healed: 900),
        };

        var frame = new MeterEngine().Tick(Live(10), allies, "encdps", "enchps");

        Assert.Equal("A", frame.Rows[0].Name);   // DPS leader first, not the HPS leader
        Assert.Equal("B", frame.Rows[1].Name);
    }

    [Fact]
    public void Non_allies_are_hidden_when_any_ally_is_present()
    {
        // Mirror ACT's mini parse: ShowOnlyAllies engages once the ally set is non-empty.
        var combatants = new List<CombatantReading>
        {
            Ally("Biffels", damage: 500, isAlly: true),
            Ally("a lamia deathcaller", damage: 9000, isAlly: false),   // the mob — big number, must not show
        };

        var frame = new MeterEngine().Tick(Live(10), combatants, "encdps");

        Assert.Single(frame.Rows);
        Assert.Equal("Biffels", frame.Rows[0].Name);
        Assert.Equal("50", frame.TotalText);            // 500/10s — the mob is NOT in the total
    }

    [Fact]
    public void All_combatants_show_when_none_is_classified_ally()
    {
        // The escape hatch: before the user acts, GetAllies() is empty, so ACT's
        // filter switches off and every combatant shows (groupmate AND mob) — the
        // transient pre-engage state that self-heals once the user engages.
        var combatants = new List<CombatantReading>
        {
            Ally("Groupmate", damage: 800, isAlly: false),
            Ally("a lamia deathcaller", damage: 200, isAlly: false),
        };

        var frame = new MeterEngine().Tick(Live(10), combatants, "encdps");

        Assert.Equal(2, frame.Rows.Count);
        Assert.Equal("Groupmate", frame.Rows[0].Name);   // shows even though unlinked — fixes the field bug
    }

    [Fact]
    public void The_Unknown_combatant_is_always_dropped()
    {
        // The parser emits attacker "Unknown" for unsourced hits; ACT's export drops it.
        var combatants = new List<CombatantReading>
        {
            Ally("Biffels", damage: 500, isAlly: true),
            Ally("Unknown", damage: 300, isAlly: false),
        };

        var frame = new MeterEngine().Tick(Live(10), combatants, "encdps");

        Assert.Single(frame.Rows);
        Assert.Equal("Biffels", frame.Rows[0].Name);
    }

    [Fact]
    public void Cleared_primary_yields_an_empty_frame()
    {
        // A cleared primary (null metricKey) shows nothing — no rows, blank metric/total
        // (SPEC §Meter display defaults). Every window-creation path seeds a non-null key,
        // so null reaches the engine only from a deliberate user clear. (Supersedes the
        // pre-cleared-primary "null -> DPS" behavior.)
        var frame = new MeterEngine().Tick(Live(10),
            new List<CombatantReading> { Ally("A", damage: 1000), Ally("B", damage: 500) },
            metricKey: null);

        Assert.Empty(frame.Rows);        // cleared primary -> nothing
        Assert.Equal("", frame.MetricLabel);
        Assert.Equal("", frame.TotalText);
    }

    [Fact]
    public void A_selected_secondary_labels_the_header_with_its_metric_name()
    {
        // The header names the secondary alongside the primary (SPEC §Header — the muted
        // secondary label left of the white primary label).
        var frame = new MeterEngine().Tick(Live(100),
            new List<CombatantReading> { Ally("A", damage: 50_000, healed: 30_000) },
            "encdps", "enchps");

        Assert.Equal("HPS", frame.SecondaryLabel);
    }

    [Fact]
    public void No_secondary_leaves_the_secondary_label_blank()
    {
        var frame = new MeterEngine().Tick(Live(100),
            new List<CombatantReading> { Ally("A", damage: 50_000) }, "encdps");

        Assert.Equal("", frame.SecondaryLabel);
    }

    [Fact]
    public void An_unknown_secondary_key_leaves_the_secondary_label_blank()
    {
        var frame = new MeterEngine().Tick(Live(100),
            new List<CombatantReading> { Ally("A", damage: 50_000) },
            "encdps", "no-such-metric");

        Assert.Equal("", frame.SecondaryLabel);   // Find -> null -> no label
    }

    [Fact]
    public void A_cleared_primary_blanks_the_secondary_label_too()
    {
        // No primary -> the header shows only the duration and cog (SPEC §Header); no metric/secondary/total labels.
        var frame = new MeterEngine().Tick(Live(10),
            new List<CombatantReading> { Ally("A", damage: 1000) },
            metricKey: null, "enchps");

        Assert.Equal("", frame.SecondaryLabel);
    }

    private static CombatantReading Combatant(string name, bool isAlly, long damageTaken = 0, long healed = 0, long powerReplenish = 0)
        => new() { Name = name, DamageTaken = damageTaken, Healed = healed, PowerReplenish = powerReplenish, IsAlly = isAlly };

    [Fact]
    public void Enemy_scope_shows_only_non_allies()
    {
        var combatants = new List<CombatantReading>
        {
            Combatant("Me", isAlly: true, damageTaken: 100),
            Combatant("Boss", isAlly: false, damageTaken: 5000),
            Combatant("Add", isAlly: false, damageTaken: 3000),
        };

        var frame = new MeterEngine().Tick(Live(100), combatants, "damagetaken", null, MeterScope.Enemies);

        Assert.Equal(new[] { "Boss", "Add" }, frame.Rows.Select(r => r.Name));   // allies excluded, sorted desc
        Assert.Equal(8000, frame.Rows.Sum(r => r.Value));
    }

    [Fact]
    public void Allies_scope_default_still_shows_only_allies()
    {
        var combatants = new List<CombatantReading>
        {
            Combatant("Me", isAlly: true, damageTaken: 100),
            Combatant("Boss", isAlly: false, damageTaken: 5000),
        };

        var frame = new MeterEngine().Tick(Live(100), combatants, "damagetaken");   // scope defaults to Allies

        Assert.Equal(new[] { "Me" }, frame.Rows.Select(r => r.Name));
    }

    [Fact]
    public void Pre_engage_shows_everyone_under_either_scope()
    {
        var combatants = new List<CombatantReading>   // no allies classified yet
        {
            Combatant("Groupmate", isAlly: false, damageTaken: 10),
            Combatant("Mob", isAlly: false, damageTaken: 20),
        };

        var allies = new MeterEngine().Tick(Live(100), combatants, "damagetaken", null, MeterScope.Allies);
        var enemies = new MeterEngine().Tick(Live(100), combatants, "damagetaken", null, MeterScope.Enemies);

        Assert.Equal(2, allies.Rows.Count);   // Allies escape hatch: no allies -> show all
        Assert.Equal(2, enemies.Rows.Count);  // Enemies: all are non-allies -> show all
    }

    [Fact]
    public void Enemy_scope_with_no_enemies_yields_no_rows()
    {
        var combatants = new List<CombatantReading> { Combatant("Me", isAlly: true, damageTaken: 100) };

        var frame = new MeterEngine().Tick(Live(100), combatants, "damagetaken", null, MeterScope.Enemies);

        Assert.Empty(frame.Rows);
    }

    [Fact]
    public void Header_identity_is_the_selection_label_not_the_bare_metric()
    {
        var combatants = new List<CombatantReading> { Combatant("Boss", isAlly: false, damageTaken: 5000) };

        var frame = new MeterEngine().Tick(Live(100), combatants, "damagetaken", null, MeterScope.Enemies);

        Assert.Equal("Enemy Damage Taken", frame.MetricLabel);
    }

    [Fact]
    public void Secondary_is_computed_over_the_scoped_population()
    {
        var combatants = new List<CombatantReading>
        {
            Combatant("Boss", isAlly: false, damageTaken: 5000, healed: 800),
        };

        // primary = enemy damage taken; secondary = total healing -> reads the enemy's Healed
        var frame = new MeterEngine().Tick(Live(100), combatants, "damagetaken", "totalhealing", MeterScope.Enemies);

        Assert.Single(frame.Rows[0].Secondaries);
        Assert.Equal("800", frame.Rows[0].Secondaries[0].FormattedValue);
    }

    [Fact]
    public void Unknown_scope_value_degrades_to_allies_behavior()
    {
        var combatants = new List<CombatantReading>
        {
            Combatant("Me", isAlly: true, damageTaken: 100),
            Combatant("Boss", isAlly: false, damageTaken: 5000),
        };

        var frame = new MeterEngine().Tick(Live(100), combatants, "damagetaken", null, (MeterScope)99);

        Assert.Equal(new[] { "Me" }, frame.Rows.Select(r => r.Name));   // not Enemies -> Allies filter
    }
}
