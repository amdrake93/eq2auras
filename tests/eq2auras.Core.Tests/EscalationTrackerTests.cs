using System.Collections.Generic;
using System.Linq;
using Eq2Auras.Core.Config;
using Eq2Auras.Core.Timers;
using Xunit;

public class EscalationTrackerTests
{

    private static TimerReading Reading(string name, int timeLeft,
        int warning = 10, int total = 30, string combatant = "none", int removeValue = -15)
        => new TimerReading
        {
            Name = name, Combatant = combatant, TimeLeft = timeLeft,
            WarningValue = warning, TotalSeconds = total,
            RemoveValueSeconds = removeValue,
            RawPreciseTimeLeft = timeLeft, FillArgb = -16776961
        };

    private static List<TimerReading> R(params TimerReading[] readings) => readings.ToList();

    [Fact]
    public void Calm_timers_stay_in_the_list_and_center_is_empty()
    {
        var frame = new EscalationTracker().Tick(R(Reading("a", 25), Reading("b", 18)));

        Assert.Equal(2, frame.ListRows.Count);
        Assert.Empty(frame.CenterElements);
    }

    [Fact]
    public void Imminent_timer_leaves_the_list_and_becomes_a_pie()
    {
        var frame = new EscalationTracker().Tick(R(Reading("boss", 8), Reading("calm", 25)));

        Assert.Single(frame.ListRows);
        Assert.Equal("calm", frame.ListRows[0].Name);
        var pie = Assert.Single(frame.CenterElements);
        Assert.Equal(CenterElementKind.Pie, pie.Kind);
        Assert.Equal("boss", pie.Name);
        Assert.Equal(8, pie.SecondsLeft);
        Assert.Equal(0.8, pie.PieFraction, 2);   // 8s left of a 10s warning window
        Assert.Equal(8.0, pie.PreciseSecondsLeft, 2);  // drives the smooth drain animation
    }

    [Fact]
    public void Pie_is_full_at_the_moment_of_escalation()
    {
        var frame = new EscalationTracker().Tick(R(Reading("boss", 10)));

        Assert.Equal(1.0, frame.CenterElements[0].PieFraction, 2);
    }

    [Fact]
    public void Overflow_imminents_wait_in_the_list_as_highlighted_rows()
    {
        var frame = new EscalationTracker().Tick(
            R(Reading("a", 2), Reading("b", 4), Reading("c", 6), Reading("d", 8), Reading("calm", 25)));

        Assert.Equal(3, frame.CenterElements.Count);   // cap
        Assert.Equal(new[] { "a", "b", "c" }, frame.CenterElements.Select(e => e.Name).ToArray());
        Assert.Equal(2, frame.ListRows.Count);          // overflow "d" + "calm"
        Assert.Equal("d", frame.ListRows[0].Name);
        Assert.Equal(TimerUrgency.Imminent, frame.ListRows[0].Urgency);
    }

    [Fact]
    public void Vanished_key_shows_nothing_gone_means_gone()
    {
        // Overdue is DATA-DRIVEN: the timer's own RemoveValue config decides whether an
        // overdue window exists. Once ACT stops reporting the timer, we show nothing —
        // no artificial floor outliving the data.
        var tracker = new EscalationTracker();
        tracker.Tick(R(Reading("boss", 1)));                       // escalated
        var frame = tracker.Tick(R());                        // ACT removed it

        Assert.Empty(frame.CenterElements);
        Assert.Empty(frame.ListRows);
    }

    [Fact]
    public void Negative_TimeLeft_in_the_data_shows_LATE_counting_up()
    {
        // A timer configured to linger past zero (negative RemoveValue) keeps being
        // reported by ACT with negative TimeLeft — LATE shows for exactly that window.
        var frame = new EscalationTracker().Tick(R(Reading("boss", -3, removeValue: -15)));

        Assert.Empty(frame.ListRows);
        var late = Assert.Single(frame.CenterElements);
        Assert.Equal(CenterElementKind.Late, late.Kind);
        Assert.Equal("boss", late.Name);
        Assert.Equal(3, late.LateSeconds);                              // -TimeLeft, directly
    }

    [Fact]
    public void Remove_at_zero_timers_never_show_LATE()
    {
        // The timer's own config says "gone at 0" — even while ACT's laggy clock still
        // reports it at -1/-2 pending removal, we show nothing. The overdue window is
        // the timer owner's RemoveValue choice.
        var frame = new EscalationTracker().Tick(R(Reading("boss", -2, removeValue: 0)));

        Assert.Empty(frame.ListRows);
        Assert.Empty(frame.CenterElements);
    }

    [Fact]
    public void Reset_replaces_LATE_with_the_fresh_countdown()
    {
        var tracker = new EscalationTracker();
        tracker.Tick(R(Reading("boss", -1)));                       // overdue in the data
        var frame = tracker.Tick(R(Reading("boss", 30)));     // new frame after removal

        Assert.Empty(frame.CenterElements);
        Assert.Equal("boss", Assert.Single(frame.ListRows).Name);
    }

