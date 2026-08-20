using System.Collections.Generic;
using System.Linq;
using Eq2Auras.Core.Config;
using Eq2Auras.Core.Timers;
using Xunit;

public class OverlayEngineAssociationTests
{
    private static TimerReading R(string name, bool a = false, bool b = false, string category = "")
        => new TimerReading { Name = name, Category = category, ShowInPanelA = a, ShowInPanelB = b,
            TimeLeft = 25, TotalSeconds = 30, RawPreciseTimeLeft = 25, WarningValue = 10, RemoveValueSeconds = -15, IsMaster = true };

    [Fact]
    public void Panel_flagged_readings_route_to_the_two_panel_groups_as_before()
    {
        var frames = new OverlayEngine(new Settings()).Tick(new List<TimerReading> { R("boss", a: true), R("cd", b: true) });
        Assert.Equal("boss", Assert.Single(frames[0].ListRows).Name);   // panel:1
        Assert.Equal("cd", Assert.Single(frames[1].ListRows).Name);     // panel:2
    }

    [Fact]
    public void A_reserved_category_reading_routes_only_to_the_buff_group()
    {
        var frames = new OverlayEngine(new Settings()).Tick(new List<TimerReading> { R("Bloodlust", category: "eq2auras Buffs") });
        Assert.Empty(frames[0].ListRows);
        Assert.Empty(frames[1].ListRows);
        Assert.Equal("Bloodlust", Assert.Single(frames[2].ListRows).Name);   // category:eq2auras Buffs
    }

    [Fact]
    public void There_are_exactly_three_seeded_groups()
        => Assert.Equal(3, new OverlayEngine(new Settings()).Tick(new List<TimerReading>()).Count);

    [Fact]
    public void A_hand_authored_fourth_group_routes_by_its_own_rule_with_no_new_code()
    {
        // The anti-panel-C litmus at the Settings+engine layer: adding a 4th group bound to a
        // name: rule (as a hand-edited config would) routes correctly — no new branch/boolean,
        // and Normalize must NOT have truncated it (SPEC §Timer groups).
        var s = new Settings();
        s.Panels.Add(new PanelSettings { Sources = new List<SourceRule> { SourceRule.OfName("Special") } });
        var frames = new OverlayEngine(s).Tick(new List<TimerReading> { R("Special") });
        Assert.Equal(4, frames.Count);
        Assert.Equal("Special", Assert.Single(frames[3].ListRows).Name);
        Assert.Empty(frames[0].ListRows);   // untouched by the name rule
    }
}
