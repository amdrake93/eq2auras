using System.Collections.Generic;
using Eq2Auras.Core.Meter;
using Xunit;

public class DeathRecapEngineTwoToneTests
{
    [Fact]
    public void Recap_rows_are_two_tone_class_ground_dark_hp_bar()
    {
        var reading = new RecapReading
        {
            DrillKey = "Bob#1",
            MaxHealthEstimate = 1000,
            Events = new List<RecapEvent>
            {
                new RecapEvent { SecondsBeforeDeath = 2, Amount = 300, IsHeal = false },
                new RecapEvent { SecondsBeforeDeath = 1, Amount = 400, IsHeal = false },
                new RecapEvent { SecondsBeforeDeath = 0, Amount = 500, IsHeal = false },
            },
        };
        int purple = SubclassColors.ArgbFor(Subclass.Summoner);
        var rows = DeathRecapEngine.Build(reading, purple);
        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Equal(purple, r.BackgroundArgb));
        Assert.All(rows, r => Assert.Equal(DeathRecapEngine.CurrentHpArgb, r.FillArgb));
    }
}
