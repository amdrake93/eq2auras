using Eq2Auras.Core.Meter;
using Xunit;

public class ClassTreeTests
{
    [Theory]
    [InlineData(FinalClass.Paladin, Subclass.Crusader)]
    [InlineData(FinalClass.Shadowknight, Subclass.Crusader)]
    [InlineData(FinalClass.Monk, Subclass.Brawler)]
    [InlineData(FinalClass.Bruiser, Subclass.Brawler)]
    [InlineData(FinalClass.Guardian, Subclass.Warrior)]
    [InlineData(FinalClass.Berserker, Subclass.Warrior)]
    [InlineData(FinalClass.Templar, Subclass.Cleric)]
    [InlineData(FinalClass.Inquisitor, Subclass.Cleric)]
    [InlineData(FinalClass.Warden, Subclass.Druid)]
    [InlineData(FinalClass.Fury, Subclass.Druid)]
    [InlineData(FinalClass.Mystic, Subclass.Shaman)]
    [InlineData(FinalClass.Defiler, Subclass.Shaman)]
    [InlineData(FinalClass.Swashbuckler, Subclass.Rogue)]
    [InlineData(FinalClass.Brigand, Subclass.Rogue)]
    [InlineData(FinalClass.Troubador, Subclass.Bard)]
    [InlineData(FinalClass.Dirge, Subclass.Bard)]
    [InlineData(FinalClass.Ranger, Subclass.Predator)]
    [InlineData(FinalClass.Assassin, Subclass.Predator)]
    [InlineData(FinalClass.Wizard, Subclass.Sorcerer)]
    [InlineData(FinalClass.Warlock, Subclass.Sorcerer)]
    [InlineData(FinalClass.Conjuror, Subclass.Summoner)]
    [InlineData(FinalClass.Necromancer, Subclass.Summoner)]
    [InlineData(FinalClass.Illusionist, Subclass.Enchanter)]
    [InlineData(FinalClass.Coercer, Subclass.Enchanter)]
    public void Each_final_maps_to_its_subclass(FinalClass final, Subclass expected)
        => Assert.Equal(expected, ClassTree.SubclassOf(final));

    [Fact]
    public void Unknown_and_zero_defaults_hold()
    {
        Assert.Equal(0, (int)Subclass.Unknown);
        Assert.Equal(0, (int)FinalClass.Unknown);
        Assert.Equal(Subclass.Unknown, ClassTree.SubclassOf(FinalClass.Unknown));
    }
}
