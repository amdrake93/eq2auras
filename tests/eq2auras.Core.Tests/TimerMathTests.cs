using Eq2Auras.Core.Timers;
using Xunit;

public class TimerMathTests
{
    private static TimerReading Reading(int timeLeft, int warning = 10, int total = 30, double rawPrecise = double.NaN)
        => new TimerReading
        {
            Name = "t", Combatant = "none", TimeLeft = timeLeft,
            WarningValue = warning, TotalSeconds = total,
            RawPreciseTimeLeft = double.IsNaN(rawPrecise) ? timeLeft : rawPrecise
        };

    [Theory]
    [InlineData(10, 30, 10)]   // usable warning -> itself
    [InlineData(0, 40, 10)]    // unusable -> total/4
    [InlineData(30, 30, 7)]    // warning >= total -> total/4
    [InlineData(0, 0, 10)]     // total unusable too -> absolute 10
    public void EffectiveWarning_matches_spec_fallbacks(int warning, int total, int expected)
    {
        Assert.Equal(expected, TimerMath.EffectiveWarning(Reading(5, warning, total)));
    }

    [Theory]
    [InlineData(7, 7.4, 7.4)]    // in-window raw passes through
    [InlineData(7, 12.0, 7.999)] // raw drifted high -> clamped just under next second
    [InlineData(7, 3.0, 7.0)]    // raw drifted low -> clamped to the displayed second
    public void PreciseOf_clamps_raw_into_the_displayed_second(int timeLeft, double raw, double expected)
    {
        Assert.Equal(expected, TimerMath.PreciseOf(Reading(timeLeft, rawPrecise: raw)), 3);
    }
}
