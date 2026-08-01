using System.Collections.Generic;
using Eq2Auras.Core.Meter;
using Xunit;

public class MeterRowShapeTests
{
    [Fact]
    public void CombatantReading_carries_ability_names()
    {
        var r = new CombatantReading { Name = "Bob", AbilityNames = new List<string> { "Lich's Siphoning" } };
        Assert.Single(r.AbilityNames);
    }

    [Fact]
    public void MeterRow_background_defaults_null()
    {
        Assert.Null(new MeterRow().BackgroundArgb);
        Assert.Equal(1, new MeterRow { BackgroundArgb = 1 }.BackgroundArgb);
    }
}
