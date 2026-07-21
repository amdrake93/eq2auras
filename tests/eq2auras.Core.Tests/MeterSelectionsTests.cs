using System.Linq;
using Eq2Auras.Core.Meter;
using Xunit;

public class MeterSelectionsTests
{
    [Fact]
    public void The_nine_selections_cover_every_metric_and_add_two_enemy_twins()
    {
        Assert.Equal(9, MeterSelections.Primary.Count);

        // Every registry metric appears at least once as an allies selection.
        foreach (var metric in MetricRegistry.All)
            Assert.Contains(MeterSelections.Primary, s => s.Scope == MeterScope.Allies && s.MetricKey == metric.Key);

        // The two enemy twins reuse existing metric keys.
        Assert.Contains(MeterSelections.Primary, s => s.Scope == MeterScope.Enemies && s.MetricKey == "damagetaken" && s.Label == "Enemy Damage Taken");
        Assert.Contains(MeterSelections.Primary, s => s.Scope == MeterScope.Enemies && s.MetricKey == "totalhealing" && s.Label == "Enemy Healing Done");
    }

    [Theory]
    [InlineData(MeterScope.Allies, "damagetaken", "Damage Taken")]
    [InlineData(MeterScope.Enemies, "damagetaken", "Enemy Damage Taken")]
    [InlineData(MeterScope.Allies, "encdps", "DPS")]
    public void Resolve_returns_the_selection_matching_scope_and_metric(MeterScope scope, string key, string expectedLabel)
    {
        Assert.Equal(expectedLabel, MeterSelections.Resolve(scope, key).Label);
    }

    [Fact]
    public void Resolve_returns_null_when_no_selection_matches()
    {
        Assert.Null(MeterSelections.Resolve(MeterScope.Enemies, "cures"));   // no enemy-cures selection defined
        Assert.Null(MeterSelections.Resolve(MeterScope.Allies, "nonsense"));
    }

    [Fact]
    public void Every_selection_metric_resolves_to_a_real_registry_metric()
    {
        foreach (var selection in MeterSelections.Primary)
            Assert.Contains(MetricRegistry.All, m => m.Key == selection.MetricKey);
    }
}
