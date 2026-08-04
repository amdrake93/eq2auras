using Eq2Auras.Core.Meter;
using Xunit;

public class SegmentRulesTests
{
    [Fact]
    public void New_combat_snaps_non_pinned_non_current_to_current()
        => Assert.Equal(SegmentKind.Current, SegmentRules.OnNewCombat(SegmentSelection.Zonewide(), pinned: false).Kind);

    [Fact]
    public void New_combat_leaves_a_pinned_selection_unchanged()
    {
        var r = SegmentRules.OnNewCombat(SegmentSelection.Historical("Nizara#1", 123), pinned: true);
        Assert.Equal(SegmentKind.Historical, r.Kind);
        Assert.Equal(123, r.StartTicks);
    }

    [Fact]
    public void New_combat_on_current_is_a_no_op()
        => Assert.Equal(SegmentKind.Current, SegmentRules.OnNewCombat(SegmentSelection.Current(), pinned: false).Kind);

    [Fact]
    public void Picking_zonewide_clears_the_knob_others_do_not()
    {
        Assert.True(SegmentRules.ClearsKnobOnPick(SegmentSelection.Zonewide()));
        Assert.False(SegmentRules.ClearsKnobOnPick(SegmentSelection.Current()));
        Assert.False(SegmentRules.ClearsKnobOnPick(SegmentSelection.Historical("z", 1)));
    }

    [Fact]
    public void From_mode_maps_persisted_mode_to_selection()
    {
        Assert.Equal(SegmentKind.Current, SegmentRules.FromMode(SegmentMode.Current).Kind);
        Assert.Equal(SegmentKind.Zonewide, SegmentRules.FromMode(SegmentMode.Zonewide).Kind);
    }

    [Fact]
    public void Selections_have_value_equality()
    {
        Assert.Equal(SegmentSelection.Historical("z", 5), SegmentSelection.Historical("z", 5));
        Assert.NotEqual(SegmentSelection.Historical("z", 5), SegmentSelection.Historical("z", 6));
    }

    [Fact]
    public void An_all_pick_is_not_equal_to_a_fight_pick_in_the_same_zone()
        => Assert.NotEqual(SegmentSelection.HistoricalAll("z"), SegmentSelection.Historical("z", 0));
}
