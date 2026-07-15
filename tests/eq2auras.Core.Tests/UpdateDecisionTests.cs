using Eq2Auras.Core.SelfUpdate;
using Xunit;

public class UpdateDecisionTests
{
    [Theory]
    [InlineData(true, "dev-latest")]
    [InlineData(false, "stable")]
    public void TagForChannel_maps_beta_flag_to_tag(bool beta, string expectedTag)
    {
        Assert.Equal(expectedTag, UpdateDecision.TagForChannel(beta));
    }

    [Theory]
    [InlineData("0.1.96", "0.1.97", true)]    // dev advanced
    [InlineData("0.1.200", "0.1.150", true)]  // beta -> stable, numerically BACKWARD, must install
    [InlineData("0.1.150", "0.1.150", false)] // same identity -> already up to date
    public void UpdateAvailable_is_identity_inequality_not_ordering(
        string installed, string release, bool expected)
    {
        Assert.Equal(expected, UpdateDecision.UpdateAvailable(installed, release));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void UpdateAvailable_is_false_when_release_version_is_missing(string? release)
    {
        Assert.False(UpdateDecision.UpdateAvailable("0.1.96", release));
    }
}
