# Player-doc style guide

Every generated wiki page documents, for a **player**, what a feature is and how to use it. This guide is the voice, depth, and shape every page follows. When unsure, match the golden examples at the bottom.

## Voice — a reference contract

Each page is reference documentation. Follow the contract:

- **Open with what a feature *is* or *does* — never why it helps.** The first sentence states the mechanism in neutral terms: not a benefit, and not what it lets you do "at a glance."
  - Write: *"Each row is coloured by the combatant's class."*
  - Not: *"Class colours let you read the raid at a glance."*
- **The phrase "at a glance" is banned outright — anywhere, not just the opener.** It is the benefit-framing tell. When explaining *why* something is the way it is, state the fact, not the perception it enables: write *"colour keys at the subclass level — the twelve classes a player is told apart by,"* not *"…the level you read at a glance."*
- **Then document how it works** — present tense, factual, plainly stated. No narrative build-up ("as an ability gets close, its timer springs to life…").
- **Address the player as "you"** for actions they take (*"uncheck Class colours to turn it off"*); describe behaviour plainly otherwise.

## Depth — exhaustive, but complete-not-padded

- **Enumerate everything the source states.** Every metric, scope, setting, knob, and option the source lists. Someone looking a feature up must find *all* of it, not a sample. "DPS, HPS, and others" is a failure — list them all, each with what it does.
- **Complete, not padded.** Exhaustive means covering the feature's real surface, never hitting a word count. A genuinely small feature gets a short, complete section; a rich one gets a long section. Depth tracks the feature, not a target length.

## Fidelity — every concrete value traces to the source

Exhaustive means covering everything the **source** states — never inventing to fill a gap. This bounds the depth rule above:

- **A concrete value — a number, colour, hex code, key, default, or limit — appears on a page only if it appears in the mapped source.** A plausible-looking value you supply yourself is fabrication, and false docs are worse than absent ones. The only values you write are the ones the source itself gives (e.g. a neutral grey `#8B93A3` stated in the source is carried over verbatim).
- **When the source says a set *exists* but does not list its members, describe it as the source does — do not invent the list.** Source: *"each subclass has one fixed colour."* Write: *"each subclass has its own fixed colour."* Never a subclass→colour table you filled in yourself.
- **A table is a structure, not a quota.** If the source supplies three columns of facts and a fourth column's values are not stated, the table has three columns — never a fourth completed by invention.

## Structure — granular; one idea per section/bullet

- **One idea per section, paragraph, or bullet.** Never cram several distinct facts into one run-on paragraph — it flattens them so none land. Give each its own line.
- **Tables and lists for any option set** (metrics, scopes, knobs). A table beats a comma-separated sentence.
- **Sub-sections are cheap — use them.** A cluster of related facts (e.g. how numbers are shaped — rates/totals/counts) is its own sub-section, and can be promoted to a shared page/section where the same concept recurs.
- **Per feature:** one line stating what it is, then a sub-section per aspect (its options, how you configure it, its sub-behaviors), each documented completely.

## Never (hard bans)

- **No screenshots or image markdown** (`![…]`).
- **No internal / developer terms** — `CombatantData`, `MasterSwing`, `EncounterData`, class/method/file names, code symbols, "the SPEC," line numbers. The reader is a player, not a developer.

(Selling and storytelling are handled by the Voice contract; cramming by the Structure recipe — a positive recipe binds better than a ban, so they are not repeated here as prohibitions.)

## Golden examples

Match these exactly — the target voice, depth, and structure. The first shows a rich feature (long, fully enumerated); the second shows a small feature (short, but still complete — proof that depth scales with the feature, not a word count).

---

### Metrics

Every meter window shows one **metric** — what its rows measure. You pick it from the window's right-click menu. Each choice also sets *who* is measured (see [Scope]), so "DPS" and "Enemy Damage Taken" are separate choices even though both concern damage.

**The metrics:**

| Selection | Measures | Rows are |
|---|---|---|
| **DPS** | Damage dealt, per second | your side |
| **HPS** | Healing done, per second | your side |
| **Total Healing** | Total healing done | your side |
| **Healing Taken** | Healing received | your side |
| **Damage Taken** | Damage received | your side |
| **Cures** | Number of cures / dispels | your side |
| **Power Replenish** | Power restored to others | your side |
| **Enemy Damage Taken** | Damage your side dealt to the enemies | the enemies |
| **Enemy Healing Done** | Healing the enemies did to themselves | the enemies |
| **Deaths** | A timeline of deaths (drilling in opens a death recap) | your side |

"Enemy Damage Taken" and "Damage Taken" are the same measurement pointed at opposite sides — as are "Enemy Healing Done" and "Total Healing."

**Metric kinds.** Each metric is one of three:
- **Rate** — a per-second figure (the total over the fight's length). *DPS, HPS.*
- **Total** — the running sum over the fight. *Total Healing, Healing Taken, Damage Taken, Power Replenish.*
- **Count** — a plain tally. *Cures.*

**How values display.** Large values are abbreviated — `1.24M`, `12.4K`. Counts and values below 1,000 show in full.

**Primary and secondary.** A window shows a primary metric and, optionally, one secondary:
- **Primary** — the metric you pick. Drives the bar length, sort order, and percentages.
- **Secondary** — an optional second metric shown as a number on each row (e.g. DPS primary, Cures alongside). It follows the primary's side, and only one shows at a time.
- **Deaths** can't be a secondary — it's a timeline, not a per-row number. Primary only.

---

### Escalation

Escalation makes the timers about to come due stand out from those further off. It happens on its own — you never turn it on — and each timer's own warning time (set on its ACT trigger) is the threshold.

As a timer nears its warning threshold, it changes in several ways at once:
- **Size** — it grows relative to the others.
- **Position** — it moves toward the panel's center.
- **Motion** — it pulses.
- **LATE** — once past due, it is tagged LATE.

**Colour is not an escalation cue.** A timer's colour identifies which ability it is (see [Timer colours]) and may be any palette colour, a custom colour, or greyscale. Urgency is carried only by size, position, motion, and the LATE tag — never colour.
