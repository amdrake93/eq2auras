using Eq2Auras.Core.Meter;
using Xunit;

public class NumberFormatTests
{
    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(42, "0:42")]
    [InlineData(80, "1:20")]
    [InlineData(113, "1:53")]
    [InlineData(3599, "59:59")]
    public void Mmss_formats_seconds_as_minutes_and_padded_seconds(double s, string expected)
        => Assert.Equal(expected, NumberFormat.Mmss(s));

    [Theory]
    [InlineData(0, "0")]
    [InlineData(-500, "-500")]
    [InlineData(1000, "+1K")]
    [InlineData(-4200, "-4.2K")]
    [InlineData(-9800, "-9.8K")]
    public void SignedAbbreviate_prefixes_sign_and_abbreviates_magnitude(double v, string expected)
        => Assert.Equal(expected, NumberFormat.SignedAbbreviate(v));
}
