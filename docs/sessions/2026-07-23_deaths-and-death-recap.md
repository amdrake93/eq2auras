# Session chronicle — Death & Death Recap (the first special metric)

*2026-07-22/23. Goal: build the QUEUED-NEXT special-metrics arc's first item — **Deaths** (a per-combatant death count as a row) and **Death Recap** (drill a death → what killed them). Ran the full pipeline with per-gate review: brainstorm → spec (reviewed to closure) → an owner-gated **rollback verification** → plan (reviewed to closure) → implement both phases inline → merge → **dev-latest 1.1.29** — then **three field fix-flows** the same day as Alex tested on the box. Left `stable` at 1.1.0; the MINOR bump to 1.2.0 waits until Alex is happy. Stories worth keeping: how "just like Details" turned into **cloning Details' actual source** after a redirect; how Deaths forced the meter's **first new metric *kind* and first new drill *surface***; the **health bar EQ2's log can't give us** (reconstructed, and why that's honest); the **rollback gate** that never fired; and how three field bugs all traced back to the same root — **ACT's data model, not our code**.*

## What shipped (dev-latest 1.1.29)

- **Deaths** — the registry's first **special (event) metric** (Damage category → red, Allies-only). Its list is a **chronological death timeline**: one row per death event (a combatant who dies twice → two rows), each `name (N) · killing-blow ability + dmg` on the left, **time-of-death** as the value, a red bar filling to **how far into the fight** the death fell. Header total = the death count (sum of the listed allies' `Deaths`).
- **Death Recap** — left-click a death → the body swaps to a **1-second-resolution health track** over the last 10s before death: each active second `−Ns · dmg (red) · heals (green) · hp%`, the row fill a **draining health bar**. Right-click → back. Live-refresh + auto-exit, same as the by-ability drill.
- **Core (strict TDD, 265 green):** `DeathsEngine` (event-timeline path), `DeathRecapEngine` (per-second bucketing + backward-from-0 health reconstruction + clamp), `DeathRecord`/`RecapEvent`/`RecapReading` DTOs, `Mmss`/`SignedAbbreviate` formatters, `MetricDef.IsEvent`, the `deaths` registry entry + selection, and shared-row extensions (`MeterRow.Detail`/`DrillKey`, `SecondaryValue.Argb`).
- **Plugin (transcribe-only, CI-compiled):** `EncounterProbe` poll-only death capture + the recap deep-read; `MeterWindow`/`MeterRowVisual`/`OverlayHost` rendering (two-tone killing-blow label, N colored recap columns, death-drill routing + auto-exit); the shared **middot** drill delimiter (also retired the by-ability header's em-dash). Timers untouched (`BarRowVisual` byte-identical; all changes in `MeterRowVisual`/Core).

## "Just like Details" meant *read Details*, not remember it

Alex opened with a clear picture: Deaths + Death Recap "just like Details." I started asking design questions off my *memory* of Details and jumped to an options screen — proposing a "timeline-position bar fill + percent" for the deaths list. Alex stopped me: *"I want to dive into how Details presents their deaths and death recap, think we skipped that part which I did bring up for a reason."*

Details is open source. Cloning it and tracing the actual code (`functions/parser.lua`, `class_utility.lua`) corrected me on the spot: the deaths **list** uses a **flat full-width decorative bar, no percent** (`SetValue(100)`, class-colored) — my proposed proportional-fill-with-percent was invented, not observed. And it confirmed the load-bearing fact for the whole feature: Details stamps **per-event HP** from WoW's live `UnitHealth` API — a thing **EQ2's log does not carry**. This became a standing lesson ([[ground-in-cited-references-first]]): when Alex cites a reference, study its real behavior *before* proposing options; he cites deliberately, and the reference holds the answer.

(Ironically Alex then *chose* a proportional into-fight bar for the list anyway — but as a considered choice against the known Details baseline, not a guess. See the third fix-flow for how that bar bit back.)

## Deaths broke the metric mold — twice

Every metric before this was a **scalar**: `select: CombatantReading → double`, rank combatants by it. Deaths doesn't fit on either end, and naming that early shaped the whole design:

- **The list is an event timeline, not a ranking.** Rows are death *events* ordered by time; the row value is a *timestamp*, not a magnitude. So it bypasses `MeterEngine.Tick`'s population/rank flow for a dedicated `DeathsEngine`.
- **The recap is a new drill surface, not a breakdown.** The shipped `BreakdownEngine` produces a *ranked contribution list* (abilities by %). A recap is a *chronological narrative with a reconstructed HP bar* — a different shape. So `DeathRecapEngine`, not `BreakdownEngine`.

This resolved the open question the backlog had carried for the whole special-metrics arc — *"is a recap the same shape as a breakdown, or a new surface?"* — **new surface.** `MetricDef.IsEvent` is the one-bit flag that routes an event metric down its own path; the seven scalars are untouched (the flag defaults false, so their call sites didn't even change).

An interceding subagent-analysis wrinkle worth recording: the internals-mapping agent, having surveyed the code, closed its report by recommending Deaths ship as *"just a count metric fitting `Tick`, recap fits `BreakdownEngine`"* — exactly the mold the spec had already rejected. Easy to have followed a confident-sounding agent summary; the spec was the guard. Its *cited internals* were gold; its *design opinion* contradicted the approved design and got discarded.

## The health bar EQ2 can't give us — reconstructed, and why that's honest

The recap's draining HP bar is the heart of the feature, and EQ2's log carries **no health** — confirmed by grepping every `MasterSwing`/`CombatantData` field. So we reconstruct. The design fork (Alex's call): walk **backward from 0-at-death** through each second's net (`heals − damage`), express as a fraction of `CombatantData.GetMaxHealth()` — ACT's own running-min-of-(heals−damage) estimate — clamped `[0,100]%`. This is precisely the fallback **Details itself** uses when its live-HP API is unavailable, so we're not inventing a hack; we're doing what the gold standard does when it can't read HP either.

Why *backward* from death, not *forward* from full: forward-from-`GetMaxHealth` assumes the victim was at full HP at the window's start — usually false (a tank enters the window already hurt), an **unbounded** error. Backward anchors on the one thing we *know* — dead = 0 — so its error is bounded to a single constant (below).

The overkill consequence, which Alex reasoned through himself at the end: EQ2 logs no overkill, and the 0-anchor counts the killing blow's *full* damage, so every reconstructed HP above the death is inflated by that one overkill constant `K`. His insight: *"that error would be consistent to each row so the RELATIVE % of the bars does tell the correct story, the raw health number will just be a bit off."* Exactly right — which is why the recap shows the **bar + hp%** (the honest relative story) and the third recap iteration **dropped the raw absolute-health number** (the one false-precise element). `GetMaxHealth()` stays as the bar's denominator.

## Poll-only capture — Alex talked himself out of my event subscription

For "notice a death and go find the killing blow," I floated subscribing to `AfterCombatAction` (fires per swing, under the lock, at the death instant). Alex interrogated it — *"is it smart to introduce multiple subscriptions against the same stream…"* — and I had to correct a premise I'd muddled: the meter has **no** subscription today, it's poll-only; this would be its *first*. That reframed the choice, and Alex landed it: *"I need a lot of value to introduce a full blown subscription processor… I can't think of anything that could possibly need it."*

So capture stays **poll-only**: each poll reads every ally's `CombatantData.Deaths`; a count **increase** vs. the prior poll triggers a **bounded killing-blow scan** for that one victim, cached in a small death-record store (reset on encounter change). Same "scan only on a death" property he wanted to protect, **zero** new infrastructure — no handler on ACT's hottest thread, no cross-thread state. The one cost (noticing a death up to a poll-interval late) is immaterial for a recap. Recorded in the spec as considered-and-rejected so it doesn't get re-litigated.

## The rollback gate that never fired

Alex attached an explicit condition to the plan phase: the plan **must give a verified answer** on whether `CombatantData.Deaths` is cheap to read every poll — and *"if it becomes a legit concern we'll need to rethink some stuff… that plan time verification flag could roll our spec back."* A real gate, not a footnote: the whole poll-only capture rests on that read being cheap.

Re-derived it from the actual binary (fetched ACT 3.8.5.288 exactly as CI does, decompiled `CombatantData`). The getter is **boolean-cached** (`deathsCached`/`cDeaths`), and the cache is invalidated **only when a Death swing lands** (`AddReverseCombatAction`, inside `if (action.Damage == Dnum.Death)`) — ordinary damage/heal swings don't touch it. So it's **O(1) on virtually every poll**; the one post-death recompute takes the O(1) Killing-bucket-count path, not the linear fallback. Gate **cleared** — no rollback. Bonus: the getter confirmed deaths live in the incoming `AllInc["Killing"]` bucket, which grounded the killing-blow read.

## Both review loops caught real things (2 rounds each)

- **Spec, r1 Important:** I'd written the header total as `EncounterData.AlliedDeaths`. The reviewer proved `AlliedDeaths` counts **one-word player names only** (decompile `:120`) while the rows come from the broader `GetAllies()` population — so a multi-word ally's death would row *without* incrementing the total, breaking my own "total = row count" claim. Fixed to **sum the listed allies' `Deaths`** (= row count by construction), which is also what Alex actually asked for ("deaths for the combatants on the list").
- **Plan, r1 two Important:** both were **wrong test literals in my plan** (not impl flaws): (1) `Abbreviate(1000)` yields `"1K"` not `"1.0K"` (the `"0.##"` format drops trailing zeros) — my `+1.0K` assertions would fail against the shipped formatter; (2) my recap first-row HP expectation (0.375) contradicted my *own* reconstruction formula (0.625). Corrected the literals + folded in the DRY nit (one `Mmss` formatter, `FormatDuration` delegates to it).

## Three field fix-flows — all rooted in ACT's data model, not our code

Alex tested on the box and found three issues, each its own reviewed fix-flow. The through-line: the *code* was faithful; **ACT's data shapes** were the surprise.

1. **Deaths-as-secondary crashed (NRE).** The popup's secondary grid was built from `MetricRegistry.All` with no event-metric filter, so Deaths (with its **null `Select`**) was selectable — and `MeterEngine.Tick` called that null selector every poll. Alex spotted it by asking *"what would deaths-as-a-secondary even show?"*, then confirmed the crash on the box. Fix: **event metrics are primary-only** — guard `Tick` (event-metric secondary → "none") *and* drop them from the picker. (This one's our bug, but it surfaced only because an event metric is a shape the secondary path never anticipated.) Also noted: it crashed on the **dispatcher thread**, which `EncounterProbe`'s try/catch doesn't cover — a data point for the backlogged crash-detection idea.

2. **Recap raw-health was false-precise → dropped.** Per the overkill reasoning above; also made a no-event second show a colored **`0`** (green/red) instead of a dash — *"0 healing that second"* is the point of a recap.

3. **Death-list bar went colorless.** Alex: the list bar "lost its color." It hadn't — the fill *was* red, but its **length** was the into-fight fraction, and testing solo (die without hitting anything) gave **encounter duration 0**, so the fraction was `0` → empty bar. Root cause is pure ACT: **encounter `Duration` keys on ally *outgoing* damage** (`EndTime` = last ally `ShortEndTime`), so a solo pre-engage death has no duration. Alex's first instinct was to drop the proportional bar for a full-red identity (option A, which I built) — then he course-corrected: *"wait, I like the % fill showing the timeline… just make a 0ed denominator == 100% fill."* So the shipped fix keeps the timeline bar and maps the degenerate `0` denominator to a full bar. (I committed option A prematurely with a failing test in the tree — caught it, reverted to the one-line C fix, re-verified before re-committing. Lesson re-learned: run the suite *before* `git commit`, not after.)

## Backlog captured (not built)

- **Buff tracking** — silent buffs have no log line, so a **standardized `eq2auras <ability> <target>` macro** raiders opt into → ACT capture groups pull ability+target → a predefined tracked-buff list spawns rows in **our own buff window**. The suite starting to own its own timer config, for what ACT won't do. Investigate.
- **In-plugin crash detection that phones home** — a local crash record is easy; the ask is *reaching Alex* without the user reporting. Options (Discord webhook / hosted intake / GH-issue-via-proxy) all hit public-repo secret exposure + it's-other-people's-machines constraints (opt-in, PII-minimal). Investigate.

## Where it stands

`dev-latest 1.1.29`, `stable 1.1.0`. Deaths + Death Recap + three fix-flows all merged and reviewer-approved; Alex's verdict: *"good enough. We can iterate if people complain."* The **1.2.0 MINOR bump + stable promote** is deferred until he's satisfied — the mechanism is ready (a `version.txt` `1.1`→`1.2` commit builds `1.2.0`, then promote the exact bytes). **Next session: the mouseover UI** — the reserved per-second recap breakdown and the by-target hovers — building the hover *surface* so data can be fed into it.
