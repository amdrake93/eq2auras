using System.Collections.Generic;
using System.Linq;
using Eq2Auras.Core.Timers;
using Xunit;

public class EscalationTrackerTests
{
    private const long T0 = 1_000_000;

    private static TimerReading Reading(string name, int timeLeft,
        int warning = 10, int total = 30, string combatant = "none")
        => new TimerReading
        {
            Name = name, Combatant = combatant, TimeLeft = timeLeft,
            WarningValue = warning, TotalSeconds = total,
            RawPreciseTimeLeft = timeLeft, FillArgb = -16776961
        };

    private static List<TimerReading> R(params TimerReading[] readings) => readings.ToList();

    [Fact]
    public void Calm_timers_stay_in_the_list_and_center_is_empty()
    {
        var frame = new EscalationTracker().Tick(R(Reading("a", 25), Reading("b", 18)), T0);

        Assert.Equal(2, frame.ListRows.Count);
        Assert.Empty(frame.CenterElements);
    }

    [Fact]
    public void Imminent_timer_leaves_the_list_and_becomes_a_pie()
    {
        var frame = new EscalationTracker().Tick(R(Reading("boss", 8), Reading("calm", 25)), T0);

        Assert.Single(frame.ListRows);
        Assert.Equal("calm", frame.ListRows[0].Name);
        var pie = Assert.Single(frame.CenterElements);
        Assert.Equal(CenterElementKind.Pie, pie.Kind);
        Assert.Equal("boss", pie.Name);
        Assert.Equal(8, pie.SecondsLeft);
        Assert.Equal(0.8, pie.PieFraction, 2);   // 8s left of a 10s warning window
    }

    [Fact]
    public void Pie_is_full_at_the_moment_of_escalation()
    {
        var frame = new EscalationTracker().Tick(R(Reading("boss", 10)), T0);

        Assert.Equal(1.0, frame.CenterElements[0].PieFraction, 2);
    }

    [Fact]
    public void Overflow_imminents_wait_in_the_list_as_highlighted_rows()
    {
        var frame = new EscalationTracker().Tick(
            R(Reading("a", 2), Reading("b", 4), Reading("c", 6), Reading("d", 8), Reading("calm", 25)), T0);

        Assert.Equal(3, frame.CenterElements.Count);   // cap
        Assert.Equal(new[] { "a", "b", "c" }, frame.CenterElements.Select(e => e.Name).ToArray());
        Assert.Equal(2, frame.ListRows.Count);          // overflow "d" + "calm"
        Assert.Equal("d", frame.ListRows[0].Name);
        Assert.Equal(TimerUrgency.Imminent, frame.ListRows[0].Urgency);
    }

    [Fact]
    public void Escalated_key_that_vanishes_becomes_a_LATE_on_the_floor()
    {
        var tracker = new EscalationTracker();
        tracker.Tick(R(Reading("boss", 1)), T0);                       // escalated
        var frame = tracker.Tick(R(), T0 + 500);                        // gone (ACT dropped it)

        var late = Assert.Single(frame.CenterElements);
        Assert.Equal(CenterElementKind.Late, late.Kind);
        Assert.Equal("boss", late.Name);
        Assert.Equal(0, late.LateSeconds);

        frame = tracker.Tick(R(), T0 + 1600);                           // still on the floor
        Assert.Equal(1, Assert.Single(frame.CenterElements).LateSeconds);

        frame = tracker.Tick(R(), T0 + 2600);                           // floor expired (2000ms)
        Assert.Empty(frame.CenterElements);
    }

    [Fact]
    public void Readings_at_or_below_zero_count_as_not_live_and_trigger_LATE()
    {
        var tracker = new EscalationTracker();
        tracker.Tick(R(Reading("boss", 1)), T0);
        var frame = tracker.Tick(R(Reading("boss", -1)), T0 + 400);     // frame lingers at -1 (measured)

        Assert.Empty(frame.ListRows);
        Assert.Equal(CenterElementKind.Late, Assert.Single(frame.CenterElements).Kind);
    }

    [Fact]
    public void Reset_supersedes_the_floor_instantly()
    {
        var tracker = new EscalationTracker();
        tracker.Tick(R(Reading("boss", 1)), T0);
        tracker.Tick(R(), T0 + 500);                                    // LATE created
        var frame = tracker.Tick(R(Reading("boss", 30)), T0 + 900);     // ability fired -> reset

        Assert.Empty(frame.CenterElements);                             // LATE cancelled
        Assert.Equal("boss", Assert.Single(frame.ListRows).Name);       // calm row back
    }

    [Fact]
    public void A_surviving_second_instance_suppresses_LATE()
    {
        var tracker = new EscalationTracker();
        tracker.Tick(R(Reading("boss", 1), Reading("boss", 12)), T0);
        var frame = tracker.Tick(R(Reading("boss", 12)), T0 + 500);     // first instance expired

        Assert.Empty(frame.CenterElements.Where(e => e.Kind == CenterElementKind.Late));
        Assert.Single(frame.ListRows);
    }

    [Fact]
    public void Calm_key_vanishing_produces_nothing()
    {
        var tracker = new EscalationTracker();
        tracker.Tick(R(Reading("boss", 25)), T0);
        var frame = tracker.Tick(R(), T0 + 500);

        Assert.Empty(frame.CenterElements);
        Assert.Empty(frame.ListRows);
    }

    [Fact]
    public void LATEs_rank_ahead_of_pies_and_consume_center_slots()
    {
        var tracker = new EscalationTracker();
        tracker.Tick(R(Reading("dead1", 1), Reading("dead2", 1, combatant: "other")), T0);
        var frame = tracker.Tick(
            R(Reading("p1", 2), Reading("p2", 4), Reading("p3", 6)), T0 + 500);

        Assert.Equal(3, frame.CenterElements.Count);
        Assert.Equal(2, frame.CenterElements.Count(e => e.Kind == CenterElementKind.Late));
        Assert.Equal("p1", frame.CenterElements.Single(e => e.Kind == CenterElementKind.Pie).Name);
        Assert.Equal(2, frame.ListRows.Count);          // p2, p3 overflow back to the list
    }
}
