# Class colors — the subclass palette (design decision)

*2026-07-27. Brainstorm outcome. **Scope: the color assignment only** — the 12 subclass
colors and the rules behind them. The rest of the class-colors feature (ability-signature
inference, the name→class data model, which surfaces get colored, render wiring) is **not**
designed here; see §Still open.*

## The decision in one line

Color the meter at the **subclass** level (EQ2's 12 tier-2 classes), **borrowing WoW class
colors** where a subclass maps cleanly and **inventing** two hues (Crusader, Bard) for the
gaps WoW's palette can't fill. Identity, not archetype-grouping: the color says *what this
row is* at a glance.

## Why this shape (the reasoning that survived testing)

- **Grouping job, at the 12 tier-2 level.** Tier-3 (e.g. Warden vs Fury) is "same job, minor
  flavor" — not worth a color a player reads at a glance. Data may resolve finer (see §Still
  open) but **color keys at 12**.
- **WoW colors carry meaning** for the many WoW-transplant players, so we borrow them as the
  fast identity signal. This *drops* archetype hue-grouping — colors scatter by whatever each
  subclass's identity color is (accepted trade).
- **WoW maps to EQ2's *final* classes**, so a subclass can borrow a WoW color only when *both*
  its finals point at the same WoW class. Where the two finals split across different WoW
  classes (Crusader = Paladin+Shadowknight) or there's no analog (Bard), the subclass has no
  honest WoW color → **invent**.
- **Invented colors must dodge every WoW-claimed hue**, or they smuggle in a false class
  meaning. This is why **red is out for Crusader** (red = WoW Death Knight = Shadowknight
  *only*, hiding the Paladin half — the exact split we invented the color to escape) and
  **pink is out** (= WoW Paladin). WoW's palette is dense; the genuinely unclaimed gaps are
  gold, a mid-green, and (grudgingly) a deep violet.
- **Palette runs cool-heavy** (6 cool borrows vs ~3 warm), and the borrows are locked
  (WoW-accurate), so **Crusader is the one lever to rebalance** → warm **gold**, which is also
  the cleanest unclaimed hue and thematically on-the-nose (holy crusade).

## The 12 (locked)

