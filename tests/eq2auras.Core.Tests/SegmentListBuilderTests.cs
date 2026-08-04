using System.Collections.Generic;
using System.Linq;
using Eq2Auras.Core.Meter;
using Xunit;

public class SegmentListBuilderTests
{
    static SegmentListBuilder.RawEncounter Enc(string t, double d, int lvl, long ticks, bool all = false)
        => new SegmentListBuilder.RawEncounter { Title = t, DurationSeconds = d, SuccessLevel = lvl, StartTicks = ticks, IsAll = all };

    static SegmentListBuilder.RawZone Zone(string name, string key, bool current, bool populateAll, long ticks, params SegmentListBuilder.RawEncounter[] encs)
        => new SegmentListBuilder.RawZone { ZoneName = name, ZoneKey = key, IsCurrent = current, PopulateAll = populateAll, StartTicks = ticks, Encounters = encs.ToList() };

    [Fact]
    public void Drops_zero_duration_fights_orders_newest_first_and_all_leads()
    {
        var z = Zone("Nizara", "Nizara#1", current: true, populateAll: true, ticks: 500,
            Enc("All", 847, 0, 0, all: true), Enc("Meas", 134, 1, 200), Enc("a temple rat", 0, 3, 300), Enc("Trash", 38, 1, 100));
        var g = SegmentListBuilder.Build(new[] { z }).Zones.Single();
        Assert.True(g.All.Available);
        Assert.True(g.All.IsAll);
        Assert.Equal(EncounterOutcome.Unknown, g.All.Outcome);
        Assert.Equal(new[] { "Meas", "Trash" }, g.Fights.Select(f => f.Title).ToArray());
        Assert.Equal(EncounterOutcome.Win, g.Fights[0].Outcome);
    }

    [Fact]
    public void Zone_without_populate_all_carries_a_disabled_all_placeholder()
    {
        var g = SegmentListBuilder.Build(new[] {
            Zone("Antonica", "a#2", false, populateAll: false, ticks: 10, Enc("a stalker", 22, 1, 5)) }).Zones.Single();
        Assert.NotNull(g.All);
        Assert.False(g.All.Available);   // disabled placeholder, not omitted (SPEC §Availability)
        Assert.Single(g.Fights);
    }

    [Fact]
    public void Zonewide_available_reflects_the_current_zone_populate_all()
    {
        var currentOff = SegmentListBuilder.Build(new[] {
            Zone("Nizara", "n", current: true, populateAll: false, ticks: 2, Enc("y", 5, 1, 2)) });
        Assert.False(currentOff.ZonewideAvailable);
        var currentOn = SegmentListBuilder.Build(new[] {
            Zone("Nizara", "n", current: true, populateAll: true, ticks: 2, Enc("All", 5, 0, 0, all: true), Enc("y", 5, 1, 2)) });
        Assert.True(currentOn.ZonewideAvailable);
    }

    [Fact]
    public void Zones_ordered_current_first_then_newest_first()
    {
        var older = Zone("Antonica", "a", false, false, ticks: 100, Enc("x", 5, 1, 1));
        var newerNonCurrent = Zone("Zek", "z", false, false, ticks: 300, Enc("w", 5, 1, 1));
        var current = Zone("Nizara", "n", true, false, ticks: 200, Enc("y", 5, 1, 1));
        var zones = SegmentListBuilder.Build(new[] { older, newerNonCurrent, current }).Zones;
        Assert.True(zones[0].IsCurrent);                       // current first
        Assert.Equal(new[] { "Zek", "Antonica" }, zones.Skip(1).Select(z => z.ZoneName).ToArray());  // then newest-first
    }

    [Fact]
    public void Empty_group_with_no_available_all_and_no_fights_is_dropped()
    {
        var junk = Zone("zoneDataTerm-import", "imp", false, false, ticks: 1);   // no encounters
        var real = Zone("Nizara", "n", true, false, ticks: 2, Enc("y", 5, 1, 1));
        var zones = SegmentListBuilder.Build(new[] { junk, real }).Zones;
        Assert.Single(zones);
        Assert.Equal("Nizara", zones[0].ZoneName);
    }
}
