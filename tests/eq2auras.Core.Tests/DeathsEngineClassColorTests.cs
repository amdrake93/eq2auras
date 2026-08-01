using System.Collections.Generic;
using Eq2Auras.Core.Meter;
using Xunit;

public class DeathsEngineClassColorTests
{
    [Fact]
    public void Death_row_fill_is_the_victims_class_color()
    {
        var deaths = new List<DeathRecord>
        {
            new DeathRecord { Victim = "Bob", Ordinal = 1, TimeOfDeathSeconds = 5, DrillKey = "Bob#1" },
        };
        int purple = SubclassColors.ArgbFor(Subclass.Summoner);
        var frame = DeathsEngine.BuildList(deaths, 10, name => name == "Bob" ? purple : SubclassColors.Grey);
        Assert.Equal(purple, frame.Rows[0].FillArgb);
    }

    [Fact]
    public void No_resolver_defaults_to_grey()
    {
        var deaths = new List<DeathRecord> { new DeathRecord { Victim = "Bob", Ordinal = 1, TimeOfDeathSeconds = 5, DrillKey = "Bob#1" } };
        var frame = DeathsEngine.BuildList(deaths, 10);
        Assert.Equal(SubclassColors.Grey, frame.Rows[0].FillArgb);
    }
}
