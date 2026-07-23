using System.Linq;
using Eq2Auras.Core.Meter;
using Xunit;

public class DeathsMetricRegistrationTests
{
    [Fact]
    public void Deaths_is_a_registered_event_metric_in_the_damage_family()
    {
        var def = MetricRegistry.ResolvePrimary("deaths");
        Assert.NotNull(def);
        Assert.True(def.IsEvent);
        Assert.Equal("Deaths", def.Label);
        Assert.Equal("Damage", def.Category);
        Assert.False(def.IsRate);
        Assert.Equal(MetricBreakdownSource.Deaths, def.BreakdownSource);
    }

    [Fact]
    public void The_seven_original_metrics_stay_scalar()
        => Assert.All(MetricRegistry.All.Where(m => m.Key != "deaths"), m => Assert.False(m.IsEvent));

    [Fact]
    public void Deaths_is_a_predefined_allies_selection()
    {
        var sel = MeterSelections.Resolve(MeterScope.Allies, "deaths");
        Assert.NotNull(sel);
        Assert.Equal("Deaths", sel.Label);
        Assert.Equal(MeterScope.Allies, sel.Scope);
    }
}
