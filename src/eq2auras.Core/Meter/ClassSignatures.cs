using System;
using System.Collections.Generic;

namespace Eq2Auras.Core.Meter
{
    /// The compiled ability-name → class catalog (SPEC Part III §Class colors), authored subclass-first
    /// from spike-data/2026-07-27/signatures.md with its census cross-reference applied as the final
    /// authority: STRONG/WEAK/SPEC under their FinalClass, SHARED (incl. census-MOVED names) under their
    /// Subclass, CUT excluded, and the log-thin classes (Ranger/Warlock/Guardian/Berserker) firmed up
    /// with single-class-by-union names recovered from census_index.tsv. Inverted at static init to a
    /// case-insensitive name→{subclass, final?} lookup, guarded so no name resolves to two subclasses.
    /// Pets and parenthetical annotations are not signatures — the player's own outgoing casts are read.
    public static class ClassSignatures
    {
        private static readonly Dictionary<FinalClass, string[]> ByFinal = new Dictionary<FinalClass, string[]>
        {
            { FinalClass.Swashbuckler, new[] {
                "Evade Blame", "Flurry of Blades", "Snap of the Wrist", "Kidney Stab", "Flash of Steel",
                "Hamstring", "Lucky Gambit", "Dashing Swathe", "Razor Edge", "Viscerate", "Lung Puncture",
                "Storm of Steel", "Flamboyant Strike", "Daring Attack", "Arctic Blast" } },
            { FinalClass.Brigand, new[] {   // Backstab, Gouge census-MOVED to Rogue SHARED
                "Shank", "Baffle", "Puncture", "Battery and Assault", "Bum Rush", "Barroom Negotiation",
                "Desperate Thrust", "Stunning Blow", "Black Jack", "Dispatch", "Murderous Rake",
                "Double Blast", "Debilitate" } },
            { FinalClass.Illusionist, new[] {   // Overwhelming Silence census-MOVED to Enchanter SHARED
                "Chromatic Shower", "Prismatic Shock", "Chromatic Storm", "Theorems", "Phantasmal Shock",
                "Brainburst", "Ultraviolet Beam", "Nightmare", "Brain Clot", "Confusion", "Headache",
                "Color Shower", "Migraine", "Speechless", "Aneurysm", "Paranoia" } },
            { FinalClass.Coercer, new[] {   // Ego Shock census-MOVED to Enchanter SHARED
                "Lash", "Convulsions", "Silence", "Despotic Mind", "Simple Minds", "Brainshock",
                "Hemorrhage", "Asylum", "Shock Wave", "Medusa Gaze", "Forceful Headache" } },
            { FinalClass.Conjuror, new[] {
                "Fiery Annihilation", "Force of the Elements", "Aery Whip", "Seed of Fire", "Flameshield",
                "Shattered Earth", "Crystal Blast", "Winds of Velious", "Thunderous Attack", "Furystorm",
                "Earthquake", "Wisp Blade", "Storm Surge" } },
            { FinalClass.Necromancer, new[] {
                "Lich's Siphoning", "Soulrot", "Grim Embrace", "Bloodcoil", "Grim Wave", "Consume",
                "Pandemic", "Grim Devastation", "Lifetap", "Grim Bolt", "Grim Lifetap", "Lifeburn" } },
            { FinalClass.Troubador, new[] {   // Singing Shot census-MOVED to Bard SHARED
                "Chaos Anthem", "Ceremonial Blade", "Dancing Blade", "Thunderous Overture", "Perfect Shrill",
                "Sandra's Deafening Strike", "Painful Lamentations", "Tap Essence", "Night Strike",
                "Breathtaking Bellow", "Vexing Verses", "Song Barrier", "Arcane Symphony", "Elemental Concerto" } },
            { FinalClass.Dirge, new[] {
                "Darksong Blade", "Scream of Death", "Wail of the Banshee", "Thuri's Doleful Thrust",
                "Daro's Dull Blade", "Tarven's Crippling Crescendo", "Luda's Nefarious Wail",
                "Lanet's Excruciating Scream", "Howl of Death", "Jarol's Sorrowful Requiem", "Misfortune's Kiss",
                "Banshee's Scream", "Jael's Dreadful Deprivation", "Death Barrier" } },
            { FinalClass.Fury, new[] {   // Regrowth census-MOVED to Druid SHARED
                "Autumn's Kiss", "Porcupine", "Porcupine Quills", "Untamed Regeneration", "Savage Feast",
                "Ring of Fire", "Thunderbolt", "Call of Storms", "Starnova", "Nature's Elixir",
                "Back into the Fray" } },
            { FinalClass.Warden, new[] {
                "Winds of Healing", "Healstorm", "Photosynthesis", "Nature's Embrace", "Spores", "Frostbite",
                "Nature's Blessing", "Reincarnate", "Verdant Trinity" } },
            { FinalClass.Templar, new[] {
                "Shield of Faith", "Divine Smash", "Divine Prayer", "Involuntary Cure", "Supplicant's Prayer",
                "Reverence", "Combat Glory", "Benefaction", "Word of Redemption", "Blaze of Faith", "Holy Touch" } },
            { FinalClass.Inquisitor, new[] {
                "Vengeance", "Torment", "Alleviation", "Convert Ally", "Repenting Strike", "Atoning Faith",
                "Litany Circle", "Heretic's Doom", "Hammer Divine Smite", "Heretical Strike", "Vengeful Faith",
                "Litany", "Ministration", "Writhing Strike", "Strike of Corruption", "Strike of Flames",
                "Invocation Strike" } },
            { FinalClass.Mystic, new[] {
                "Lunar Attendant's Oracle's Blessing", "Ancestral Ward", "Umbral Warding", "Runic Armor",
                "Torpor", "Prophetic Ward", "Oberon" } },
            { FinalClass.Defiler, new[] {
                "Spiritual Circle", "Shroud of Armor", "Carrion Warding", "Ancient Shroud" } },
            { FinalClass.Shadowknight, new[] {
                "Reaver's Mania", "Pestilence", "Grim Strike", "Mana Sieve", "Insidious Whisper",
                "Grave Sacrament", "Devour Vitae", "Malice", "Life Draw", "Cleave Flesh", "Soulrend",
                "Dreadful Wrath", "Hateful Slam", "Piercing Feedback", "Harm Touch" } },
            { FinalClass.Paladin, new[] {   // Power Cleave, Demonstration of Faith census-MOVED to Crusader SHARED
                "Consecration", "Holy Strike", "Divine Vengeance", "Ancient Wrath", "Decree",
                "Prayer of Healing", "Shock of Conviction", "Heroic Dash", "Glorious Strike", "Penitent Kick",
                "Judgment", "Faith Strike", "Holy Circle", "Refusal of Atonement", "Castigate" } },
            { FinalClass.Guardian, new[] {   // Taunting Blow, Bash census-MOVED to Warrior SHARED
                "Taunting Assault", "Gut Kick", "Slam", "Decimate", "Precise Strike" } },
            { FinalClass.Berserker, new[] {   // Knee Break census-MOVED to Warrior SHARED
                "Rampaging Blow", "Berserker Onslaught", "Frenzy", "Bloodbath", "Raging Blow", "Adrenal Flow",
                "Open Wounds", "Provoking Counterattack", "Maul", "Insolent Assault", "Body Check",
                "Dragoon Spin", "Head Crush", "Demolish", "Rupture", "Stunning Roar", "Mutilate" } },
            { FinalClass.Wizard, new[] {
                "Flame Surge", "Immolation", "Glacial Wind", "Storming Tempest", "Ball of Fire", "Frost Spikes",
                "Firestorm", "Ice Comet", "Magma Chamber", "Fusion", "Ice Spears", "Manaburn", "Frost Ward" } },
            { FinalClass.Warlock, new[] {
                "Cataclysm", "Apocalypse", "Dark Pyre", "Rift", "Aftershocks", "Distortion", "Encase",
                "Aura of Pain", "Acid", "Absolution", "Static Discharge", "Abhorrence", "Dissolve",
                "Dark Infestation", "Nether Void" } },
            { FinalClass.Assassin, new[] {   // Impale census-MOVED to Predator SHARED; Ambush CUT
                "Gushing Wound", "Agonizing Pain", "Eviscerate", "Mortal Blade", "Death Blow",
                "Paralyzing Strike", "Crippling Strike", "Improvised Weapon", "Deadly Shot", "Spine Shot",
                "Head Shot" } },
            { FinalClass.Ranger, new[] {   // Sneak Attack CUT
                "Quick Shot", "Searing Shot", "Makeshift Arrow", "Trick Shot", "Storm of Arrows",
                "Bloody Reminder", "Immobilizing Lunge" } },
            { FinalClass.Monk, new[] {
                "Dragonfire", "Five Rings", "Rising Dragon", "Waking Dragon", "Crescent Strike",
                "Roundhouse Kick", "Pressure Point", "Arctic Talon", "Rising Phoenix", "Frozen Palm",
                "Silent Palm", "Striking Cobra", "Charging Tiger", "Jolting Strike", "Combination" } },
            { FinalClass.Bruiser, new[] {   // Shoulder Charge, Devastation Fist census-MOVED to Brawler SHARED
                "Shifty Dodge", "One Hundred Hand Punch", "Blaze Kick", "Thunder Fist", "Savage Assault",
                "Shove", "Engulf", "Roundhouse", "Roughhousing", "Pummel", "Beatdown", "Merciless Stomp",
                "Meteor Fist", "Uppercut", "Eye Gouge", "Sucker Punch", "Baton Flurry", "Shimmering Strike" } },
        };

