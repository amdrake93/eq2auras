using System.Collections.Generic;
using Eq2Auras.Core.Meter;
using Xunit;

public class DeathRecapEngineTests
{
    private static RecapEvent Dmg(double s, double a) => new RecapEvent { SecondsBeforeDeath = s, Amount = a, IsHeal = false };
    private static RecapEvent Heal(double s, double a) => new RecapEvent { SecondsBeforeDeath = s, Amount = a, IsHeal = true };

    [Fact]
    public void One_row_per_active_second_oldest_first_death_row_at_zero_hp()
    {
        var rows = DeathRecapEngine.Build(new RecapReading
        {
            MaxHealthEstimate = 8000,
            Events = new List<RecapEvent> { Dmg(0.4, 5000), Dmg(1.2, 3000) },
        });

        Assert.Equal(2, rows.Count);
        Assert.Equal("-1s", rows[0].Name);        // oldest first
        Assert.Equal("0s", rows[1].Name);
        Assert.Equal(0, rows[1].Percent, 3);       // death second → 0% hp
        // HP at end of the second before death = the net damage that then killed them in second 0 = dmg[0] = 5000.
        Assert.Equal(5000.0 / 8000, rows[0].Percent, 3);   // 0.625
    }

    [Fact]
    public void Health_reconstructs_backward_and_a_heal_raises_it()
    {
        var rows = DeathRecapEngine.Build(new RecapReading
        {
            MaxHealthEstimate = 10000,
            Events = new List<RecapEvent> { Dmg(0.5, 5000), Dmg(1.5, 1000), Heal(2.5, 2000) },
        });
        Assert.Equal(new[] { "-2s", "-1s", "0s" }, rows.ConvertAll(r => r.Name).ToArray());
        Assert.Equal(0.60, rows[0].Percent, 3);
        Assert.Equal(0.50, rows[1].Percent, 3);
        Assert.Equal(0.00, rows[2].Percent, 3);
    }

    [Fact]
    public void Health_percent_clamps_at_100_when_window_damage_exceeds_the_estimate()   // plan-watch item 2
    {
        var rows = DeathRecapEngine.Build(new RecapReading
        {
            MaxHealthEstimate = 4000,
            Events = new List<RecapEvent> { Dmg(0.5, 5000), Dmg(1.5, 3000) },  // cumulative to top = 5000 > 4000
        });
        Assert.Equal(1.0, rows[0].Percent, 3);                 // pinned at 100%
        Assert.Equal("100%", rows[0].FormattedPercent);
        Assert.Equal(NumberFormat.Abbreviate(4000), rows[0].FormattedValue);   // est-health clamped to the estimate
    }

    [Fact]
    public void Empty_seconds_are_skipped_and_dmg_heals_are_colored_secondaries()
    {
        var rows = DeathRecapEngine.Build(new RecapReading
        {
            MaxHealthEstimate = 10000,
            Events = new List<RecapEvent> { Dmg(0.5, 5000), Heal(0.7, 1000), Dmg(4.5, 2000) },
        });
        // seconds present: 0 (dmg 5000 + heal 1000) and 4 (dmg 2000); seconds 1,2,3 skipped.
        Assert.Equal(new[] { "-4s", "0s" }, rows.ConvertAll(r => r.Name).ToArray());
        var deathRow = rows[1];
        Assert.Equal(2, deathRow.Secondaries.Count);
        Assert.Equal("-5K", deathRow.Secondaries[0].FormattedValue);   // dmg, red
        Assert.Equal(DeathRecapEngine.DmgArgb, deathRow.Secondaries[0].Argb);
        Assert.Equal("+1K", deathRow.Secondaries[1].FormattedValue);   // heals, green
        Assert.Equal(DeathRecapEngine.HealArgb, deathRow.Secondaries[1].Argb);
    }

    [Fact]
    public void Bar_fraction_equals_the_health_percent()
    {
        var rows = DeathRecapEngine.Build(new RecapReading
        {
            MaxHealthEstimate = 10000,
            Events = new List<RecapEvent> { Dmg(0.5, 5000), Dmg(1.5, 5000) },
        });
        foreach (var r in rows) Assert.Equal(r.Percent, r.BarFraction, 3);
    }
}
