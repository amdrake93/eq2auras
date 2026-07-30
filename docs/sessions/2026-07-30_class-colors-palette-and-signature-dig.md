# Session chronicle — class colors: the palette, then mining a class-signature catalog from raid logs

*2026-07-27 → 30. From "i think we're going to try class colors" to two finished inputs for a feature that isn't built yet: a locked **12-subclass color palette**, and a ground-truth-pruned, **census-validated 24-class ability-signature catalog** — the data source a future inference engine will use to color meter rows by class. The whole run was a **spike**: expedient learning, zero design weight, the engine design still starts from a blank page. Stories worth keeping: the palette's reference-first turn (EQ2 has its own class colors), the identity-vs-grouping pivot, invents that must dodge WoW's meanings; then the data arc — a feasibility spike, a **train wreck** that forced a human-ground-truth pivot, a 1-by-1 curation that turned Alex's game knowledge into a reusable signature playbook, and a census cross-reference (grounded in Alex's *own* other repo) that nearly over-corrected until the names-only reality set it straight.*

## Part I — the palette

**Reference-first paid off before I could invent anything.** The plan was "borrow WoW class colors." But grounding first (a habit Alex has drilled in) turned up that **EQ2 has its own class-color convention** — the Heroic Opportunity wheel colors abilities by archetype (Fighter blue, Priest yellow, Scout green, Mage red). Neither of us knew it as a *color system*. It didn't end up load-bearing (archetype-level, weak recognition), but it reframed the whole question: the recognition we were chasing barely exists in EQ2, so we had latitude.

**The pivot that set the scheme: identity, not grouping.** I kept trying to make color carry archetype-grouping (hue families). Alex's examples killed it — "Rogue is a scout, but *rogue is yellow* says rogue." Color's job is to say *what a row is at a glance*, and for a WoW-transplant guild the fastest identity is the burned-in WoW color. That chose **per-subclass identity**, borrowing WoW colors where a subclass maps.

**Invents must dodge WoW's meanings.** The sharp constraint Alex surfaced: because we're reusing WoW colors *for their meaning*, an invented color that lands near a WoW one smuggles that meaning in. Red screams Death Knight = Shadowknight-only → poisons **Crusader** (which is Paladin *and* SK). Pink = Paladin, also poison. So Crusader had to be a hue WoW doesn't claim — **gold**, arrived at by elimination in the visual companion (steel died into Evoker-teal at low opacity, bronze into Warrior-tan). **Bard → indigo** (no WoW bard). The other ten are WoW borrows. Locked, written to `docs/plans/2026-07-27-class-colors-palette.md`, backlog'd.

Two references did the validating, not my taste: **Details!** (its source proved 13 class-colored rows read fine in a raid meter — killed my "12 colors is mush" worry) and the visual companion (dragging the real fill-opacity knob over the real backplate is how we found Cleric-white ate its own text; Details' text-outline trick fixed it).

## Part II — the data arc

**Alex framed it as a pipeline, not a guess.** "We have MB of raid logs. I know my raiders' classes. Input + known output → design the inbetween." ACT exposes no class field, so the whole feature hinges on inferring class from ability signatures — and the only honest way to design that is against real data. He ferried three raid nights (146/78/91 MB) via `spike-data/`; I built a parser off the EQ2 combat grammar (`<Attacker>'s <Ability> <verb>…`) and pulled a roster.

**Then the train wreck.** I built the signatures by *inferring* each player's class from their abilities, then collecting those abilities as the class's signatures. It failed two ways at once, and Alex named it: "this whole script is kind of a train wreck."
- **Single-witness fallacy** — with ~1 player per class, *every* ability that one player cast looked "class-unique." Lance (Crusader-shared), Crane Twirl (Brawler-shared), Noxious Venom (Predator-shared), Daring Attack (a cooldown) all got mislabeled class-specific.
- **Theme-guessing poisoned the labels** — I assumed "nature heal = Warden," so a *Fury*-only ability (Autumn's Kiss) got called "shared." Garbage labels in, garbage signatures out.

**The reversal that made everything work:** "I'm tempted to scrap all of this and just tell you the classes of the players and then have you go dig." That's the whole lesson — **stop inferring, take ground truth, dig with it.** I handed Alex a ranked roster; he labeled 34 players covering all 24 classes (using the ability tells to *deduce* finals he didn't know off-hand — e.g. Devastation Fist on Skynet/Defileds ⇒ they're Bruisers). Multiple samples per class instantly killed the single-witness fallacy: an ability on all three Swashbucklers (Evade Blame) is real; a Warden buff leaking onto one Swashbuckler (Primal Instincts) is not.

**The 1-by-1 curation turned his game knowledge into a playbook.** We went subclass by subclass, Alex pruning, and a set of durable rules fell out (all in `spike-data/2026-07-27/signatures.md`):
- **Class-unique every-cast procs are the *best* tell** — Lich's Siphoning (Necro), Reaver's Mania (SK), the Mystic/Defiler pet-procs fire on every cast. Opposite of cross-class procs (Vampiric Requiem, Fae Fires racial) which are noise. The distinction is how many classes cast it, not "it's a proc."
- **Pets cut both ways** — own-class/fixed pets are great signatures (names included); Coercer's **clone pets adopt other classes' abilities** → exclude.
- **Item procs fool the data** — Healing Blanket sat on both Furies at 2/2 and looked class-specific; it's a *cloak*. Only game knowledge caught it.
- **Spec abilities cut both ways** — Inquisitor's melee "Strike of X" versions stay class-unique; Druid spec abilities the Furies took and the Warden didn't are subclass-shared.
- **Role-defining abilities are reliable** — a healer always casts their primary heal (Shaman wards), a tank its taunts.
- **A betrayed sole-sample** (Vicious, Monk→Bruiser) can isolate the *other* final's uniques but can't split its own from shared — needs a pure sample or ground truth.

## Part III — census, grounded in Alex's own repo

To confirm class-*uniqueness* (the axis single samples were weak on), the plan was the DBG census. Reference-first again — Alex pointed at his **own other project, `eq2-eof-itemdex`**, which already pulls per-class spells from census. Its code handed us the exact recipe (the `spell` collection, `given_by=class`, the class-id map, and a warning that class IDs differ per collection) — far better than generic docs.

**The insight that reframed it was Alex's: "we only have logs with ability names, not IDs."** That turns out to be the core design constraint. Census revealed EQ2 **reuses ability names across distinct spells** — there's a Brigand-only "Gouge" *and* a Brigand+Swashbuckler "Gouge." I first "corrected" my cross-ref to keep Gouge Brigand-specific; Alex's names-only point flipped it back: the plugin only ever sees the *name*, can't know which spell, so the right model is the **union across all same-named variants — does it stay within one subclass?** Gouge's union is all-Rogue → a valid Rogue signature (Rogue is the level we color at anyway). Ambush's union spans all seven scouts → genuinely ambiguous → cut. The names-only reality that would sink a 24-precise scheme is a *non-issue* for our 12-color one — the two decisions reinforce each other.

**"Verify census disagreements, don't hand-wave" — and it cut both ways.** Census flagged **Blaze** as Sorcerer, not Conjuror; I nearly dismissed it as a collision — Alex confirmed census was right (Blaze leaked onto Sprok from elsewhere). Then census flagged **Gouge** as shared; Alex said "that's weird, should be Brigand" — and *there* it was a real name collision. Same rule, opposite outcomes: check the actual records, don't assume.

**Procs weren't a census gap, just two patterns.** Alex's description-mining idea worked: Lich's Siphoning isn't its own spell record, but the granting **Necro "Lich" ability** is, and its description names it → census-confirmed necromancer. And Reaver's Mania returns census count 0 — because it's an **SK AA** (AAs aren't class-trained spells, same as Crane Twirl came back empty). Class-ability procs → mineable; AA/pet procs → ground-truth. Nothing left unresolved.

## Where it stands

Two finished **inputs**, both in `spike-data/2026-07-27/` and `docs/plans/`:
- **Palette** — 12 subclass colors locked (10 WoW borrows + Crusader gold + Bard indigo), rendering rules (text-outline, min-alpha floor), design doc + backlog entry.
- **Signature catalog** — all 12 subclasses / 24 classes, ground-truth-pruned and census-validated under the names-only union model; thin classes (Warrior/Warlock/Ranger) firmed up from census; premium procs flagged as the ideal live tells; a full method playbook + the `census_index.tsv` (1,955 abilities) + pull scripts.

**Next (a real phase, Alex-owned, starts fresh):** the **inference → labeling → coloring pipeline** — infer a combatant's class from log ability-names against the catalog, store the `name→class` result somewhere durable, and feed it to the row-color resolver (successor to `MeterFamilyColors` at `MeterEngine.cs:101`). Everything that phase needs to *start* is now bedrock; the design of it is not yet written.
