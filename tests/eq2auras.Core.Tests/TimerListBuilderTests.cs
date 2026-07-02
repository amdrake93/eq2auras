using System.Collections.Generic;
using System.Linq;
using Eq2Auras.Core.Timers;
using Xunit;

public class TimerListBuilderTests
{
    private static TimerReading Reading(string name, int timeLeft,
        int warning = 10, int total = 30, string combatant = "none", int argb = -16776961)
        => new TimerReading
        {
            Name = name, Combatant = combatant, TimeLeft = timeLeft,
            WarningValue = warning, TotalSeconds = total, FillArgb = argb
        };

    [Fact]
    public void Sorts_soonest_first_with_name_tiebreak()
    {
        var rows = TimerListBuilder.Build(new List<TimerReading>
        {
            Reading("Slow", 25), Reading("Fast", 3), Reading("Mid", 12), Reading("Also3", 3)
        });

        Assert.Equal(new[] { "Also3", "Fast", "Mid", "Slow" }, rows.Select(r => r.Name).ToArray());
    }

    [Theory]
    [InlineData(11, TimerUrgency.Calm)]
    [InlineData(10, TimerUrgency.Imminent)]   // at WarningValue -> warning state
    [InlineData(1, TimerUrgency.Imminent)]
    [InlineData(0, TimerUrgency.Overdue)]     // measured: expire fires at 0
    [InlineData(-3, TimerUrgency.Overdue)]    // measured: TimeLeft goes negative
    public void Urgency_pivots_on_the_timers_own_WarningValue(int timeLeft, TimerUrgency expected)
    {
        var rows = TimerListBuilder.Build(new List<TimerReading> { Reading("t", timeLeft, warning: 10, total: 30) });

        Assert.Equal(expected, rows[0].Urgency);
    }

    [Theory]
    [InlineData(0, 40, 10, TimerUrgency.Imminent)]   // warning=0 -> fallback total/4 = 10
    [InlineData(0, 40, 11, TimerUrgency.Calm)]
    [InlineData(30, 30, 7, TimerUrgency.Imminent)]   // warning >= total -> same fallback (30/4=7)
    [InlineData(30, 30, 8, TimerUrgency.Calm)]
    [InlineData(0, 0, 10, TimerUrgency.Imminent)]    // total also unusable -> absolute 10s
    [InlineData(0, 0, 11, TimerUrgency.Calm)]
    public void Warning_fallbacks_fraction_of_total_then_absolute(
        int warning, int total, int timeLeft, TimerUrgency expected)
    {
        var rows = TimerListBuilder.Build(new List<TimerReading> { Reading("t", timeLeft, warning, total) });

        Assert.Equal(expected, rows[0].Urgency);
    }

    [Theory]
    [InlineData(15, 30, 0.5)]
    [InlineData(-3, 30, 0.0)]   // overdue clamps empty
    [InlineData(45, 30, 1.0)]   // >total clamps full
    [InlineData(10, 0, 0.0)]    // unusable total -> empty fill, no divide-by-zero
    public void FillFraction_is_timeLeft_over_total_clamped(int timeLeft, int total, double expected)
    {
        var rows = TimerListBuilder.Build(new List<TimerReading> { Reading("t", timeLeft, total: total) });

        Assert.Equal(expected, rows[0].FillFraction, 3);
    }

    [Fact]
    public void Multiple_instances_of_the_same_timer_each_get_a_row()
    {
        var rows = TimerListBuilder.Build(new List<TimerReading>
        {
            Reading("Holy Shield", 12), Reading("Holy Shield", 27)
        });

        Assert.Equal(2, rows.Count);
        Assert.Equal(12, rows[0].TimeLeft);
        Assert.Equal(27, rows[1].TimeLeft);
    }
}
