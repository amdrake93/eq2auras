using Eq2Auras.Core.Meter;
using Xunit;

public class MetricRegistryTests
{
    [Theory]
    [InlineData(0, "0")]
    [InlineData(950, "950")]
    [InlineData(1_000, "1K")]
    [InlineData(1_460, "1.5K")]        // one decimal, rounded (1_450 avoided: float midpoint formats differ across runtimes)
    [InlineData(890_000, "890K")]
    [InlineData(1_400_000, "1.4M")]
    [InlineData(4_200_000_000, "4.2B")]
    public void Abbreviates_with_kmb_family(double value, string expected)
        => Assert.Equal(expected, NumberFormat.Abbreviate(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-such-metric")]      // file from a future version
    public void Unknown_or_missing_key_resolves_to_the_dps_default(string? key)
        => Assert.Equal(MetricRegistry.DefaultKey, MetricRegistry.Resolve(key).Key);

    [Fact]
    public void Ships_exactly_the_three_slice1_metrics()
    {
        Assert.Equal(new[] { "encdps", "enchps", "cures" },
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
}