        // Thin-class census firm-ups (single-class-by-union from census_index.tsv) — recover recall for
        // the log-thin classes. Separate from ByFinal to avoid dup-key merges; BuildLookup iterates both.
        private static readonly Dictionary<FinalClass, string[]> ByFinalFirmup = new Dictionary<FinalClass, string[]>
        {
            { FinalClass.Ranger, new[] {
                "Archer's Fury", "Arrow Rip", "Bloody Reminder", "Coverage", "Crippling Arrow", "Emberstrike",
                "Focus Aim", "Hawk Attack", "Hidden Shot", "Huntmaster", "Immobilizing Lunge", "Killing Instinct",
                "Lightning Strike", "Makeshift Arrows", "Miracle Shot", "Natural Selection", "Point Blank", "Primal Reflexes",
                "Ranger's Blade", "Rear Shot", "Searing Shot", "Shadebringer", "Snaring Shot", "Snipe",
                "Sniper Shot", "Stalker's Resolve", "Steady Bow", "Storm Of Arrows", "Stream Of Arrows", "Thorny Trap",
                "Tracing Shot", "Trick Shot", "Triple Shot" } },
            { FinalClass.Warlock, new[] {
                "Absolution", "Acid", "Acid Storm", "Apocalypse", "Aspect Of Darkness", "Aura Of Void",
                "Boon Of The Damned", "Cataclysm", "Curse Of Darkness", "Curse Of Void", "Dark Infestation", "Dark Nebula",
                "Dark Pact", "Dark Pyre", "Dark Siphoning", "Decimation", "Dissolve", "Distortion",
                "Encase", "Enhanced Cataclysm", "Gift Of Bertoxxulous", "Grasp Of Bertoxxulous", "Mana Trickle", "Netherealm",
                "Netherlord", "Netherous Bind", "Null Caress", "Nullify", "Nullmail", "Perdition",
                "Plaguebringer's Resolve", "Rift", "Shadowsight", "Shroud Of Bertoxxulous", "Skeletal Grasp", "Vacuum Field",
                "Void Contract" } },
            { FinalClass.Guardian, new[] {
                "Armored", "Assault", "Battle Cry", "Call Of Shielding", "Champion's Resolve", "Decimate",
                "Dissociate Limbs", "Focused Offensive", "Forward Charge", "Guardian Sphere", "Gut Kick", "Iron Will",
                "Moderate", "Never Surrender", "Overpower", "Plant", "Precise Strike", "Recapture",
                "Reinforcement", "Retaliate", "Ruin", "Saving Grace", "Sentinel", "Sentry Watch",
                "Sever", "Slam", "Taunting Assault", "Taunting Strike", "Tower Of Stone" } },
            { FinalClass.Berserker, new[] {
                "Abandoned Fury", "Adrenaline", "Aggressive Defense", "Annihilate", "Battlemaster's Resolve", "Berserk Rage",
                "Berserker Onslaught", "Blood Rage", "Bloodbath", "Bloodlust", "Body Check", "Chaos",
                "Controlled Rage", "Demolish", "Destructive Rage", "Dismember", "Enrage", "Frenzy",
                "Head Crush", "Insolence", "Juggernaut", "Maul", "Mock", "Mutilate",
                "Open Wounds", "Overwhelming Force", "Raging Blow", "Rampage", "Reckless Aide", "Rupture",
                "Stunning Roar", "Unflinching Will", "Vision Of Madness", "Wall Of Rage", "War Cry", "Weapon Counter" } },
        };

