using System.Collections.Generic;

namespace Eq2Auras.Core.Meter
{
    /// The locked 12-subclass palette (docs/plans/2026-07-27-class-colors-palette.md), the row-fill
    /// successor to MeterFamilyColors. Unknown/unclassed → neutral grey (SPEC Part III §Class colors).
    public static class SubclassColors
    {
        public const int Grey = unchecked((int)0xFF8B93A3);

        private static readonly Dictionary<Subclass, int> Map = new Dictionary<Subclass, int>
        {
            { Subclass.Crusader, unchecked((int)0xFFC9A227) },
            { Subclass.Brawler, unchecked((int)0xFF00FF98) },
            { Subclass.Warrior, unchecked((int)0xFFC69B6D) },
            { Subclass.Cleric, unchecked((int)0xFFFFFFFF) },
            { Subclass.Druid, unchecked((int)0xFFFF7C0A) },
            { Subclass.Shaman, unchecked((int)0xFF0070DD) },
            { Subclass.Rogue, unchecked((int)0xFFFFF468) },
            { Subclass.Bard, unchecked((int)0xFF6C3FB5) },
            { Subclass.Predator, unchecked((int)0xFFAAD372) },
            { Subclass.Sorcerer, unchecked((int)0xFF3FC7EB) },
            { Subclass.Summoner, unchecked((int)0xFF8788EE) },
            { Subclass.Enchanter, unchecked((int)0xFF33937F) },
        };

        public static int ArgbFor(Subclass subclass)
            => Map.TryGetValue(subclass, out var argb) ? argb : Grey;
    }
}
