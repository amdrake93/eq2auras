namespace Eq2Auras.Core.SelfUpdate
{
    /// Pure update-targeting rules (SPEC §Release channels & public distribution).
    /// Kept in Core so the identity-not-ordering contract is unit-tested on the Mac.
    public static class UpdateDecision
    {
        public const string BetaTag = "dev-latest";
        public const string StableTag = "stable";

        public static string TagForChannel(bool betaChannel)
            => betaChannel ? BetaTag : StableTag;

        /// Install iff the channel release's identity differs from what is installed.
        /// Equality only — NEVER numeric ordering: opting out of beta routinely moves
        /// numerically backward (0.1.200 -> 0.1.150) and must still install.
        public static bool UpdateAvailable(string installedVersion, string releaseVersion)
            => !string.IsNullOrEmpty(releaseVersion) && releaseVersion != installedVersion;
    }
}
