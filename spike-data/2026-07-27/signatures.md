# Curated class signatures — ground-truth pruned 1-by-1 with Alex
# tier: STRONG = frequent, class/subclass-specific (good live signal)
#       WEAK   = valid class ability but a cooldown/filler (slow to fire — low priority)
#       SHARED = subclass-level (both finals cast it) — resolves color, not final class
#       CUT    = removed (cross-class proc/buff/poison — false signal)

## Rogue  ✓ (Swashbuckler ×3 samples, Brigand ×1)
Swashbuckler STRONG: Evade Blame, Flurry of Blades, Snap of the Wrist, Kidney Stab, Flash of Steel, Hamstring, Lucky Gambit, Dashing Swathe, Razor Edge, Viscerate, Lung Puncture, Storm of Steel, Flamboyant Strike
Swashbuckler WEAK:   Daring Attack [cooldown ~10s of triggers], Arctic Blast [ranged filler]
Brigand STRONG:      Backstab, Shank, Baffle, Puncture, Battery and Assault, Bum Rush, Barroom Negotiation, Desperate Thrust, Stunning Blow, Gouge, Black Jack, Dispatch, Murderous Rake, Double Blast, Debilitate
Brigand WEAK:        thug / thug's Assault [pet summon, cooldown]
Rogue SHARED:        Interrupt, Pirate Stab, Traumatic Swipe, Walk the Plank, Shadow Slip, Boot Dagger, Torporous Strike
Rogue CUT:           Primal Instincts (a Warden buff leaking onto Cyanotic), warding ebb (poison shared by 4 classes)

