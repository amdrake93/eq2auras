using Eq2Auras.Core.Meter;
using Xunit;

public class MeterFamilyColorsTests
{
    [Theory]
    [InlineData("Damage", unchecked((int)0xFFE05A5A))]
    [InlineData("Healing", unchecked((int)0xFF2FBF8F))]
    [InlineData("Utility", unchecked((int)0xFF56B4E9))]
    public void Known_categories_map_to_their_family_color(string category, int expectedArgb)
    {
        Assert.Equal(expectedArgb, MeterFamilyColors.ArgbFor(category));
    }

    [Fact]
    public void Unknown_category_falls_back_to_neutral_grey()
    {
        Assert.Equal(unchecked((int)0xFF8B93A3), MeterFamilyColors.ArgbFor("Threat"));
    }

    [Fact]
    public void Null_category_falls_back_to_neutral_grey()
    {
        Assert.Equal(unchecked((int)0xFF8B93A3), MeterFamilyColors.ArgbFor(null));
    }
}
