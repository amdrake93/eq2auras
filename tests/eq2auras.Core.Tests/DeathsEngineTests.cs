using System.Collections.Generic;
using Eq2Auras.Core.Meter;
using Xunit;

public class DeathsEngineTests
{
    private static DeathRecord D(string victim, int ord, double t, string ability, double dmg) =>
        new DeathRecord { Victim = victim, Ordinal = ord, TimeOfDeathSeconds = t,
            KillingBlowAbility = ability, KillingBlowDamage = dmg, DrillKey = victim + "#" + ord };

    [Fact]
    public void Rows_are_death_events_in_chronological_order_with_time_as_value()
    {
        var frame = DeathsEngine.BuildList(new List<DeathRecord>
        {
            D("Aeralik", 2, 113, "Melee", 5000),      // deliberately out of order in the input
            D("Aeralik", 1, 42, "Frostbite", 3800),
            D("Biffles", 1, 80, "Cleaving Strike", 9800),
        }, durationSeconds: 120);

        Assert.Equal(new[] { "Aeralik", "Biffles", "Aeralik" },
            frame.Rows.ConvertAll(r => r.Name).ToArray());               // sorted by time asc: 42, 80, 113
        Assert.Equal("0:42", frame.Rows[0].FormattedValue);
        Assert.Equal("1:20", frame.Rows[1].FormattedValue);
        Assert.Equal("1:53", frame.Rows[2].FormattedValue);
    }

    [Fact]
    public void Every_death_is_numbered_and_the_killing_blow_is_a_muted_detail()
    {
        var frame = DeathsEngine.BuildList(new List<DeathRecord>
        {
            D("Biffles", 1, 80, "Cleaving Strike", 9800),
        }, 120);

        Assert.Equal("Biffles", frame.Rows[0].Name);
        Assert.Equal("(1) · Cleaving Strike 9.8K", frame.Rows[0].Detail);   // ordinal always shown, dmg abbreviated
        Assert.Equal("Biffles#1", frame.Rows[0].DrillKey);
    }

    [Fact]
    public void Bar_and_percent_are_the_deaths_position_in_the_fight()
    {
        var frame = DeathsEngine.BuildList(new List<DeathRecord>
        {
            D("A", 1, 60, "X", 1), // 60/120 = 0.5
        }, 120);

        Assert.Equal(0.5, frame.Rows[0].BarFraction, 3);
        Assert.Equal(0.5, frame.Rows[0].Percent, 3);
        Assert.Equal("50%", frame.Rows[0].FormattedPercent);
    }

    [Fact]
    public void Total_is_the_death_count_and_there_is_no_secondary()
    {
        var frame = DeathsEngine.BuildList(new List<DeathRecord>
        {
            D("A", 1, 10, "X", 1), D("A", 2, 20, "Y", 1), D("B", 1, 30, "Z", 1),
        }, 120);

        Assert.Equal("3", frame.TotalText);
        Assert.Equal("Deaths", frame.MetricLabel);
        Assert.Equal("", frame.SecondaryLabel);
        Assert.All(frame.Rows, r => Assert.Empty(r.Secondaries));
    }

    [Fact]
    public void A_death_with_no_killing_blow_shows_a_dash()
    {
        var frame = DeathsEngine.BuildList(new List<DeathRecord>
        {
            new DeathRecord { Victim = "A", Ordinal = 1, TimeOfDeathSeconds = 10,
                KillingBlowAbility = null, KillingBlowDamage = 0, DrillKey = "A#1" },
        }, 120);

        Assert.Equal("(1) · —", frame.Rows[0].Detail);
    }

    [Fact]
    public void Into_fight_fraction_clamps_when_duration_is_zero_or_less_than_time()
    {
        var atZero = DeathsEngine.BuildList(new List<DeathRecord> { D("A", 1, 10, "X", 1) }, 0);
        Assert.Equal(0, atZero.Rows[0].BarFraction, 3);   // no divide-by-zero

        var past = DeathsEngine.BuildList(new List<DeathRecord> { D("A", 1, 200, "X", 1) }, 120);
        Assert.Equal(1.0, past.Rows[0].BarFraction, 3);   // clamp to 1
    }
}
