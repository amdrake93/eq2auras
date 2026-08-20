using System.Collections.Generic;
using System.Linq;

namespace Eq2Auras.Core.Timers
{
    /// The bounded v1 buff library. The compiled sibling of MetricRegistry: adding a buff =
    /// appending a BuffDef. Category is the reserved routing/management/segregation namespace
    /// for ALL eq2auras-managed timers (SPEC §Buff tracking). BASE durations = census
    /// (spell + AA collections, Grandmaster max ÷ 10s); †tier-variable → the per-char override
    /// corrects. Regex is built by BuffDef from the shared template — entries carry only name/duration.
    public static class BuffCatalog
    {
        public const string Category = "eq2auras Buffs";

        public static readonly IReadOnlyList<BuffDef> All = new List<BuffDef>
        {
            // --- Single-target (12) — capture the target from the payload ---
            new BuffDef("bolster",             "Bolster",             36,  isTargeted: true),
            new BuffDef("jesters-cap",         "Jester's Cap",        30,  isTargeted: true),
            new BuffDef("ritual-of-alacrity",  "Ritual of Alacrity",  30,  isTargeted: true),
            new BuffDef("holy-shield",         "Holy Shield",         30,  isTargeted: true),
            new BuffDef("animal-form",         "Animal Form",         60,  isTargeted: true),
            new BuffDef("got-your-back",       "Got Your Back",       15,  isTargeted: true),
            new BuffDef("tsunami",             "Tsunami",             21,  isTargeted: true),   // †20.6
            new BuffDef("divine-aura",         "Divine Aura",         10,  isTargeted: true),
            new BuffDef("adrenaline",          "Adrenaline",          33,  isTargeted: true),   // †33.0
            new BuffDef("unyielding-will",     "Unyielding Will",     180, isTargeted: true),
            new BuffDef("brutal-inspiration",  "Brutal Inspiration",  30,  isTargeted: true),
            new BuffDef("gravitas",            "Gravitas",            30,  isTargeted: true),

            // --- Group/raid-wide (10) — capture the caster (speaker), any channel ---
            new BuffDef("tortoise-shell",            "Tortoise Shell",            30, isTargeted: false),
            new BuffDef("bladedance",                "Bladedance",                30, isTargeted: false),
            new BuffDef("cacophony-of-blades",       "Cacophony of Blades",       12, isTargeted: false),
            new BuffDef("perfection-of-the-maestro", "Perfection of the Maestro", 20, isTargeted: false),
            new BuffDef("frigid-gift",               "Frigid Gift",               24, isTargeted: false),
            new BuffDef("curse-of-darkness",         "Curse of Darkness",         12, isTargeted: false),
            new BuffDef("peace-of-mind",             "Peace of Mind",             20, isTargeted: false),
            new BuffDef("death-march",               "Death March",               60, isTargeted: false),
            new BuffDef("sanctuary",                 "Sanctuary",                 31, isTargeted: false), // †30.9
            new BuffDef("advance-warning",           "Advance Warning",           13, isTargeted: false),
        };

        public static BuffDef Find(string id) => All.FirstOrDefault(b => b.Id == id);

        /// By the display name (= the timer's `Name`, so the row renderer can look a buff up
        /// from a reading and choose its format, §Display). Ordinal-ignore-case; names are unique.
        public static BuffDef FindByName(string displayName)
            => All.FirstOrDefault(b => string.Equals(b.DisplayName, displayName, System.StringComparison.OrdinalIgnoreCase));
    }
}
