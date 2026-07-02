using Eq2Auras.Core.Config;
using Xunit;

public class SettingsTests
{
    [Fact]
    public void Roundtrips_all_fields()
    {
        var settings = new Settings
        {
            ColorSource = ColorSource.Greyscale,
            EscalationStyle = EscalationStyle.HighlightInPlace
        };

        var parsed = Settings.Parse(settings.ToJson());

        Assert.Equal(ColorSource.Greyscale, parsed.ColorSource);
        Assert.Equal(EscalationStyle.HighlightInPlace, parsed.EscalationStyle);
    }

    [Theory]
    [InlineData("")]                       // empty file
    [InlineData("not json at all {{{")]    // corrupt file
    [InlineData("{}")]                     // old file missing every field
    [InlineData("{\"someFutureKnob\":7}")] // file from a NEWER version
    public void Bad_or_partial_json_yields_defaults(string json)
    {
        var parsed = Settings.Parse(json);

        Assert.Equal(ColorSource.Palette, parsed.ColorSource);
        Assert.Equal(EscalationStyle.CenterRadial, parsed.EscalationStyle);
    }
}
