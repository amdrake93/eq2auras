using Eq2Auras.Core.Meter;
using Xunit;

public class BreakdownDirectionTests
{
    [Theory]
    [InlineData(MetricBreakdownSource.IncomingDamage, true)]   // who hit me — by attacker
    [InlineData(MetricBreakdownSource.IncomingHealing, true)]  // who healed me — by attacker
    [InlineData(MetricBreakdownSource.OutgoingDamage, false)]  // what I hit — by victim
    [InlineData(MetricBreakdownSource.OutgoingHealing, false)] // whom I healed — by victim
    [InlineData(MetricBreakdownSource.PowerReplenish, false)]  // whom I fed power — by victim
    [InlineData(MetricBreakdownSource.Cures, false)]           // whom I cured — by victim
    [InlineData(MetricBreakdownSource.None, false)]            // never hovered — safe default
    [InlineData(MetricBreakdownSource.Deaths, false)]          // event metric, never hovered — safe default
    public void IsIncoming_is_true_only_for_the_two_incoming_buckets(MetricBreakdownSource source, bool expected)
    {
        Assert.Equal(expected, BreakdownDirection.IsIncoming(source));
    }
}
