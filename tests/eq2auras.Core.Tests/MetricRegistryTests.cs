using System.Linq;
using Eq2Auras.Core.Meter;
using Xunit;

public class MetricRegistryTests
{
    [Theory]
    [InlineData("encdps", MetricBreakdownSource.OutgoingDamage)]
    [InlineData("damagetaken", MetricBreakdownSource.IncomingDamage)]
    [InlineData("enchps", MetricBreakdownSource.OutgoingHealing)]
    [InlineData("totalhealing", MetricBreakdownSource.OutgoingHealing)]
    [InlineData("healstaken", MetricBreakdownSource.IncomingHealing)]
    [InlineData("powerheal", MetricBreakdownSource.PowerReplenish)]
    [InlineData("cures", MetricBreakdownSource.Cures)]
    public void Each_metric_names_its_by_ability_breakdown_bucket(string key, MetricBreakdownSource expected)
    {
        var metric = MetricRegistry.All.Single(m => m.Key == key);
        Assert.Equal(expected, metric.BreakdownSource);
    }

    [Fact]
    public void Damage_dealt_and_damage_taken_read_opposite_buckets()
    {
        // Same Damage total, opposite direction — the descriptor cannot be derived from `select`.
        var dealt = MetricRegistry.All.Single(m => m.Key == "encdps");
        var taken = MetricRegistry.All.Single(m => m.Key == "damagetaken");
        Assert.NotEqual(dealt.BreakdownSource, taken.BreakdownSource);
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(7.5, "8")]              // sub-1K is integer-rounded, NOT 3-sig-figs "7.50"
    [InlineData(950, "950")]
    [InlineData(999, "999")]
    [InlineData(1_000, "1K")]           // 1.00 -> trailing zeros dropped
    [InlineData(1_240, "1.24K")]        // <10 mantissa -> 2 decimals (3 sig figs)
    [InlineData(1_460, "1.46K")]        // was "1.5K" under the old one-decimal format
    [InlineData(12_400, "12.4K")]       // 10..100 mantissa -> 1 decimal
    [InlineData(124_000, "124K")]       // >=100 mantissa -> 0 decimals
    [InlineData(890_000, "890K")]
    [InlineData(1_240_000, "1.24M")]
    [InlineData(9_990_000, "9.99M")]    // the 5-char worst case
    [InlineData(12_400_000, "12.4M")]
    [InlineData(124_000_000, "124M")]
    [InlineData(1_400_000, "1.4M")]     // 1.40 -> trailing zero dropped
    [InlineData(4_200_000_000, "4.2B")]
    public void Abbreviates_with_three_sig_figs(double value, string expected)
        => Assert.Equal(expected, NumberFormat.Abbreviate(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-such-metric")]      // file from a future version
    public void Unknown_or_missing_key_resolves_to_the_dps_default(string? key)
        => Assert.Equal(MetricRegistry.DefaultKey, MetricRegistry.Resolve(key).Key);

    [Fact]
    public void Ships_the_seven_scalar_metrics_then_the_deaths_event_metric_in_order()
    {
        Assert.Equal(new[] { "encdps", "enchps", "cures", "damagetaken", "totalhealing", "healstaken", "powerheal", "deaths" },
            System.Linq.Enumerable.Select(MetricRegistry.All, m => m.Key));
    }

    [Fact]
    public void Selectors_read_the_matching_totals()
    {
        var reading = new CombatantReading { Name = "Zephyria", Damage = 500, Healed = 300, CureDispels = 7 };

        Assert.Equal(500, MetricRegistry.Resolve("encdps").Select(reading));
        Assert.Equal(300, MetricRegistry.Resolve("enchps").Select(reading));
        Assert.Equal(7, MetricRegistry.Resolve("cures").Select(reading));
    }

    [Fact]
    public void Rates_are_rates_and_counts_are_counts()
    {
        Assert.True(MetricRegistry.Resolve("encdps").IsRate);
        Assert.True(MetricRegistry.Resolve("enchps").IsRate);
        Assert.False(MetricRegistry.Resolve("cures").IsRate);
        Assert.Equal("7", MetricRegistry.Resolve("cures").Format(7));          // counts: plain integer
        Assert.Equal("1.4M", MetricRegistry.Resolve("encdps").Format(1_400_000));
    }

    [Fact]
    public void Find_returns_the_metric_or_null_without_a_default()
    {
        Assert.Equal("enchps", MetricRegistry.Find("enchps").Key);
        Assert.Null(MetricRegistry.Find(null));            // no secondary
        Assert.Null(MetricRegistry.Find("no-such-metric")); // unknown -> off, NOT DPS
    }

    [Fact]
    public void ResolvePrimary_null_is_cleared()
        => Assert.Null(MetricRegistry.ResolvePrimary(null));   // cleared -> show nothing

    [Theory]
    [InlineData("")]                 // non-null but unknown -> DPS (forward-compat)
    [InlineData("no-such-metric")]
    public void ResolvePrimary_unknown_nonnull_key_is_dps(string key)
        => Assert.Equal("encdps", MetricRegistry.ResolvePrimary(key).Key);

    [Fact]
    public void ResolvePrimary_known_key_resolves()
        => Assert.Equal("enchps", MetricRegistry.ResolvePrimary("enchps").Key);

    [Theory]
    [InlineData("damagetaken", "Damage Taken", "Damage")]
    [InlineData("totalhealing", "Total Healing", "Healing")]
    [InlineData("healstaken", "Healing Taken", "Healing")]
    [InlineData("powerheal", "Power Replenish", "Utility")]
    public void New_total_metrics_are_registered_as_abbreviated_non_rates(string key, string label, string category)
    {
        var metric = MetricRegistry.Resolve(key);

        Assert.Equal(key, metric.Key);
        Assert.Equal(label, metric.Label);
        Assert.Equal(category, metric.Category);
        Assert.False(metric.IsRate);                 // a total, not a rate — never divided by duration
        Assert.Equal("1.5M", metric.Format(1_500_000));   // K/M/B abbreviation, not a plain integer
    }

    [Theory]
    [InlineData("damagetaken", 4200L, 0L, 0L, 4200)]
    [InlineData("healstaken", 0L, 900L, 0L, 900)]
    [InlineData("powerheal", 0L, 0L, 700L, 700)]
    public void New_metric_selectors_read_their_combatant_field(string key, long dmgTaken, long healsTaken, long powerReplenish, double expected)
    {
        var reading = new CombatantReading
        {
            DamageTaken = dmgTaken,
            HealsTaken = healsTaken,
            PowerReplenish = powerReplenish,
        };

        Assert.Equal(expected, MetricRegistry.Resolve(key).Select(reading));
    }

    [Fact]
    public void Total_healing_and_hps_share_the_healed_selector()
    {
        var reading = new CombatantReading { Healed = 12_000 };

        Assert.Equal(12_000, MetricRegistry.Resolve("totalhealing").Select(reading));
        Assert.Equal(12_000, MetricRegistry.Resolve("enchps").Select(reading));
    }
}