        private static readonly Dictionary<Subclass, string[]> BySubclass = new Dictionary<Subclass, string[]>
        {
            { Subclass.Rogue, new[] {   // + census-MOVED Backstab, Gouge
                "Interrupt", "Pirate Stab", "Traumatic Swipe", "Walk the Plank", "Shadow Slip",
                "Boot Dagger", "Torporous Strike", "Backstab", "Gouge" } },
            { Subclass.Enchanter, new[] {   // + census-MOVED Ego Shock, Overwhelming Silence
                "Nullifying Staff", "Counterblade", "Spellblade's Counter", "Daydream",
                "Ego Shock", "Overwhelming Silence" } },
            { Subclass.Summoner, new[] {
                "Vampire Bats", "Theurgist's Detonation", "Animated Dagger" } },
            { Subclass.Bard, new[] {   // + census-MOVED Singing Shot
                "Bump", "Rhythm Blade", "Turnstrike", "Round Bash", "Messenger's Letter", "Singing Shot" } },
            { Subclass.Druid, new[] {   // + census-MOVED Regrowth
                "Infusive Wrath", "Feral Pulse", "Thunderspike", "Regrowth" } },
            { Subclass.Cleric, new[] {
                "Bolt of Power", "Smite Corruption", "Divine Demonstration", "Skull Crack", "Divine Castigation" } },
            { Subclass.Shaman, new[] {
                "Aura of Warding", "Leg Bleed", "Eidolic Ward" } },
            { Subclass.Crusader, new[] {   // + census-MOVED Power Cleave, Demonstration of Faith
                "Aura of Leadership", "Swift Attack", "Doom Judgment", "Hammer Ground", "Lance",
                "Power Cleave", "Demonstration of Faith" } },
            { Subclass.Warrior, new[] {   // + census-MOVED Taunting Blow, Bash, Knee Break
                "Acceleration Strike", "Taunting Blow", "Bash", "Knee Break" } },
            { Subclass.Sorcerer, new[] {
                "Flames of Velious", "Magi's Shielding", "Ambidexterous Casting" } },
            { Subclass.Predator, new[] {   // + census-MOVED Impale
                "Poison Combination", "Stalk V", "Noxious Venom", "Bladed Opening", "Impale" } },
            { Subclass.Brawler, new[] {   // + census-MOVED Shoulder Charge, Devastation Fist
                "Crane Twirl", "Crane Sweep", "Mantis Bolt", "Mantis Star", "Shoulder Charge", "Devastation Fist" } },
        };

