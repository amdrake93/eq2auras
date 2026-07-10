# Session chronicle: 2026-07-06 → 2026-07-09 — the raid logs, the decompile, and the death of soonest-governs

*The story between the docs. What exists is in SPEC/backlog/act-timer-engine; this is how it happened and why.*

## The arc

Started as "time for fun" (a lunch-break promise carried across a context wipe); became the most consequential engine work since Phase 0. First real raid capture (2026-07-05, Mayong raid, ferried as `spike-data/2026-07-05/` — 17 MB overlay JSONL + 96 MB game log) → analysis found the **LATE-survives-refire bug 51×** (Alex had seen it live: "timers not properly resetting to their list if already LATE") → ACT decompiled to explain it → **soonest-governs was a wrong inference from a single-master test** → newest-master rule specced, shipped, and field-verified within 48 hours. Three releases: v0.1.90 (newest-master), v0.1.92 (debug mode + retention), v0.1.95 (grow-up ordering).

## Why-chains worth remembering

- **The bug wasn't a state machine.** Every layer (tracker, list builder, WPF windows, probe) is stateless; the "stuck LATE" was the soonest-governs rule faithfully displaying an overdue corpse instance that ACT keeps for `|RemoveValue|` seconds after a recast. The data showed paired polls (−9 and 35 at the same tick) — two instances coexisting, wrong one governing.
- **The decompile changed everything.** `ilspycmd` on the vendored `ThirdParty/Advanced Combat Tracker.exe` (now a repeatable recipe in `docs/act-timer-engine.md`) revealed the **per-instance `MasterTimer` flag**: 2s dedup, 12s tick window, largest-master-only native display, silent instance purge vs no-master frame kill. Both July-5 "purge modes" turned out to be one deterministic rule. Two July-1 findings formally superseded (soonest-governs; "RemoveValue doesn't govern").
- **Alex supplied the semantic key**: "we've always used it for cooldown tracking… the ability hit, therefore its cooldown has reset." Every trigger falsifies every older prediction → **newest master governs, non-masters invisible** ("master timer dictates all, let ACT do the work"). His skepticism drove two refinements: newest-vs-max only diverges via timer mods ("unless we invent time travel" — mods ARE the time travel, and same-frame 49s/73s instances were in his own raid data), and mods are pre-applied per instance gated by `Modable` — zero code on our side.
- **DoT tick spam needed no trigger reconfiguration.** The feared "ignore ticks" config work dissolved: ticks arrive flagged non-master and the new rule ignores them. `OnlyMasterTicks` checked would be the *wrong* direction (every tick a reset). Slow DoTs (>12s ticks) remain an ACT-inherited limitation — Absolute Timing or tighter regex, config-side.

## Process evolution (fresh in this session)

- **Alex dictated the full flow explicitly** (2026-07-08): branch → spec → review loop (reviewer + Alex gate) → plan → review loop (**reviewer-only gate**; Alex surfaces findings but doesn't gate plans) → implement → CI sanity push → merge (his call) → live test with assistance; field problems resurrect the same branch through the same process. Ran three full cycles this session; every review found something real (doc-governance trap, mispredicted TDD red phase, phantom record types in SPEC).
- **CLAUDE.md trimmed to workflow-only** (his call): engine truths live in SPEC + `act-timer-engine.md`; CLAUDE.md points, never restates. The read-first list now names the engine doc as ground truth over spike-findings (which carries a supersession banner).
- **The double-click discovery** (Alex's, mid-testing): double-clicking a timer in ACT's native window calls `NotifySpell` directly — a real engine trigger. Made the whole merge-gate script solo-runnable; raid-gated verification shrank to "non-master suppression at scale." Gotcha that bit us: the 2s/12s gates run on ACT's **log-driven clock** — idle-log testing needs `/say` chatter or clicks get deduped. Alex derived the window-extension rule (any instance re-arms the 12s window) live from first principles.
- **Fix-flow used once** (grow-up ordering): spec sentence + code, no plan doc, reviewer pass before merge. Worked clean; the reviewer's only finding was a scope *question* (center zone), answered and recorded in SPEC rather than coded — "the grow knob governs window geometry, not element order; per-window ordering belongs to the element/group arc."
- **QoL decisions** (debug mode + retention): "debug mode" naming his; feature-enable knob **dropped** (ACT's plugin checkbox is that switch); retention = rolling 14d/200 MB, newest survives the size rule, **debug-on suppresses the sweep entirely** ("you have intentionally decided you need excessive logging"). Retention decision logic went in Core as a pure function purely for Mac testability.

## Standing at close

- **Sunday 2026-07-12 raid**: Debug mode ON before first pull; ferry JSONLs to `spike-data/2026-07-12/`; verification = the July-5 analysis inverted (notify-while-LATE incidents should show same-tick governance flips; Blanket's tick stream should be invisible).
- Backlog top after that: LATE-cap/end-of-fight-pileup design think; tab UI cleanup; release channels; the element/group arc (WeakAuras research first).
- **Next session pivots hard**: a "meter" plugin (the Parse Meter direction — per the grid-session decision, a module in the SAME dll, not a sibling). Alex brings details after the context clear.
