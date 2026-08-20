using System.Text.RegularExpressions;

namespace Eq2Auras.Core.Timers
{
    /// One catalog entry: the bounded library's atom. `Pattern` is handed verbatim to ACT's
    /// CustomTrigger (ACT does the matching); `TryMatch` is the Mac-testable validator that
    /// proves the shipped regex captures the right NAME (SPEC §Buff tracking). `IsTargeted`
    /// distinguishes single-target buffs (macro carries %T; regex captures the target from the
    /// payload) from group-wide ones (no %T; regex captures the CASTER from the chat wrapper).
    public sealed class BuffDef
    {
        public string Id { get; }
        public string DisplayName { get; }
        public int DurationSeconds { get; }   // catalog BASE duration (census); a raider may override (BuffPref)
        public bool IsTargeted { get; }
        public string Pattern { get; }        // built from the shared template — handed verbatim to ACT

        private Regex _rx;   // lazily built by TryMatch (test-only); the runtime uses Pattern

        public BuffDef(string id, string displayName, int durationSeconds, bool isTargeted)
        {
            Id = id;
            DisplayName = displayName;
            DurationSeconds = durationSeconds;
            IsTargeted = isTargeted;
            Pattern = BuildPattern(displayName, isTargeted);
        }

        // One shape per kind (adapted from Alex's field-proven announce trigger). Case-insensitive
        // (inline `(?i)`), channel-agnostic, position-agnostic — ACT only needs the payload to appear
        // ANYWHERE in the line, in any chat channel. BOTH lead the scan with the literal `eq2auras`
        // so non-matching lines fast-reject (the N-trigger runtime fix, SPEC-plan §REGEX RUNTIME):
        //   Single-target: `eq2auras <buff>` then capture the target token from the payload.
        //   Group-wide:    `eq2auras <buff>` with the SPEAKER captured via a variable-length LOOKBEHIND
        //                  over the `<name>\/a say/tells …, "` chat wrapper (\/a = EQ2's speaker-link
        //                  markup, optional to tolerate a markup-stripped line).
        private static string BuildPattern(string buff, bool isTargeted)
        {
            var lit = Regex.Escape(buff);
            return isTargeted
                ? $"(?i)eq2auras {lit} (?<attacker>[a-zA-Z]+)"
                : $"(?i)(?<=(?<attacker>[a-zA-Z]+)(?:\\\\/a)? (?:say|tell)s? [a-zA-Z ]+, \"[^\"]*)eq2auras {lit}";
        }

        /// True if the line matches; `name` is the captured `attacker` group (the target for a
        /// targeted buff, the caster for a group-wide one), or null when absent/empty.
        public bool TryMatch(string line, out string name)
        {
            name = null;
            if (line == null) return false;
            // Lazy: the runtime Plugin only hands `Pattern` to ACT (ACT does the matching); this
            // validator is test-only, so we don't compile 22 regexes on ACT's UI thread at init.
            var m = (_rx ?? (_rx = new Regex(Pattern))).Match(line);
            if (!m.Success) return false;
            var g = m.Groups["attacker"];
            if (g.Success && g.Value.Length > 0) name = g.Value;
            return true;
        }
    }
}
