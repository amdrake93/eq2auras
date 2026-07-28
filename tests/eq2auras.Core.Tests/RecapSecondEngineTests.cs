using System.Collections.Generic;
using Eq2Auras.Core.Meter;
using Xunit;

public class RecapSecondEngineTests
{
    private static RecapEventDetail E(string source, string ability, double amount, bool isHeal, int order)
        => new RecapEventDetail { Source = source, Ability = ability, Amount = amount, IsHeal = isHeal, Order = order };

    [Fact]
    public void Empty_input_yields_no_rows()
    {
        Assert.Empty(RecapSecondEngine.Build(new List<RecapEventDetail>()));
        Assert.Empty(RecapSecondEngine.Build(null));
    }

    [Fact]
    public void Rows_are_time_ordered_by_Order_ascending()
    {
        var rows = RecapSecondEngine.Build(new List<RecapEventDetail>
        {
            E("Boss", "Cleave", 5000, false, 30),
            E("Priest", "Ward", 1000, true, 10),
            E("Boss", "Melee", 2000, false, 20),
        });
        Assert.Equal(new[] { "Priest · Ward", "Boss · Melee", "Boss · Cleave" },
            new[] { rows[0].Name, rows[1].Name, rows[2].Name });
    }

    [Fact]
    public void Damage_is_red_and_negative_heal_is_green_and_positive()
    {
        var rows = RecapSecondEngine.Build(new List<RecapEventDetail>
        {
            E("Boss", "Cleave", 5000, false, 10),
            E("Priest", "Ward", 1000, true, 20),
        });
        Assert.Equal("-5K", rows[0].FormattedValue);
        Assert.Equal(DeathRecapEngine.DmgArgb, rows[0].FillArgb);
        Assert.Equal("+1K", rows[1].FormattedValue);
        Assert.Equal(DeathRecapEngine.HealArgb, rows[1].FillArgb);
    }

    [Fact]
    public void Bar_is_magnitude_over_the_largest_event_regardless_of_kind()
    {
        var rows = RecapSecondEngine.Build(new List<RecapEventDetail>
        {
            E("Boss", "Cleave", 4000, false, 10),   // biggest
            E("Priest", "Ward", 1000, true, 20),
        });
        Assert.Equal(1.0, rows[0].BarFraction);
        Assert.Equal(0.25, rows[1].BarFraction);
    }

    [Fact]
    public void A_single_event_fills_the_bar()
    {
        var rows = RecapSecondEngine.Build(new List<RecapEventDetail> { E("Boss", "Cleave", 5000, false, 10) });
        Assert.Single(rows);
        Assert.Equal(1.0, rows[0].BarFraction);
    }

    [Fact]
    public void No_source_shows_the_ability_alone()
    {
        var rows = RecapSecondEngine.Build(new List<RecapEventDetail>
        {
            E(null, "Falling", 3000, false, 10),
            E("", "Bleed", 500, false, 20),
        });
        Assert.Equal("Falling", rows[0].Name);
        Assert.Equal("Bleed", rows[1].Name);
    }

    [Fact]
    public void Rows_carry_no_percent_and_no_secondaries()
    {
        var rows = RecapSecondEngine.Build(new List<RecapEventDetail> { E("Boss", "Cleave", 5000, false, 10) });
        Assert.Equal("", rows[0].FormattedPercent);
        Assert.Empty(rows[0].Secondaries);
    }
}
