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

    [Fact]
    public void PreciseOf_is_the_raw_wall_clock_value()
    {
        // The wall clock owns the visuals. Clamping to ACT's integer TimeLeft was tried
        // and produced a sawtooth: ACT's clock only advances when log lines arrive, so
        // idle it lags and kept yanking the smooth animation back up to a stale second.
        Assert.Equal(6.3, TimerMath.PreciseOf(Reading(7, rawPrecise: 6.3)), 3);
    }
}
