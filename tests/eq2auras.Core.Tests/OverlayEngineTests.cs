using System.Collections.Generic;
using System.Linq;
using Eq2Auras.Core.Config;
using Eq2Auras.Core.Timers;
using Xunit;

public class OverlayEngineTests
{
    private static TimerReading Reading(string name, int timeLeft, bool inA = false, bool inB = false)
        => new TimerReading
        {
            Name = name, Combatant = "none", TimeLeft = timeLeft,
            RawPreciseTimeLeft = timeLeft, WarningValue = 10, TotalSeconds = 30,
            RemoveValueSeconds = -15, FillArgb = -16776961,
            ShowInPanelA = inA, ShowInPanelB = inB
        };

    [Fact]
    public void Routes_by_panel_flags_mirroring_ACT()
    {
        var engine = new OverlayEngine(new Settings());

        var frames = engine.Tick(new List<TimerReading>
        {
            Reading("a-only", 25, inA: true),
            Reading("b-only", 24, inB: true),
            Reading("both", 23, inA: true, inB: true),
            Reading("neither", 22)
        });

        Assert.Equal(2, frames.Count);
        Assert.Equal(new[] { "both", "a-only" }, frames[0].ListRows.Select(r => r.Name).ToArray());
        Assert.Equal(new[] { "both", "b-only" }, frames[1].ListRows.Select(r => r.Name).ToArray());
    }

    [Fact]
    public void Groups_apply_their_own_knobs_independently()
    {
        var settings = new Settings();
        settings.Panels[1].EscalationStyle = EscalationStyle.HighlightInPlace;
        var engine = new OverlayEngine(settings);

        var frames = engine.Tick(new List<TimerReading> { Reading("boss", 5, inA: true, inB: true) });

        Assert.Empty(frames[0].ListRows);            // A: CenterRadial -> pie in the zone
        Assert.Single(frames[0].CenterElements);
        Assert.Single(frames[1].ListRows);           // B: stays in the list, highlighted
        Assert.Empty(frames[1].CenterElements);
    }

    [Fact]
    public void Dual_flagged_timer_keeps_one_color_identity_across_groups()
    {
        var settings = new Settings();
        settings.Panels[1].ColorSource = ColorSource.Greyscale;
        var engine = new OverlayEngine(settings);

        var frames = engine.Tick(new List<TimerReading>
        {
            Reading("First", 25, inA: true, inB: true),
            Reading("Second", 20, inA: true, inB: true)
        });

        // One slot per name everywhere; each group renders its own ColorSource over it.
        Assert.Equal(ColorPolicy.PaletteArgb[0], frames[0].ListRows.Single(r => r.Name == "First").FillArgb);
        Assert.Equal(ColorPolicy.GreyArgb[0], frames[1].ListRows.Single(r => r.Name == "First").FillArgb);
        Assert.Equal(ColorPolicy.PaletteArgb[1], frames[0].ListRows.Single(r => r.Name == "Second").FillArgb);
        Assert.Equal(ColorPolicy.GreyArgb[1], frames[1].ListRows.Single(r => r.Name == "Second").FillArgb);
    }
}
