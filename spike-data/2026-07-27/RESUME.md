# Class-signature dig — COMPLETE (all 12 subclasses curated)

Done 2026-07-30. Ground-truth-labeled + pruned 1-by-1 with Alex. This is the input to the
eventual class-colors **inference design** (a separate brainstorm; not started).

## Deliverables (here)
- **`signatures.md`** — all **12 subclasses** curated, per final class, tiered STRONG / WEAK / SHARED / CUT, plus a METHOD NOTES section of learnings.
- **`labels.tsv`** — 34 ground-truth player→class labels covering all 24 classes.
- **`detail.py`** / **`sigdig.py`** — the analysis tools; **`player-roster.md`** — the labelable roster.
- **`eq2log_Biffels.2026.07.22.7z`** — the 3 raid nights (re-extract to run the scripts).

## Confidence / gaps (for the reference-DB pass)
- **Solid** (multi-sample or clean): Rogue, Enchanter, Summoner, Bard, Druid, Cleric, Crusader, Brawler (Monk+Bruiser split via 3 samples).
- **Thin — firm up with a reference `ability→class` DB (EQ2U/ZAM):** Warrior (low-attendance tanks), Sorcerer (Warlock under-sampled), Predator (Ranger is a ~34-action trace).
- **Premium ★every-cast procs found** (the ideal live tells): Lich's Siphoning (Necro), Reaver's Mania (SK), Lunar Attendant's Oracle's Blessing (Mystic), Spiritual Circle (Defiler).

## What's next (inference design phase — Alex-directed, not started)
1. **Reference ability→class DB** — firm up the thin classes + backstop anything the logs can't witness.
2. **Frequency/timing pass** — segment encounters by timestamp, rank each signature by how *early-in-fight* and how *often* it fires (auto-surfaces the premium procs, demotes cooldowns).
3. **Formalize** — brainstorm the actual inference engine (how the plugin ingests signatures, infers class→subclass live, feeds the color resolver at `MeterEngine.cs:101`), then spec + plan per the normal review flow.

## Key method learnings (full list in signatures.md METHOD NOTES)
- Class-UNIQUE procs = best signatures; CROSS-class procs/racials/weapon-procs = noise (distinguish by # of classes casting).
- Own-class/fixed pets = good tells (names included); charm/CLONE pets (Coercer) adopt other classes' abilities → exclude.
- Item procs (cloaks) can appear on 2/2 → game knowledge required.
- Spec/AA abilities: class-UNIQUE (Inquisitor strikes) OR subclass-SHARED (Druid spec) — check which subclasses can take it.
- Role-defining abilities (healer wards, tank taunts) are reliable — the role compels the cast.
- A betrayed sole-sample can isolate the OTHER final's uniques but can't split its own final from shared.