    [Fact]
    public void Refire_does_not_extend_the_governing_countdown()
    {
        // A re-fire adds a second SpellTimer instance, but ACT's engine kills the whole
        // frame when the SOONEST instance expires (measured: `removed` fired at tL=2 with
        // a live second instance). The soonest instance is therefore the only truthful
        // countdown — exactly what ACT's own window shows. Never display the newer one.
        var frame = new EscalationTracker().Tick(
            R(Reading("boss", 5), Reading("boss", 30)));

        Assert.Empty(frame.ListRows);                              // 5s governs -> escalated
        var pie = Assert.Single(frame.CenterElements);
        Assert.Equal(CenterElementKind.Pie, pie.Kind);
        Assert.Equal(5, pie.SecondsLeft);                          // NOT 30
    }

    [Fact]
    public void Governing_instance_at_zero_goes_LATE_even_if_a_newer_instance_lingers()
    {
        var tracker = new EscalationTracker();
        tracker.Tick(R(Reading("boss", 3), Reading("boss", 20)));          // 3s governs
        var frame = tracker.Tick(R(Reading("boss", -1), Reading("boss", 16)));

        Assert.Empty(frame.ListRows);                              // no phantom 16s row
        Assert.Equal(CenterElementKind.Late, Assert.Single(frame.CenterElements).Kind);
    }

    [Fact]
    public void Distinct_combatants_are_not_collapsed()
    {
        var frame = new EscalationTracker().Tick(
            R(Reading("boss", 20, combatant: "MobA"), Reading("boss", 25, combatant: "MobB")));

        Assert.Equal(2, frame.ListRows.Count);
    }

    [Fact]
    public void Calm_key_vanishing_produces_nothing()
    {
        var tracker = new EscalationTracker();
        tracker.Tick(R(Reading("boss", 25)));
        var frame = tracker.Tick(R());

        Assert.Empty(frame.CenterElements);
        Assert.Empty(frame.ListRows);
    }

    [Fact]
    public void Palette_mode_assigns_stable_name_keyed_colors()
    {
        var tracker = new EscalationTracker();   // defaults: ColorSource.Palette
        var first = tracker.Tick(R(Reading("Blanket", 25), Reading("Shield", 20)));
        var again = tracker.Tick(R(Reading("Shield", 19), Reading("Blanket", 24)));

        Assert.Equal(ColorPolicy.PaletteArgb[0], first.ListRows.Single(r => r.Name == "Blanket").FillArgb);
        Assert.Equal(ColorPolicy.PaletteArgb[1], first.ListRows.Single(r => r.Name == "Shield").FillArgb);
        // stable across ticks regardless of this tick's order
        Assert.Equal(ColorPolicy.PaletteArgb[0], again.ListRows.Single(r => r.Name == "Blanket").FillArgb);
        Assert.Equal(ColorPolicy.PaletteArgb[1], again.ListRows.Single(r => r.Name == "Shield").FillArgb);
    }

    [Fact]
    public void ActColor_mode_keeps_the_timers_own_color_softened()
    {
        var tracker = new EscalationTracker(new Settings { ColorSource = ColorSource.ActColor });
        var frame = tracker.Tick(R(Reading("t", 25)));

        Assert.Equal(ColorPolicy.Soften(-16776961), frame.ListRows[0].FillArgb);
    }

    [Fact]
    public void HighlightInPlace_keeps_imminents_in_the_list_and_center_empty()
    {
        var tracker = new EscalationTracker(new Settings { EscalationStyle = EscalationStyle.HighlightInPlace });
        var frame = tracker.Tick(R(Reading("boss", 5), Reading("calm", 25)));

        Assert.Empty(frame.CenterElements);
        Assert.Equal(2, frame.ListRows.Count);
        Assert.Equal(TimerUrgency.Imminent, frame.ListRows.Single(r => r.Name == "boss").Urgency);
    }

    [Fact]
    public void HighlightInPlace_renders_linger_overdue_as_LATE_rows()
    {
        var tracker = new EscalationTracker(new Settings { EscalationStyle = EscalationStyle.HighlightInPlace });
        var frame = tracker.Tick(R(Reading("boss", -2, removeValue: -15), Reading("calm", 25)));

        Assert.Empty(frame.CenterElements);
        var lateRow = frame.ListRows.Single(r => r.Name == "boss");
        Assert.Equal(TimerUrgency.Overdue, lateRow.Urgency);
        Assert.Equal(-2, lateRow.TimeLeft);
        Assert.Equal("boss", frame.ListRows[0].Name);   // overdue sorts first (most urgent)
    }

    [Fact]
    public void HighlightInPlace_still_hides_remove_at_zero_timers_past_zero()
    {
        var tracker = new EscalationTracker(new Settings { EscalationStyle = EscalationStyle.HighlightInPlace });
        var frame = tracker.Tick(R(Reading("boss", -1, removeValue: 0)));

        Assert.Empty(frame.ListRows);
        Assert.Empty(frame.CenterElements);
    }

    [Fact]
    public void LATEs_rank_ahead_of_pies_and_consume_center_slots()
    {
        var frame = new EscalationTracker().Tick(
            R(Reading("dead1", -1), Reading("dead2", -2, combatant: "other"),
              Reading("p1", 2), Reading("p2", 4), Reading("p3", 6)));

        Assert.Equal(3, frame.CenterElements.Count);
        Assert.Equal(2, frame.CenterElements.Count(e => e.Kind == CenterElementKind.Late));
        Assert.Equal("p1", frame.CenterElements.Single(e => e.Kind == CenterElementKind.Pie).Name);
        Assert.Equal(2, frame.ListRows.Count);          // p2, p3 overflow back to the list
    }
}
