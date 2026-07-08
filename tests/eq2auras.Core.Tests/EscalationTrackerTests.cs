using System.Collections.Generic;
using System.Linq;
using Eq2Auras.Core.Config;
using Eq2Auras.Core.Timers;
using Xunit;

public class EscalationTrackerTests
{

    private static readonly System.DateTime BaseStart = new System.DateTime(2026, 7, 8, 20, 0, 0, System.DateTimeKind.Utc);

    private static TimerReading Reading(string name, int timeLeft,
        int warning = 10, int total = 30, string combatant = "none", int removeValue = -15,
        bool master = true, int startOffset = 0)
        => new TimerReading
        {
            Name = name, Combatant = combatant, TimeLeft = timeLeft,
            WarningValue = warning, TotalSeconds = total,
            RemoveValueSeconds = removeValue,
            RawPreciseTimeLeft = timeLeft, FillArgb = -16776961,
            IsMaster = master, StartTime = BaseStart.AddSeconds(startOffset)
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
    public void Non_master_tick_never_governs_or_displays()
    {
        // A DoT tick re-trigger arrives as a non-master instance (ACT pre-applies the
        // OnlyMasterTicks config). It is diagnostics-only: the master keeps governing.
        var frame = new EscalationTracker().Tick(
            R(Reading("boss", 20, startOffset: 0), Reading("boss", 45, master: false, startOffset: 6)));

        var row = Assert.Single(frame.ListRows);
        Assert.Equal(20, row.TimeLeft);                      // NOT the tick's 45
    }

    [Fact]
    public void Master_recast_governs_over_the_older_master()
    {
        // Cooldown truth: the ability just fired, so the older prediction is falsified.
        var frame = new EscalationTracker().Tick(
            R(Reading("boss", 5, startOffset: 0), Reading("boss", 30, startOffset: 9)));

        Assert.Empty(frame.CenterElements);                  // 5s no longer governs -> no pie
        Assert.Equal(30, Assert.Single(frame.ListRows).TimeLeft);
    }

    [Fact]
    public void Master_recast_while_LATE_clears_the_LATE_instantly()
    {
        // THE raid-night bug (observed 51x, 2026-07-05): the overdue corpse must not
        // outrank a live recast while it awaits ACT's purge.
        var frame = new EscalationTracker().Tick(
            R(Reading("boss", -9, startOffset: 0), Reading("boss", 35, total: 35, startOffset: 44)));

        Assert.Empty(frame.CenterElements);                  // no LATE card survives the recast
        Assert.Equal(35, Assert.Single(frame.ListRows).TimeLeft);
    }

    [Fact]
    public void Newest_master_wins_even_with_less_time_than_an_older_modded_master()
    {
        // Timer mods can leave an older master with MORE remaining time; newest still wins
        // (deliberately not ACT's largest-master display — SPEC §Timer identity).
        var frame = new EscalationTracker().Tick(
            R(Reading("boss", 40, total: 60, startOffset: 0), Reading("boss", 30, total: 30, startOffset: 20)));

        Assert.Equal(30, Assert.Single(frame.ListRows).TimeLeft);
    }

    [Fact]
    public void No_masters_displays_nothing_for_the_key()
    {
        // A poll landing mid-purge can see a master-less frame; ACT kills it the same
        // engine pass. Ticks alone never earn display.
        var frame = new EscalationTracker().Tick(
            R(Reading("boss", 18, master: false), Reading("boss", 24, master: false, startOffset: 6)));

        Assert.Empty(frame.ListRows);
        Assert.Empty(frame.CenterElements);
    }

    [Fact]
    public void Overdue_master_stays_LATE_when_only_ticks_are_newer()
    {
        // A recast landing inside a still-running tick stream stays non-master: LATE
        // correctly holds until the overdue master purges (SPEC §The Overdue visual).
        var frame = new EscalationTracker().Tick(
            R(Reading("boss", -4, startOffset: 0), Reading("boss", 45, master: false, startOffset: 30)));

        Assert.Empty(frame.ListRows);
        var late = Assert.Single(frame.CenterElements);
        Assert.Equal(CenterElementKind.Late, late.Kind);
        Assert.Equal(4, late.LateSeconds);
    }

    [Fact]
    public void Equal_StartTime_masters_tie_break_to_the_larger_TimeLeft()
    {
        // Unreachable via ACT's 2s trigger dedup; defined for a total ordering.
        var frame = new EscalationTracker().Tick(
            R(Reading("boss", 5, startOffset: 0), Reading("boss", 30, startOffset: 0)));

        Assert.Equal(30, Assert.Single(frame.ListRows).TimeLeft);
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

        Assert.Equal(ColorPolicy.DefaultPaletteArgb[0], first.ListRows.Single(r => r.Name == "Blanket").FillArgb);
        Assert.Equal(ColorPolicy.DefaultPaletteArgb[1], first.ListRows.Single(r => r.Name == "Shield").FillArgb);
        // stable across ticks regardless of this tick's order
        Assert.Equal(ColorPolicy.DefaultPaletteArgb[0], again.ListRows.Single(r => r.Name == "Blanket").FillArgb);
        Assert.Equal(ColorPolicy.DefaultPaletteArgb[1], again.ListRows.Single(r => r.Name == "Shield").FillArgb);
    }

    [Fact]
    public void ActColor_mode_keeps_the_timers_own_color_softened()
    {
        var tracker = new EscalationTracker(new PanelSettings { ColorSource = ColorSource.ActColor });
        var frame = tracker.Tick(R(Reading("t", 25)));

        Assert.Equal(ColorPolicy.Soften(-16776961), frame.ListRows[0].FillArgb);
    }

    [Fact]
    public void HighlightInPlace_keeps_imminents_in_the_list_and_center_empty()
    {
        var tracker = new EscalationTracker(new PanelSettings { EscalationStyle = EscalationStyle.HighlightInPlace });
        var frame = tracker.Tick(R(Reading("boss", 5), Reading("calm", 25)));

        Assert.Empty(frame.CenterElements);
        Assert.Equal(2, frame.ListRows.Count);
        Assert.Equal(TimerUrgency.Imminent, frame.ListRows.Single(r => r.Name == "boss").Urgency);
    }

    [Fact]
    public void HighlightInPlace_renders_linger_overdue_as_LATE_rows()
    {
        var tracker = new EscalationTracker(new PanelSettings { EscalationStyle = EscalationStyle.HighlightInPlace });
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
        var tracker = new EscalationTracker(new PanelSettings { EscalationStyle = EscalationStyle.HighlightInPlace });
        var frame = tracker.Tick(R(Reading("boss", -1, removeValue: 0)));

        Assert.Empty(frame.ListRows);
        Assert.Empty(frame.CenterElements);
    }

    [Fact]
    public void Color_resolution_does_not_mutate_the_input_readings()
    {
        var reading = Reading("boss", 25);              // FillArgb = -16776961 (ACT blue)
        new EscalationTracker().Tick(R(reading));

        Assert.Equal(-16776961, reading.FillArgb);      // original untouched
    }

    [Fact]
    public void Two_trackers_sharing_readings_each_resolve_from_the_ACT_original()
    {
        var palette = new PaletteAssigner();
        var trackerA = new EscalationTracker(new PanelSettings(), palette);
        var trackerB = new EscalationTracker(new PanelSettings { ColorSource = ColorSource.ActColor }, palette);
        var readings = R(Reading("boss", 25));

        trackerA.Tick(readings);                        // Palette mode resolves first
        var frameB = trackerB.Tick(readings);

        // B must soften ACT's original blue — NOT A's already-assigned palette color.
        Assert.Equal(ColorPolicy.Soften(-16776961), frameB.ListRows[0].FillArgb);
    }

    [Fact]
    public void Tick_resolves_against_a_custom_palette()
    {
        var frame = new EscalationTracker().Tick(R(Reading("boss", 25)), new[] { 424242 });

        Assert.Equal(424242, frame.ListRows[0].FillArgb);
    }

    [Fact]
    public void Shared_assigner_gives_one_name_one_slot_across_trackers()
    {
        var palette = new PaletteAssigner();
        var trackerA = new EscalationTracker(new PanelSettings(), palette);
        var trackerB = new EscalationTracker(new PanelSettings(), palette);

        trackerA.Tick(R(Reading("First", 25)));
        var frameB = trackerB.Tick(R(Reading("Second", 25), Reading("First", 20)));

        Assert.Equal(ColorPolicy.DefaultPaletteArgb[0], frameB.ListRows.Single(r => r.Name == "First").FillArgb);
        Assert.Equal(ColorPolicy.DefaultPaletteArgb[1], frameB.ListRows.Single(r => r.Name == "Second").FillArgb);
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
