using Eq2Auras.Core.Config;
using Xunit;

public class ClassColorsConfigTests
{
    [Fact]
    public void New_config_has_class_colors_on()
    {
        // Stored inverted (disableClassColors); the 0-value false = colours on, so a fresh
        // window shows class colours with no field initializer (the DCJS 0-value rule).
        var c = new MeterWindowConfig();
        Assert.False(c.DisableClassColors);
    }

    [Fact]
    public void A_window_with_no_disable_flag_keeps_class_colors_on()
    {
        // DCJS skips the initializer on deserialize; an absent "disableClassColors" -> false (on).
        var json = "{\"meter\":{\"enabled\":true,\"windows\":[{\"metricKey\":\"encdps\"}]}}";
        var parsed = Settings.Parse(json);
        Assert.False(parsed.Meter.Windows[0].DisableClassColors);
    }

    [Fact]
    public void Disabling_class_colors_round_trips()
    {
        var json = "{\"meter\":{\"enabled\":true,\"windows\":[{\"metricKey\":\"encdps\",\"disableClassColors\":true}]}}";
        var parsed = Settings.Parse(json);
        Assert.True(parsed.Meter.Windows[0].DisableClassColors);
        Assert.Contains("\"disableClassColors\":true", parsed.ToJson());
    }
}
