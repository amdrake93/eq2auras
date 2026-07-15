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

    [Theory]
    // The shipped DLL's InformationalVersion carries the SDK's +<sha> build metadata
    // (e.g. 0.1.98+362dc28…) while the release name is bare 0.1.98 — same build.
    [InlineData("0.1.98+362dc2825a34bf6e5d88859aab0c13a536eeb0b3", "0.1.98", false)]
    // A genuinely newer bare release against a suffixed installed value still updates.
    [InlineData("0.1.98+362dc2825a34bf6e5d88859aab0c13a536eeb0b3", "0.1.103", true)]
    // Suffix on the release side too — normalized both sides, still same identity.
    [InlineData("0.1.98+abc", "0.1.98+def", false)]
    public void UpdateAvailable_ignores_build_metadata_after_plus(
        string installed, string release, bool expected)
    {
        Assert.Equal(expected, UpdateDecision.UpdateAvailable(installed, release));
    }
}
