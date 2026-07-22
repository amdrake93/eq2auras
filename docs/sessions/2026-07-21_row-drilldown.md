# Session chronicle — Row drill-down, and the 1.1.0 launch

*2026-07-21. Goal: build the backlog's DEFERRED #2 — row drill-down (click a combatant → detail for that combatant). Ran **full-autonomous** end to end again — brainstorm → spec (reviewed to closure) → plan (reviewed to closure) → implement inline → merge → promote — surfacing to Alex only at the two human gates (design, merge/promote). Shipped design-to-public in one session: **`stable` went 1.0.33 → 1.1.0** (the first **MINOR** bump — the 1.0.x line is closed). Two stories worth keeping: how the brainstorm kept **carving future features off the v1 surface** rather than into it, and the **drill-request channel** — the one genuinely new bit of architecture, forced by where ACT's data can be touched.*

## What shipped (stable 1.1.0)

- **Left-click a combatant row → its by-ability breakdown** of the window's current primary metric, swapped into the window body in place. Each ability shows its value and its **percent of that combatant's own total** (the abilities sum to 100% of *its* number, not the encounter's). **Right-click = up one layer** (list → popup, drill → back — context-sensitive). Header while drilled: `‹ Name — metric` (back-hint chevron), right-cluster total = that combatant's own number, secondary cell hidden. Live refresh + **auto-exit** when the drilled combatant leaves the scope-filtered population. Drill state transient (reload → list mode).
- **Core (strict TDD, 240 green):** `MetricBreakdownSource` enum + `MetricDef.BreakdownSource`; a **surface-agnostic `BreakdownEngine`** (`(label, value)` list → ranked `MeterRow`s, reusing the row DTO); `MeterEngine.DurationSeconds` promoted public so the engine shares the one duration policy.
- **Plugin (transcribe-only, CI-compiled):** `EncounterProbe` deep-reads **one** drilled combatant's `AttackType`s under the lock; a host-published **drill-request channel**; `MeterWindow` drill state + gestures; `OverlayHost` list-vs-drill routing + auto-exit. Timers untouched (only `MeterRowVisual` gained a `CurrentName` field).

## The brainstorm carved features *off* the surface, not into it

The whole design conversation trended one way: every time a richer capability came up, Alex **reserved it to a future surface** instead of loading the v1 drill view. The result is a deliberately thin v1 with clean seams, not a feature-full first cut.

1. **By-ability only, but the framework holds more.** First question was the breakdown *dimension*. Alex picked by-ability alone for v1 — but explicitly "framework holds more." The asymmetry justified it: by-ability is a *read* (ACT pre-aggregates each `AttackType`), by-target needs a raw-swing grouping. So v1 ships the cheap dimension and the DTO/engine stay general.

2. **Right-click is "back," and click-away is *wrong*.** I offered three back-nav options (header breadcrumb, pinned summary row, right-click/click-away). Alex: *"just right click anywhere will go back, click away doesn't make sense since it's not a popup… click away usually closes popups."* That's a sharp bit of interaction taste — it made right-click **context-sensitive** (a deliberate change to the "right-click always opens the popup" invariant) and kept the drill body honestly *not* a popup.

3. **The secondary-stats question spawned a whole new feature.** I asked whether the configured secondary carries into the detail. Alex reasoned through it live: drop the secondary (a scope-free HPS means nothing per damage-ability) — *but* he liked showing per-ability crit%/maxhit… then caught himself: *"this is going to introduce a labeling problem and I have a better idea for this data to implement later. Skip all of this."* Those stats became a **new backlog item** — a *separate* per-ability detail window (left-clicking an ability, reserved no-op in v1) where they get proper labeling that a column spanning seven metrics can't give them.

4. **By-target is a mouseover, not a toggle.** Late in the design Alex reserved the *other* breakdown dimension to **hover**: mousing a combatant row → its by-target breakdown; mousing an ability row → that ability's by-target. This refined my "framework holds N dimensions as a later *toggle*" into "by-target is a hover *surface*." Consequence: the drill view itself stays single-dimension, and all three reserved seams (ability-detail window, by-target at two levels) **reuse the same `BreakdownEngine`**, fed a differently-grouped list — building it now for by-ability is what makes them additive, not speculative.

The through-line: the owner keeps the shipped surface small and pushes richness onto *new* surfaces with their own seams. My instinct was to add columns/toggles to the one view; his was to keep the view honest and open a new window/gesture for each new kind of detail.

## The one new bit of architecture: the drill-request channel

Everything else reused the existing pipeline. Drill-down needed one genuinely new thing, and it was forced by **where ACT's data can be touched**:

- `UpdateMeterSample` only ever received the shallow `CombatantReading` list (per-combatant totals). The per-ability `AttackType`s live on ACT's `CombatantData`, which is only safe to read **under `AfterCombatActionDataLock` on ACT's UI thread** — i.e. inside `EncounterProbe`, not in the host (a different STA thread).
- So the drilled window's identity has to travel *back* to the probe. Solution: the host publishes a **volatile drill-request snapshot** (`CurrentDrillRequests()`), rebuilt on the STA thread whenever a window enters/leaves drill; the probe reads it lock-free at the top of each poll and, still under the lock, deep-reads **one** `CombatantData` per request into a Core `BreakdownReading`. One combatant, on demand — never a per-combatant fan-out.

Two decouplings fell out that kept it clean:
- **Rate ÷ duration lives in Core `BreakdownEngine`, not the probe.** The probe emits raw per-ability values; the engine divides (for rate metrics) exactly as `MeterEngine` does — so per-ability values sum to the combatant's own total, and percent is duration-independent.
- **The header's own-total comes free from the list frame's row** (`FormattedValue`), so it's correct the instant you click, before the first breakdown even arrives. And **auto-exit** keys on the drilled name's absence from the *scope-filtered* `listFrame.Rows` — the population filter is already the reset detector, no extra scope logic.

## Review loops caught real things

- **Spec, 2 rounds.** R1 Minor: the "header while drilled" prose re-specified the total and cog cells but went **silent on the secondary-label cell** — for a window with a secondary set, that left an orphan label above the drill body's absent secondary column, breaking §Header's "cells mirror the columns 1:1" invariant. Pinned it to hide while drilled. R2 closure.
- **Plan, 2 rounds.** R1 **Important**: my Task 2 said `MeterEngine.EncounterDuration` had "one caller" and only updated it — but it has **two**, and the second sits in the cleared-primary early-return branch (`:24`). A literal transcription would rename the definition and leave `:24` referencing a nonexistent method — a **compile error**, caught before a line was written. Plus two Minors (read the drill-request snapshot *before* the lock, matching the prose; a transposed cures/powerheal line-number citation) and two nits. R2 closure. The reviewer's round-1 "what checks out" independently re-derived every ACT-API claim against the vendored parser — the bucket alias-statics, the uppercase key lookup, the "All"-skip idiom — so the single Important finding was a real catch, not noise.

## Data path — pinned to the vendored source, one field-gate

Every metric→bucket mapping was verified against `ThirdParty/ACT_English_Parser.cs:2082-2088` (the EQ2 `SetupEQ2EnglishEnvironment` alias statics): `encdps`→Outgoing Damage, `damagetaken`→Incoming Damage, `enchps`/`totalhealing`→Healed (Out), `healstaken`→Healed (Inc), `powerheal`→Power Replenish (Out), `cures`→Cure/Dispel (Out). Per-`AttackType` value is `at.Damage` (the positive-Dnum sum) for damage/heal/power; **`at.Swings`** for the lone count metric, cures. That cures accessor is the one thing the vendored source couldn't fully confirm (it's an ACT-core computation, not in the parser) — flagged as the on-box field-gate item. The spec deliberately didn't assert cures/powerheal buckets; the *plan* pinned them, verified in code, and the on-box script confirms cures reads sensibly.

