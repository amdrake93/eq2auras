using System.Collections.Generic;
using System.Linq;

namespace Eq2Auras.Core.Timers
{
    /// Pure "what should be live" policy: the enabled catalog entries (catalog ∩ enabled). The
    /// injector reconciles ACT's live state against this and applies each buff's effective duration
    /// at build time; the stale-def sweep and zone re-inject are the injector's (SPEC §Buff tracking).
    public static class BuffSync
    {
        public static IReadOnlyList<BuffDef> Desired(IEnumerable<string> enabledIds)
            => enabledIds == null
                ? new List<BuffDef>()
                : enabledIds.Select(BuffCatalog.Find).Where(b => b != null).ToList();
    }
}
