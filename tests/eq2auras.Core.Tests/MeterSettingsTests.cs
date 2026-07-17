using System.Collections.Generic;
using Eq2Auras.Core.Config;
using Xunit;

public class MeterSettingsTests
{
    [Theory]
    [InlineData("")]                        // empty file
    [InlineData("{}")]                      // old file with no meter section
    [InlineData("{\"meter\":null}")]        // explicit null section
    public void Missing_meter_section_yields_empty_disabled(string json)
    {
        var parsed = Settings.Parse(json);

        Assert.NotNull(parsed.Meter);
        Assert.False(parsed.Meter.Enabled);      // default OFF — opt-in (SPEC Part III §Settings)
        Assert.NotNull(parsed.Meter.Windows);
        Assert.Empty(parsed.Meter.Windows);      // no window exists until the meter is enabled
    }

    [Fact]
    public void Legacy_single_window_file_migrates_into_one_config()
    {
        // Slice-1 shape: the single window's config sat in flat meter fields, no "windows" key.
        var json = "{\"meter\":{\"enabled\":true,\"metricKey\":\"enchps\",\"left\":100.0,\"top\":200.5,\"locked\":true}}";

        var parsed = Settings.Parse(json);

        Assert.True(parsed.Meter.Enabled);
        var window = Assert.Single(parsed.Meter.Windows);
        Assert.Equal("enchps", window.MetricKey);
        Assert.Equal(100.0, window.Left);
        Assert.Equal(200.5, window.Top);
        Assert.True(window.Locked);
    }

    [Fact]
    public void Enabled_with_no_windows_seeds_one_default()
    {
        // An enabled meter always has at least one window (SPEC Part III §Multiple windows).
        var json = "{\"meter\":{\"enabled\":true,\"windows\":[]}}";

        var parsed = Settings.Parse(json);

        var window = Assert.Single(parsed.Meter.Windows);
        Assert.Null(window.MetricKey);   // null -> DPS at resolve time
        Assert.Null(window.Left);        // null -> host default placement
        Assert.Null(window.Top);
        Assert.False(window.Locked);
    }

    [Fact]
    public void Disabled_with_no_windows_stays_empty()
    {
        var json = "{\"meter\":{\"enabled\":false,\"windows\":[]}}";

        var parsed = Settings.Parse(json);

        Assert.Empty(parsed.Meter.Windows);   // nothing to show while hidden
    }

    [Fact]
    public void Null_window_entries_are_dropped()
    {
        var json = "{\"meter\":{\"enabled\":true,\"windows\":[null,{\"metricKey\":\"encdps\"}]}}";

        var parsed = Settings.Parse(json);

        var window = Assert.Single(parsed.Meter.Windows);
        Assert.Equal("encdps", window.MetricKey);
    }

    [Fact]
    public void Multi_window_roundtrip_preserves_each_config()
    {
        var settings = new Settings();
        settings.Meter.Enabled = true;
        settings.Meter.Windows = new List<MeterWindowConfig>
        {
            new MeterWindowConfig { MetricKey = "encdps", Left = 0, Top = 300, Locked = false },   // 0 is a REAL position
            new MeterWindowConfig { MetricKey = "enchps", Left = 640.5, Top = 300, Locked = true },
        };

        var parsed = Settings.Parse(settings.ToJson());

        Assert.Equal(2, parsed.Meter.Windows.Count);
        Assert.Equal("encdps", parsed.Meter.Windows[0].MetricKey);
        Assert.Equal(0.0, parsed.Meter.Windows[0].Left);
        Assert.False(parsed.Meter.Windows[0].Locked);
        Assert.Equal("enchps", parsed.Meter.Windows[1].MetricKey);
        Assert.Equal(640.5, parsed.Meter.Windows[1].Left);
        Assert.True(parsed.Meter.Windows[1].Locked);
    }
}