Values are **current-retail WoW** (verified against Warcraft Wiki `RAID_CLASS_COLORS`;
current beat classic on Alex's eye for Mage/Warlock, the only two that meaningfully drifted).

| Archetype | Subclass | Finals | Source | Hex | ARGB int |
|---|---|---|---|---|---|
| Fighter | **Crusader** | Paladin · Shadowknight | *invented — gold* | `#C9A227` | `0xFFC9A227` |
| Fighter | **Brawler** | Monk · Bruiser | WoW Monk | `#00FF98` | `0xFF00FF98` |
| Fighter | **Warrior** | Berserker · Guardian | WoW Warrior | `#C69B6D` | `0xFFC69B6D` |
| Priest | **Cleric** | Templar · Inquisitor | WoW Priest | `#FFFFFF` | `0xFFFFFFFF` |
| Priest | **Druid** | Warden · Fury | WoW Druid | `#FF7C0A` | `0xFFFF7C0A` |
| Priest | **Shaman** | Mystic · Defiler | WoW Shaman | `#0070DD` | `0xFF0070DD` |
| Scout | **Rogue** | Swashbuckler · Brigand | WoW Rogue | `#FFF468` | `0xFFFFF468` |
| Scout | **Bard** | Troubador · Dirge | *invented — indigo* | `#6C3FB5` | `0xFF6C3FB5` |
| Scout | **Predator** | Ranger · Assassin | WoW Hunter | `#AAD372` | `0xFFAAD372` |
| Mage | **Sorcerer** | Wizard · Warlock | WoW Mage | `#3FC7EB` | `0xFF3FC7EB` |
| Mage | **Summoner** | Conjuror · Necromancer | WoW Warlock | `#8788EE` | `0xFF8788EE` |
| Mage | **Enchanter** | Illusionist · Coercer | WoW Evoker | `#33937F` | `0xFF33937F` |

**Unknown / not-yet-inferred → neutral grey `#8B93A3` / `0xFF8B93A3`** (the existing
`MeterFamilyColors` fallback). Distinct from Cleric-white by luminance; a class snaps from
grey to its color once a signature lands.

*Off-pattern classes* (later additions outside the symmetric tree): **Beastlord** (Scout) and
**Channeler** (Priest). Not assigned; fold-or-omit is a §Still-open decision.

## Rendering rules that fell out

- **Global name text-outline** (a dark 1–2px shadow on the row name — the trick Details!
  ships on by default). Keeps the name legible over light fills; without it, white text on
  Cleric-white is unreadable. Applies to all rows, not just Cleric.
- **Minimum-alpha floor on the class fill** (filed, not yet specced): the meter fill-opacity
  is a user knob; a floor keeps class identity from dissolving into the grey backplate when a
  user cranks opacity low. Tested against the real default (fill alpha 200/255 over backplate
  `rgb(18,20,26)`); the two brightness extremes (Cleric-white, Warrior-tan) degrade fastest.
- **The unknown-grey is ours to tune** — if a future cool color ever risks colliding with it,
  shifting the unknown tone is a valid lever (noted while eliminating a steel Crusader).
- Data model note for when this lands: DCJS skips field initializers on deserialize, so if any
  of this becomes an enum knob, the default must be the 0-value (see repo CLAUDE.md).

## Where it plugs in (forward pointer, not a plan)

Row fill is resolved in one place today — `MeterEngine.cs:101`, inside the per-row loop:
`row.FillArgb = MeterFamilyColors.ArgbFor(metric.Category)`. `row.Name` is already in scope.
Class colors are a **new resolver keyed by inferred subclass** dropped at that seam — a
successor to `MeterFamilyColors` (which calls itself "the interim color model, pending
row-color-by-class"). The same call feeds `BreakdownEngine`, `DeathsEngine`,
`DeathRecapEngine`, and the `MeterPopup` headers — so *which surfaces* the class color reaches
is a real design question (§Still open), not automatic.

## Provenance

- **EQ2 class tree** (4→12→24) — Allakhazam `EQ2_Archetype_Tree`, Fanbyte/ZAM Classes page
  (two independent sources agree).
- **WoW class colors** — Warcraft Wiki `RAID_CLASS_COLORS` / `ChrClasses.db2` (current retail).
- **Details! render grounding** — `Tercioo/Details-Damage-Meter@master`: class colors live in
  an overridable `Details.class_colors` table (defaults == Blizzard's, untuned); rows are
  StatusBars tinted at full alpha; legibility handled by a **text outline**, not by clamping
  bar brightness; per-class override UI + right-click reset. (The overridable-table + reset
  pattern is worth copying when this becomes code.)
- **EQ2's own color convention** — the Heroic Opportunity archetype palette (Fighter blue /
  Priest yellow / Scout green / Mage red) exists but is *archetype*-level only and weak as
  recognition; **not used** here (we went per-subclass, identity-first).
- Interactive swatch mockups from the brainstorm persist in `.superpowers/brainstorm/`
  (gitignored).

## Still open (not designed — future phases, Alex-directed)

- **Ability-signature inference** — the actual mechanism to infer a combatant's class from the
  log (ACT exposes no class field). The real labor; mined from Alex's raid logs.
- **Two-file data model** — shipped `ability→class` ruleset (shared) vs. local self-building
  `name→class` cache (per-user). Betrayal/staleness policy (name→class isn't write-once).
- **Scope of application** — main rows only, or drill / recap / popup too.
- **Data granularity** — infer to final class (24) and roll up to subclass color, or stop at 12.
- **Render/config work** — the resolver swap, the text-outline, the min-alpha floor, any
  per-class override UI.
