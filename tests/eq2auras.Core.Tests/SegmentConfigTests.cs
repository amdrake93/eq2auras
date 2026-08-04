using Eq2Auras.Core.Config;
using Eq2Auras.Core.Meter;
using Xunit;

public class SegmentConfigTests
{
    [Fact]
    public void Segment_mode_zero_value_is_current()
        => Assert.Equal(0, (int)SegmentMode.Current);

    [Fact]
    public void New_config_defaults_to_current_and_auto_return()
    {
        var c = new MeterWindowConfig();
        Assert.Equal(SegmentMode.Current, c.SegmentMode);
        Assert.False(c.PinnedToSegment);          // false = knob on (auto-return)
    }

    [Fact]
    public void A_window_with_no_segment_mode_defaults_to_current()
    {
        // DCJS skips the initializer on deserialize; an absent "segmentMode" -> 0-value Current.
        var json = "{\"meter\":{\"enabled\":true,\"windows\":[{\"metricKey\":\"encdps\"}]}}";
        var parsed = Settings.Parse(json);
        Assert.Equal(SegmentMode.Current, parsed.Meter.Windows[0].SegmentMode);
        Assert.False(parsed.Meter.Windows[0].PinnedToSegment);
    }

    [Fact]
    public void An_unknown_segment_mode_value_is_left_as_is_and_resolves_to_current()
    {
        // A newer version could write a value we don't know; SegmentRules.FromMode maps any
        // non-Zonewide value to Current, so an unknown value degrades safely.
        var json = "{\"meter\":{\"enabled\":true,\"windows\":[{\"metricKey\":\"encdps\",\"segmentMode\":7}]}}";
        var parsed = Settings.Parse(json);
        Assert.Equal(SegmentKind.Current, SegmentRules.FromMode(parsed.Meter.Windows[0].SegmentMode).Kind);
    }

    [Fact]
    public void Zonewide_mode_and_pinned_round_trip_numerically()
    {
        var json = "{\"meter\":{\"enabled\":true,\"windows\":[{\"metricKey\":\"encdps\",\"segmentMode\":1,\"pinnedToSegment\":true}]}}";
        var parsed = Settings.Parse(json);
        Assert.Equal(SegmentMode.Zonewide, parsed.Meter.Windows[0].SegmentMode);
        Assert.True(parsed.Meter.Windows[0].PinnedToSegment);
        Assert.Contains("\"segmentMode\":1", parsed.ToJson());   // DCJS enum-as-number house style
    }
}