## Enchanter  ✓ (Illusionist ×2, Coercer ×2)
Illusionist STRONG:  Chromatic Shower, Prismatic Shock, Chromatic Storm, Theorems, Phantasmal Shock, Brainburst, Ultraviolet Beam, Nightmare, Brain Clot, Overwhelming Silence, Confusion, Headache, Color Shower, Migraine, Speechless, Aneurysm, Paranoia
Coercer STRONG:      Lash, Convulsions, Silence, Despotic Mind, Simple Minds, Brainshock, Hemorrhage, Asylum, Shock Wave, Ego Shock, Medusa Gaze, Forceful Headache
Enchanter SHARED:    Nullifying Staff, Counterblade, Spellblade's Counter, Daydream (spec ability — not all have it, but both subclasses can)
Enchanter CUT:       Shock of Mana + Linked Pain (unknown, not needed), Firesong (weapon proc), Assault + Sentry Watch Guard (Coercer CLONE-PET abilities — pet adopts other classes' spells → taint)

## Summoner  ✓ (Conjuror ×1, Necromancer ×2)
Conjuror STRONG (conjuror-only): Fiery Annihilation, Force of the Elements, Aery Whip, Seed of Fire, Blaze, Flameshield, Shattered Earth, Crystal Blast, Winds of Velious, Thunderous Attack, Furystorm, Earthquake, Wisp Blade, Storm Surge; pets: aqueous swarm, roaring flames
Necromancer STRONG: Lich's Siphoning [★PREMIUM — necro-only proc, fires on EVERY spell cast, most prevalent necro event], Soulrot, Grim Embrace, Bloodcoil, Grim Wave, Consume, Pandemic, Grim Devastation, Lifetap, Grim Bolt, Grim Lifetap, Lifeburn; pets: blighted horde, undead horde, awaken grave; scout-pet (necro-unique): Throat Gash, Poisoned Spike
Summoner SHARED:     Vampire Bats, Theurgist's Detonation, Animated Dagger

## Bard  ✓ (Troubador ×2, Dirge ×2)
Troubador STRONG:    Chaos Anthem, Ceremonial Blade, Dancing Blade, Thunderous Overture, Perfect Shrill, Sandra's Deafening Strike, Painful Lamentations, Tap Essence, Night Strike, Breathtaking Bellow, Vexing Verses, Singing Shot, Song Barrier
Troubador LOCK-IF-SEEN: Arcane Symphony, Elemental Concerto (spec shield effects — 100% Troubador when present, but spec-dependent so often absent)
Dirge STRONG:        Darksong Blade, Scream of Death, Wail of the Banshee, Thuri's Doleful Thrust, Daro's Dull Blade, Tarven's Crippling Crescendo, Luda's Nefarious Wail, Lanet's Excruciating Scream, Howl of Death, Jarol's Sorrowful Requiem, Misfortune's Kiss, Banshee's Scream, Jael's Dreadful Deprivation, Death Barrier
Bard SHARED:         Bump, Rhythm Blade, Turnstrike, Round Bash, Messenger's Letter
Bard CUT:            Poison (weapon proc), Sonic Interference + Fiendish Bite + Clearcut (low-confidence, not needed)

## Druid  ✓ (Fury ×2, Warden ×1)
Fury STRONG:         Autumn's Kiss, Porcupine, Porcupine Quills, Untamed Regeneration, Savage Feast, Ring of Fire('s Flames), Thunderbolt, Regrowth, Call of Storms, Starnova, Nature's Elixir, Back into the Fray
Warden STRONG:       Winds of Healing, Healstorm, Photosynthesis, Nature's Embrace, Spores, Frostbite, Nature's Blessing, Reincarnate, Verdant Trinity; pet: wolf ally
Druid SHARED (spec): Infusive Wrath, Feral Pulse, Thunderspike (optional spec/AA — Furies took them for damage, this Warden didn't)
Druid CUT:           Healing Blanket (cloak proc), Natural Rejuvenation (proc), Clouded Mind (unknown), Awakening (uncertain)

## Cleric  ✓ (Templar ×1, Inquisitor ×2)
Templar STRONG:      Shield of Faith, Divine Smash, Divine Prayer, Involuntary Cure, Supplicant's Prayer, Reverence, Combat Glory, Benefaction, Word of Redemption, Blaze of Faith, Holy Touch
Inquisitor STRONG:   Vengeance, Torment, Alleviation, Convert Ally, Repenting Strike, Atoning Faith, Litany Circle, Heretic's Doom, Hammer Divine Smite, Heretical Strike, Vengeful Faith, Litany, Ministration
Inquisitor SPEC (still inq-unique — melee versions of base spells): Writhing Strike, Strike of Corruption, Strike of Flames, Invocation Strike
Cleric SHARED:       Bolt of Power, Smite Corruption, Divine Demonstration, Skull Crack, Divine Castigation
Cleric CUT:          Cleanse (unsure, not needed); Templar low-count: Fate of Healing, Restoration, Battle's Reprieve

## Shaman  ✓ (Mystic ×1, Defiler ×1)
Mystic STRONG:       Lunar Attendant's Oracle's Blessing [★pet-proc], Ancestral Ward, Umbral Warding, Runic Armor, Torpor, Prophetic Ward, Oberon  (primary ward-heals — always cast when present, class-unique)
Defiler STRONG:      Spiritual Circle [★pet-proc], Shroud of Armor, Carrion Warding, Ancient Shroud  (primary ward-heals)
Shaman SHARED:       Aura of Warding, Leg Bleed, Eidolic Ward
Shaman NOTE:         non-ward dmg/debuff (Mystic: Bear Claws/Glacial Strike/Rabies…; Defiler: Defile/Soul Cannibalize/Putrefy…) left unresolved (single-sample). Wards+pet-proc sufficient; Defile/Soul Cannibalize obvious Defiler if more wanted.

## Crusader  ✓ (Shadowknight ×1, Paladin ×1)
Shadowknight STRONG: Reaver's Mania [★PREMIUM — lifetap proc, fires EVERY cast, as good as Lich's], Pestilence, Grim Strike, Mana Sieve, Insidious Whisper, Grave Sacrament, Devour Vitae, Malice, Life Draw, Cleave Flesh, Soulrend, Dreadful Wrath, Hateful Slam, Piercing Feedback, Harm Touch
Paladin STRONG:      Consecration, Holy Strike, Divine Vengeance, Power Cleave, Ancient Wrath, Decree, Prayer of Healing, Shock of Conviction, Heroic Dash, Glorious Strike, Penitent Kick, Judgment, Faith Strike, Holy Circle, Demonstration of Faith, Refusal of Atonement, Castigate
Crusader SHARED:     Aura of Leadership, Swift Attack, Doom Judgment, Hammer Ground, Lance

## Warrior  ✓ (Guardian ×1, Berserker ×1) — THIN DATA (low-attendance samples; reference DB should firm this up)
Guardian STRONG:     Taunting Blow, Taunting Assault (taunts — role-tell), Gut Kick, Slam, Decimate, Precise Strike, Bash
Berserker STRONG:    Rampaging Blow, Berserker Onslaught, Frenzy, Bloodbath, Raging Blow, Adrenal Flow, Open Wounds, Provoking Counterattack (taunt), Maul, Insolent Assault, Body Check, Dragoon Spin, Knee Break, Head Crush, Demolish, Rupture, Stunning Roar, Mutilate
Warrior SHARED:      Acceleration Strike
Warrior CUT:         Magic Feedback (vague — likely gear proc)

## Sorcerer  ✓ (Wizard ×1, Warlock ×1 — Warlock thin, ref-DB candidate)
Wizard STRONG:       Flame Surge, Immolation, Glacial Wind, Storming Tempest, Ball of Fire, Frost Spikes, Firestorm, Ice Comet, Magma Chamber, Fusion, Ice Spears, Manaburn, Frost Ward; pets: protoflame (Protofire), Furnace of Ro
Warlock STRONG:      Cataclysm, Apocalypse, Dark Pyre, Rift, Aftershocks, Distortion, Encase, Aura of Pain, Acid, Absolution, Static Discharge, Abhorrence, Dissolve, Dark Infestation, Nether Void
Sorcerer SHARED:     Flames of Velious, Magi's Shielding (shared buff), Ambidexterous Casting (shared AA)

