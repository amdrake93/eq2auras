using System.Collections.Generic;

namespace Eq2Auras.Core.Meter
{
    /// EQ2's class tree (SPEC Part III §Class colors): 4 archetypes → 12 subclasses → 24 finals.
    /// Color keys at the subclass; the final rides along as enrichment. Unknown = 0 (DCJS rule).
    public enum Subclass
    {
        Unknown = 0,
        Crusader, Brawler, Warrior,      // Fighter
        Cleric, Druid, Shaman,           // Priest
        Rogue, Bard, Predator,           // Scout
        Sorcerer, Summoner, Enchanter,   // Mage
    }

    public enum FinalClass
    {
        Unknown = 0,
        Paladin, Shadowknight,           // Crusader
        Monk, Bruiser,                   // Brawler
        Guardian, Berserker,             // Warrior
        Templar, Inquisitor,             // Cleric
        Warden, Fury,                    // Druid
        Mystic, Defiler,                 // Shaman
        Swashbuckler, Brigand,           // Rogue
        Troubador, Dirge,                // Bard
        Ranger, Assassin,                // Predator
        Wizard, Warlock,                 // Sorcerer
        Conjuror, Necromancer,           // Summoner
        Illusionist, Coercer,            // Enchanter
    }

    public static class ClassTree
    {
        private static readonly Dictionary<FinalClass, Subclass> Map = new Dictionary<FinalClass, Subclass>
        {
            { FinalClass.Paladin, Subclass.Crusader }, { FinalClass.Shadowknight, Subclass.Crusader },
            { FinalClass.Monk, Subclass.Brawler }, { FinalClass.Bruiser, Subclass.Brawler },
            { FinalClass.Guardian, Subclass.Warrior }, { FinalClass.Berserker, Subclass.Warrior },
            { FinalClass.Templar, Subclass.Cleric }, { FinalClass.Inquisitor, Subclass.Cleric },
            { FinalClass.Warden, Subclass.Druid }, { FinalClass.Fury, Subclass.Druid },
            { FinalClass.Mystic, Subclass.Shaman }, { FinalClass.Defiler, Subclass.Shaman },
            { FinalClass.Swashbuckler, Subclass.Rogue }, { FinalClass.Brigand, Subclass.Rogue },
            { FinalClass.Troubador, Subclass.Bard }, { FinalClass.Dirge, Subclass.Bard },
            { FinalClass.Ranger, Subclass.Predator }, { FinalClass.Assassin, Subclass.Predator },
            { FinalClass.Wizard, Subclass.Sorcerer }, { FinalClass.Warlock, Subclass.Sorcerer },
            { FinalClass.Conjuror, Subclass.Summoner }, { FinalClass.Necromancer, Subclass.Summoner },
            { FinalClass.Illusionist, Subclass.Enchanter }, { FinalClass.Coercer, Subclass.Enchanter },
        };

        public static Subclass SubclassOf(FinalClass final)
            => Map.TryGetValue(final, out var s) ? s : Subclass.Unknown;
    }
}
