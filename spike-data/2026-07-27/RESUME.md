# RESUME — class-signature dig (paused 2026-07-27, ~halfway)

## Where we are
- **Ground truth:** `labels.tsv` — 32 players → final class → subclass. All **24 classes witnessed**.
- **Curated signatures:** `signatures.md` — **6 of 12 subclasses DONE** (Rogue, Enchanter, Summoner, Bard, Druid, Cleric) with tiers (STRONG / WEAK / SHARED / CUT) + a METHOD NOTES section.
- **Scripts:** `detail.py` (per-subclass candidate dump — the resume tool), `sigdig.py` (full 24-class catalog).
- **Logs:** `eq2log_Biffels.2026.07.22.7z` (contains the 07.19, 07.22, 07.27 raid nights).

## Remaining (6 subclasses)
- **Shaman** — PRESENTED to Alex, not yet pruned. He paused here: doesn't know Mystic/Defiler well enough off-hand to judge which candidates are class-specific vs Shaman-shared. (Premium pet-procs already spotted: Lunar Attendant's Oracle's Blessing = Mystic, Spiritual Circle = Defiler.)
- **Crusader, Warrior, Sorcerer, Predator** — not started.
- **Brawler** — ⚠️ ORPHANED. Vicious is labeled `Monk/Bruiser` (he betrayed), so his Brawler abilities (Crane Twirl etc.) don't map to one final. Fix before/when doing Brawler: label a 2nd Brawler (**Skynet**, roster #24, looks like one) OR treat Vicious's set as Brawler-shared.

## How to resume
1. Extract logs: `bsdtar -xf eq2log_Biffels.2026.07.22.7z -C <scratch>/logs`
2. Per subclass: `python3 detail.py <Subclass> <log19> <log22> <log27>`
3. Present candidates; Alex prunes to STRONG / WEAK / SHARED / CUT; append to `signatures.md`.

## Method learnings so far (full detail in signatures.md)
- **Class-UNIQUE procs = the BEST signatures** (Lich's Siphoning / Lunar Attendant / Spiritual Circle — fire every cast). Opposite of **cross-class procs = noise** (Vampiric Requiem, Fae Fires racial, generic weapon "Poison").
- **Pets:** own-class/fixed pets are great tells (Necro undead + scout pet, Conjuror elementals, Warden wolf). **Charm/CLONE pets (Coercer) adopt other classes' abilities → exclude.**
- **Item procs** (e.g. Healing Blanket = a cloak) can appear on 2/2 players — an ability on both samples can still be gear, not class. Game knowledge required.
- **Spec/AA abilities:** can be class-UNIQUE (Inquisitor "Strike of X" melee versions) OR subclass-SHARED (Druid spec Furies took, Warden didn't). Check which subclasses can take it.
- **High-precision/low-recall** signatures (Troub symphony shields) = confirming LOCK, not primary detector.
- Curation rule: strong signatures are spammed every fight, so freely DROP low-confidence/uncertain candidates.

## Still open beyond the dig
- **Ranger** has only a trace (Tluan, ~34 actions) → real signatures need the reference DB.
- A **frequency/timing pass** (segment encounters by timestamp, rank by early-in-fight + casts/encounter) would auto-surface the premium procs. Not built yet.
- A **reference ability→class DB** (EQ2U/ZAM) is the completeness backstop.