        // ★PREMIUM every-cast procs — class-unique, fire constantly (signatures.md METHOD NOTES).
        private static readonly HashSet<string> Premium = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Lich's Siphoning", "Reaver's Mania", "Lunar Attendant's Oracle's Blessing", "Spiritual Circle",
        };

        private struct Record { public Subclass Subclass; public FinalClass Final; }

        private static readonly List<string> CrossSubclassCollisions = new List<string>();
        private static readonly Dictionary<string, Record> Lookup = BuildLookup();

        public static IReadOnlyList<string> FindCrossSubclassCollisions() => CrossSubclassCollisions;
        public static IEnumerable<string> AllNames => Lookup.Keys;

        public static bool IsPremium(string abilityName)
            => abilityName != null && Premium.Contains(abilityName);

        public static bool TryResolve(string abilityName, out Subclass subclass, out FinalClass final)
        {
            subclass = Subclass.Unknown;
            final = FinalClass.Unknown;
            if (abilityName == null || !Lookup.TryGetValue(abilityName, out var rec)) return false;
            subclass = rec.Subclass;
            final = rec.Final;
            return true;
        }

        private static Dictionary<string, Record> BuildLookup()
        {
            var map = new Dictionary<string, Record>(StringComparer.OrdinalIgnoreCase);
            var collided = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var pair in ByFinal)
                AddFinal(map, collided, pair.Key, pair.Value);
            foreach (var pair in ByFinalFirmup)
                AddFinal(map, collided, pair.Key, pair.Value);
            foreach (var pair in BySubclass)
                foreach (var name in pair.Value)
                    Add(map, collided, name, pair.Key, FinalClass.Unknown);
            return map;
        }

        private static void AddFinal(Dictionary<string, Record> map, HashSet<string> collided, FinalClass final, string[] names)
        {
            var subclass = ClassTree.SubclassOf(final);
            foreach (var name in names)
                Add(map, collided, name, subclass, final);
        }

        private static void Add(Dictionary<string, Record> map, HashSet<string> collided, string name, Subclass subclass, FinalClass final)
        {
            if (map.TryGetValue(name, out var existing))
            {
                if (existing.Subclass != subclass)   // cross-subclass collision — a catalog bug
                {
                    if (collided.Add(name)) CrossSubclassCollisions.Add(name);
                    return;
                }
                if (existing.Final != final)   // same subclass, two finals → a subclass-shared name
                    map[name] = new Record { Subclass = subclass, Final = FinalClass.Unknown };
                return;
            }
            map[name] = new Record { Subclass = subclass, Final = final };
        }
    }
}
