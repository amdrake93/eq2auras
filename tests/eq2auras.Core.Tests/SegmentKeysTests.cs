using System.Linq;
using Eq2Auras.Core.Meter;
using Xunit;

public class SegmentKeysTests
{
    [Fact]
    public void Key_encodes_kind_and_uses_current_zone_for_zonewide()
    {
        Assert.Equal("C", SegmentKeys.Of(SegmentSelection.Current(), "Nizara#1"));
        Assert.Equal("Z:Nizara#1", SegmentKeys.Of(SegmentSelection.Zonewide(), "Nizara#1"));
        Assert.Equal("H:Antonica#2:55", SegmentKeys.Of(SegmentSelection.Historical("Antonica#2", 55), "Nizara#1"));
    }

    [Fact]
    public void Distinct_collapses_all_current_windows_to_one_and_always_includes_current()
    {
        var keys = SegmentKeys.Distinct(new[] { SegmentSelection.Current(), SegmentSelection.Current() }, "n");
        Assert.Equal(new[] { "C" }, keys);
    }

    [Fact]
    public void Distinct_splits_when_selections_differ_and_still_includes_current()
    {
        var keys = SegmentKeys.Distinct(new[] { SegmentSelection.Zonewide(), SegmentSelection.Historical("a", 3) }, "n");
        Assert.Contains("C", keys);              // added for fallback even though no window asked for it
        Assert.Contains("Z:n", keys);
        Assert.Contains("H:a:3", keys);
        Assert.Equal(3, keys.Count);
    }

    [Fact]
    public void Fallback_sends_a_culled_historical_to_current_but_leaves_resolved_and_non_historical_alone()
    {
        var h = SegmentSelection.Historical("a", 3);
        Assert.Equal(SegmentKind.Current, SegmentKeys.FallbackOnMissing(h, resolved: false).Kind);
        Assert.Equal(SegmentKind.Historical, SegmentKeys.FallbackOnMissing(h, resolved: true).Kind);
        Assert.Equal(SegmentKind.Zonewide, SegmentKeys.FallbackOnMissing(SegmentSelection.Zonewide(), resolved: false).Kind);
    }
}
