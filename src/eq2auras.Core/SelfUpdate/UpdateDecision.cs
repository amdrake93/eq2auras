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
        ///
        /// Both sides are normalized to their build-metadata-free core first: the .NET SDK
        /// stamps the assembly's InformationalVersion as "0.1.98+<sha>" while the release
        /// name is bare "0.1.98". Per semver, the "+build" segment is excluded from identity,
        /// so the "0.1.<run>" core (unique per CI run) is the true identity token.
        public static bool UpdateAvailable(string installedVersion, string releaseVersion)
            => !string.IsNullOrEmpty(releaseVersion)
               && CoreVersion(releaseVersion) != CoreVersion(installedVersion);

        private static string CoreVersion(string version)
        {
            if (string.IsNullOrEmpty(version)) return version;
            var plus = version.IndexOf('+');
            return plus < 0 ? version : version.Substring(0, plus);
        }
    }
}