## Predator  ✓ (Assassin ×1, Ranger trace — ref-DB firm-up)
Assassin STRONG:     Impale, Gushing Wound, Agonizing Pain, Eviscerate, Mortal Blade, Death Blow, Ambush, Paralyzing Strike, Crippling Strike, Improvised Weapon, Deadly Shot, Spine Shot, Head Shot  ("Shot" = assassin ranged)
Ranger STRONG (trace): Quick Shot, Searing Shot, Makeshift Arrow, Trick Shot, Storm of Arrows, Bloody Reminder (bow); Sneak Attack, Immobilizing Lunge (uncertain)
Predator SHARED:     Poison Combination, Stalk V, Noxious Venom, Bladed Opening (AA a ranger could take)
Predator CUT:        Hilt Strike (shared among several scouts), Swipe (vague/unknown), Shadow of Purity (unknown), Caustic Poison (generic 4-class poison)

## Brawler  ✓ (Monk ×1 [Vicious, betrayed], Bruiser ×2 [Skynet, Defileds])
Monk STRONG:         Dragonfire, Five Rings, Rising Dragon, Waking Dragon, Crescent Strike, Roundhouse Kick, Pressure Point, Arctic Talon, Rising Phoenix, Frozen Palm, Silent Palm, Striking Cobra, Charging Tiger, Jolting Strike, Combination
Bruiser STRONG:      Shifty Dodge, One Hundred Hand Punch, Blaze Kick, Thunder Fist, Savage Assault, Shove, Engulf, Roundhouse, Roughhousing, Pummel, Beatdown, Merciless Stomp, Meteor Fist, Shoulder Charge, Uppercut, Eye Gouge, Sucker Punch, Devastation Fist, Baton Flurry, Shimmering Strike
Brawler SHARED (AAs): Crane Twirl, Crane Sweep, Mantis Bolt, Mantis Star
Brawler CUT:         Chilling Darkness (weapon proc); Skynet's Unholy Strike / Sacred Vengeance / Strike of Faith (gear procs)
Brawler NOTE:        No PURE Monk in data (Vicious played both) → could isolate Monk (Vicious minus the Bruisers) but NOT split Bruiser-from-shared (Vicious's Bruiser-era casts masquerade as shared). Alex's knowledge resolved it: only the 4 Crane/Mantis AAs are truly both-subclass shared. All → Brawler color regardless.

# METHOD NOTES (learned during pruning)
# - Betrayed player as the sole sample for one final: you CAN isolate the OTHER final's uniques (Monk = Vicious minus pure-Bruisers) but CANNOT split that final from subclass-shared (their cross-era casts look shared). Needs a pure single-class sample or ground-truth knowledge.
# - Some abilities are optionally shared across a HANDFUL of classes (Hilt Strike among scouts, Caustic Poison across 4 classes) — cut like any cross-class ability (broader than one subclass = not a signature).
# - ★PREMIUM every-cast procs are the emerging gold-standard per class: Lich's Siphoning (Necro), Reaver's Mania (SK), Lunar Attendant's Oracle's Blessing (Mystic), Spiritual Circle (Defiler). Class-unique + fires constantly. A frequency/timing pass would auto-rank these #1.
# - ROLE-defining abilities are reliable signatures: a healer always casts their primary heal (Shaman wards), a tank its taunts. The role compels the cast → guaranteed to appear + class-unique.
# - Spec variants can be class-UNIQUE (Inquisitor melee "Strike of X" versions of base spells) OR subclass-SHARED (Druid spec). Depends on which subclasses can take it — check, don't assume.
# - High-precision / low-recall signatures exist: definitive-if-seen but not always present (Troub symphony shields = spec-dependent). Use as a confirming LOCK, not a primary detector.
# - SPEC/AA abilities: optional choices both subclasses CAN take (Druid: Infusive Wrath/Feral Pulse/Thunderspike) → subclass-SHARED, but not universal.
# - ITEM procs can appear consistently across multiple players of a class (Healing Blanket = a cloak both Furies wore). An ability on 2/2 can still be gear, not class — game knowledge catches these.
# - Class-UNIQUE procs are the BEST signatures (Lich's Siphoning = necro-only, procs every cast). Opposite of CROSS-class procs (Vampiric Requiem, Fae Fires) which are noise. Distinguish by how many classes cast it.
# - Fixed/own-class pets are GOOD signatures (Necro undead + scout pet, Conjuror elementals — class-unique spells; pet NAMES are tells). Charm/CLONE pets (Coercer) adopt other classes' abilities → exclude.
# - Cooldowns/filler demoted or cut: strong signatures are spammed every fight, so marginal ones aren't needed. (A frequency/timing pass would auto-surface the premiums like Lich's Siphoning.)