## The 1.1.0 launch — first MINOR bump

Merged `meter-row-drilldown` → `main` → `dev-latest 1.0.47`. Alex pulled it, then: *"promote that, we'll call it 1.1.0."* That's a **MINOR** bump per the versioning scheme (enough new functionality in an existing product). Mechanics: a direct `version.txt` `1.0` → `1.1` commit on `main` — HEAD at its own build, so `PATCH = 0` → **`dev-latest 1.1.0`** — then `gh workflow run promote` promoted the exact bytes to **`stable 1.1.0`**. First MINOR bump; the `1.0.x` line is closed. (Same shape as the 1.0.0 launch: version.txt commit builds the new number, promote ships it.)

**One honest wrinkle:** I promoted the `1.1.0` *rebuild* without an independent on-box verify of it specifically — it's byte-identical in code to the `1.0.47` Alex pulled (the bump only re-stamps the number), and he made the explicit promote call. Cures + auto-exit remain worth an on-box glance; anything off is a fix-flow follow-up.

## State & what's next

- **`stable` = `dev-latest` = `1.1.0`.** Public default now carries row drill-down. Branch `meter-row-drilldown` merged (kept per Alex unless he says otherwise).
- **NEXT (backlog): the drill-down reserved seams** — the **per-ability detail window** (left-click an ability → crit%/maxhit/hits with proper labeling; the left-click is already a reserved no-op) and **by-target breakdowns on mouseover** (combatant + ability levels; ability-level feasibility TBV). Both reuse the same `BreakdownEngine`. Still queued behind them: player-facing docs/wiki, **class colors** (the BIG effort — ability-signature inference), crit%/accuracy (a percentage *kind*, overlapping the ability-detail window), derived/event metrics (deaths, special avoidance — DEFERRED #3).
