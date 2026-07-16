using Eq2Auras.Core.Config;
using Xunit;

public class MeterSettingsTests
{
    [Theory]
    [InlineData("")]                        // empty file
    [InlineData("{}")]                      // old file with no meter section
    [InlineData("{\"meter\":null}")]        // explicit null section
    public void Missing_meter_section_yields_defaults(string json)
    {
        var parsed = Settings.Parse(json);

        Assert.NotNull(parsed.Meter);
        Assert.False(parsed.Meter.Enabled);   // default OFF — opt-in while groundwork (SPEC Part III)
        Assert.False(parsed.Meter.Locked);
        Assert.Null(parsed.Meter.Left);       // null, never 0 — 0 is a real screen edge
        Assert.Null(parsed.Meter.Top);
        Assert.Null(parsed.Meter.MetricKey);  // null key -> registry default at resolve time
    }

    [Fact]
    public void Meter_settings_roundtrip()
    {
        var settings = new Settings();
        settings.Meter.Enabled = true;
        settings.Meter.Left = 0;              // zero is a REAL position, must survive
        settings.Meter.Top = 451.5;
        settings.Meter.MetricKey = "enchps";
        settings.Meter.Locked = true;

        var parsed = Settings.Parse(settings.ToJson());

        Assert.True(parsed.Meter.Enabled);
        Assert.Equal(0.0, parsed.Meter.Left);
        Assert.Equal(451.5, parsed.Meter.Top);
        Assert.Equal("enchps", parsed.Meter.MetricKey);
        Assert.True(parsed.Meter.Locked);
    }
}
