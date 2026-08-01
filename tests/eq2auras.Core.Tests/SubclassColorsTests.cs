using Eq2Auras.Core.Meter;
using Xunit;

public class SubclassColorsTests
{
    [Theory]
    [InlineData(Subclass.Crusader, unchecked((int)0xFFC9A227))]
    [InlineData(Subclass.Brawler, unchecked((int)0xFF00FF98))]
    [InlineData(Subclass.Warrior, unchecked((int)0xFFC69B6D))]
    [InlineData(Subclass.Cleric, unchecked((int)0xFFFFFFFF))]
    [InlineData(Subclass.Druid, unchecked((int)0xFFFF7C0A))]
    [InlineData(Subclass.Shaman, unchecked((int)0xFF0070DD))]
    [InlineData(Subclass.Rogue, unchecked((int)0xFFFFF468))]
    [InlineData(Subclass.Bard, unchecked((int)0xFF6C3FB5))]
    [InlineData(Subclass.Predator, unchecked((int)0xFFAAD372))]
    [InlineData(Subclass.Sorcerer, unchecked((int)0xFF3FC7EB))]
    [InlineData(Subclass.Summoner, unchecked((int)0xFF8788EE))]
    [InlineData(Subclass.Enchanter, unchecked((int)0xFF33937F))]
    public void Each_subclass_has_its_locked_color(Subclass s, int argb)
        => Assert.Equal(argb, SubclassColors.ArgbFor(s));

    [Fact]
    public void Unknown_is_neutral_grey()
    {
        Assert.Equal(unchecked((int)0xFF8B93A3), SubclassColors.ArgbFor(Subclass.Unknown));
        Assert.Equal(SubclassColors.Grey, SubclassColors.ArgbFor(Subclass.Unknown));
    }
}
