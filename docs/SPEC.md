# eq2auras — Design Spec

**A personal ACT overlay suite for EverQuest 2.** This document is the source of truth for what eq2auras is and how it works. It is organized as: the suite vision and architecture (Part I), then the Phase 1 feature — the Timer Overlay — in full (Part II), then cross-cutting concerns, unknowns, and the roadmap (Part III).

Status: **pre-implementation design.** No code exists yet. Present-tense descriptions below describe the intended system; anything not yet built is scoped to a phase.

---

## Part I — The Suite

### Vision

The north star is *"WeakAuras for EQ2"*: a framework that lets a player build configurable, good-looking combat overlays, hosted in one place. WeakAuras is beloved because it is a *construction kit* — many "auras" on one engine — not a fixed window. EQ2 has nothing comparable. eq2auras aims there, built incrementally.

### The one hard constraint: ACT owns the data

eq2auras is an **ACT plugin that reads ACT's live data and renders its own UI**. It does **not** reimplement triggers, timers, or log parsing. Triggers and spell timers stay in ACT's native framework, which matters because the team already shares timer configurations through ACT's built-in sharing system — a teammate who does not run eq2auras still receives and uses those timers normally. eq2auras owns *presentation only*.

### Architecture: shared core + feature modules

The suite is deliberately layered so that each new overlay idea is a module on common plumbing, not a new project:

- **Core (reusable, feature-agnostic):**
  - Overlay window framework — a transparent, top-most, click-through window.
  - The render loop.
  - Rendering primitives — bars, text, radial/pie, free positioning — and the escalation/conditions engine that maps *state → appearance*.
  - Configuration.
  - Diagnostic logging.
- **ACT data adapters (feature-specific, thin):** small layers that pull from ACT and normalize into the core's model. The *timer adapter* (Phase 1) reads `FormSpellTimers.GetTimerFrames()`; a future *encounter adapter* reads combatant/DPS data.
- **Feature modules:** built on the core.
  - **Timer Overlay** — Phase 1 (this spec, Part II).
  - **Parse Meter** — future (replacement for ACT's "mini parse" names/DPS window). Different data source, same core plumbing — this module is the reason the core is kept feature-agnostic.

### Packaging

Ships as a **single ACT plugin** — one `.NET Framework 4.x` `.dll` dropped into ACT's `Plugins` folder. Features are individually toggleable, so a teammate can install the package and enable only what they want. Splitting into sibling plugin DLLs later (for fault isolation or standalone sharing) remains possible because the core is a separate library; it is explicitly **not** required for Phase 1.

### Platform facts

- **.NET Framework 4.x** class library. ACT is a .NET Framework host; .NET Core / 5+ assemblies will not load.
- Plugin entry point: `Advanced_Combat_Tracker.IActPluginV1` (`InitPlugin(TabPage, Label)` / `DeInitPlugin()`).
- The overlay renders over **borderless-windowed** EQ2. It cannot draw over true exclusive-fullscreen Direct3D — a documented limitation that applies to ACT's own overlays too, and rarely a problem in practice (Win10+ usually runs "fullscreen" as borderless).

---

## Part II — Phase 1: The Timer Overlay

### Goal

Replace ACT's cramped, static spell-timer window with a **calm, glanceable list of upcoming timers that escalates each timer as it approaches its moment** — so salience tracks urgency instead of everything looking the same. This is the concrete pain being solved: ACT's window treats a 40-seconds-away timer identically to a 3-seconds-away one, in the same cramped spot off to the side.

Phase 1 ships the **general engine with a single baked-in preset** (constants), not a configuration editor. The escalation behavior is expressed as condition values that happen to be fixed constants for now; adding configuration later is *exposing knobs that already exist internally*, not a rewrite.

### The core loop

Everything is one loop, run on a render tick (target ~15–30 fps):

> **read timer data → diff against tracked state → update visuals (and emit diagnostics)**

Each tick reads `ActGlobals.oFormSpellTimers.GetTimerFrames()` (a `List<TimerFrame>`) for a smooth live countdown. ACT's lifecycle events (`OnSpellTimerNotify` / `Warning` / `Expire` / `Removed`, each handing us the whole `TimerFrame`) are available and may be used for precise transition moments, but polling covers Phase 1.

### Data read from ACT (per timer, per tick)

From each `TimerFrame` and its `TimerData` definition + live `SpellTimer` instances:

| Field | Source | Use |
|---|---|---|
| `Name` | `TimerFrame.Name` / `TimerData.Name` | Label |
| caster | `TimerFrame.Combatant` | Disambiguation / per-target |
| `TimeLeft` | `SpellTimer.TimeLeft` (seconds; live) | The countdown; drives state |
| `TimerVals` | `TimerFrame.TimerVals` (sorted `int[]`) | Convenient pre-sorted remaining values |
| `WarningValue` | `TimerData.WarningValue` (seconds) | **The escalation threshold** — "how many seconds left is a warning" |
| `TimerValue` / `TimerFinalDuration` | def / instance | Total duration (post-mods) |
| `FillColor` | `TimerData.FillColor` | Element color (the only color ACT exposes) |
| `Category` | `TimerData.Category` | Grouping (future) |
| `RadialDisplay` | `TimerData` / `TimerFrame` | ACT's bar-vs-radial preference |

ACT exposes **no per-timer icon/image**, **no `CategoryData` object** (category is a bare string), and **no separate warning color** (only the threshold + an optional warning sound). Icons and per-category theming are therefore owned by eq2auras in later phases, not inherited.

### Escalation is driven by ACT's `WarningValue`

We do **not** invent thresholds. Each timer already carries its own `WarningValue` (the team sets it per-timer in ACT). Escalation pivots on it. This keeps Phase 1 honest with the "configuration in mind" principle — the threshold is real data we read.

### The timer lifecycle (state model)

Because we must *hold onto* a timer past the point where ACT would drop it (see Overdue), eq2auras is **not** a stateless mirror of `GetTimerFrames()`. The overlay maintains a small **state model** keyed by timer identity across ticks. Each tracked timer is in one of three states:

1. **Calm** — `TimeLeft > WarningValue`. Shown as a row in the side **list**, auto-sorted soonest-to-expire, drawn as a horizontal bar (name + countdown + draining fill), colored by `FillColor`.
2. **Imminent** — `0 < TimeLeft ≤ WarningValue`. **Removed from the list** and promoted to a **big center radial pie** (escalation Model A: one focus at a time). See pie semantics below. Pulses.
3. **Overdue** — `TimeLeft ≤ 0`. The ability is *late* — we have lost a deterministic countdown, which is the scariest state. **Escalated further** beyond the pie (see Overdue visual). We **keep showing it even after ACT drops the frame at `RemoveValue`**.

**Transitions:**
- Calm → Imminent → Overdue as `TimeLeft` decreases.
- **Reset-to-full → Calm** is the *only* de-escalation path, and it is how ACT signals *the ability actually fired*. Detected either as (a) a tracked frame's `TimeLeft` jumping back up toward its full duration, or (b) a frame with the same identity reappearing at full duration after ACT had dropped it. A reset from *any* state returns the timer to Calm.

**Why the state model:** ACT will drop an overdue frame from `GetTimerFrames()` at `RemoveValue` (its "give up and remove" behavior). eq2auras overrides that — an overdue timer is held and escalated until a reset arrives, because "the tank buster is overdue and could land any second" is exactly what a player must not lose track of.

### The escalated radial pie — warning-window semantics

When a timer escalates to **Imminent**, the pie represents the **warning window, not the whole duration**:

- Fill fraction = `TimeLeft / WarningValue` (clamped to `[0,1]`).
- At the instant of escalation, `TimeLeft == WarningValue` → the pie is **full**; it drains to empty as `TimeLeft → 0`.

A 90s timer with a 10s warning escalates at 10s-left and gives a full, fast-draining pie for those last 10 seconds — rather than a barely-moving sliver of the whole 90s. The pie's motion is calibrated to the window that actually matters, which is easier to read at a glance. The pie shows a big seconds-left number and the timer name, tinted by `FillColor`.

### The Overdue visual

Since there is no meaningful countdown once `TimeLeft < 0`, the element flips to a **count-up "LATE +Ns"** state — empty/red, fast pulse, strong emphasis (candidate: screen-edge flash) — conveying *how overdue* the ability is and that timing is lost. It persists until reset-to-full (or an escape hatch fires — see Open Decisions). Exact styling is a tunable Phase 1 constant.

### Diagnostic logging (first-class Phase 1 feature)

Because the whole thing is one "read → diff → update" loop, tapping that loop yields a complete picture of ACT's behavior and our own. eq2auras writes **structured, timestamped diagnostics** (JSON-lines or CSV) capturing per-timer readings and — especially — every state transition (calm→imminent→overdue, resets, removals), each with `TimeLeft` / `WarningValue` / `RemoveValue`. It is toggleable so it is quiet in normal play. This log is both the mechanism for the verification spike (below) and a permanent debugging tool.

### Baked-in constants for Phase 1

These are the values that become configuration later. Phase 1 fixes them:
- List anchor position, size, orientation, and sort (soonest-to-expire).
- Bar styling (fill, font, colors derived from `FillColor`).
- Center-pie size and position; pulse animation parameters.
- Overdue visual (count-up styling, flash).
- Render tick rate.
- Fallback when a timer has no usable `WarningValue` (e.g. `0` or `≥` total) → a sane default so it still escalates.

### Explicitly out of scope for Phase 1

The configuration editor; per-timer / per-category customization; import/export sharing strings; element types beyond bar + radial; game icons/art; reading the combat log directly; multiple layout groups; the Parse Meter module. All are later phases on the same core.

---

## Part III — Cross-cutting

### Testing strategy

- **Verification spike is the first implementation task.** A barebones plugin that subscribes to the four `OnSpellTimer*` events and polls `GetTimerFrames()`, writing the diagnostic log described above. Run it against synthetic timers and a real fight to *observe* — not guess — exactly: when ACT drops a frame, what `TimeLeft` reads while negative, and what a reset looks like in the data. This locks the reset-detection and overdue-hold rules. The diagnostic-logging feature and this spike are the same code.
- **Synthetic timers for desk development.** We drive test timers without being in a raid — via ACT manual triggers and/or `FormSpellTimers.NotifySpell` — so the overlay can be developed and tuned at the desk.
- Standard unit tests for the state model / transition logic (pure functions over sequences of `(TimeLeft, WarningValue)` readings).

### Unverified items to confirm against a live ACT build

Flagged by API research as documented-but-unconfirmed; the spike resolves them:
- Exact runtime type of `SpellTimer.TimeLeft` (doc says "seconds"; confirm `double` vs `int`).
- Whether ACT exposes a distinct warning **color** or hardcodes the warning tint (only the threshold + sound are in the API).
- `RemoveValue` semantics precisely (counts from `0`? from `-RemoveValue`? when does `OnSpellTimerRemoved` fire?).
- Contents/keys of `SpellTimer.ExtraInfo` (where captured regex groups / per-target labels live).

The design tolerates whatever the spike finds because eq2auras owns its own state; the findings only tune *how* we distinguish "reset → calm" from "RemoveValue timeout → hold as overdue."

### Roadmap (later phases, same core)

1. **Timer Overlay Phase 1** — this spec.
2. **Open the knobs** — expose the baked-in constants as per-timer / per-category configuration; a config surface (config strings first, then an in-ACT editor with live preview).
3. **Sharing** — import/export configuration strings, so overlay layouts travel the way timers do today.
4. **Richer elements** — more display types (icon w/ cooldown swipe, plain text, alternate bar styles), an intermediate "approaching" visual tier, animations.
5. **Icons** — a name→icon mapping; possibly sourcing art from EQ2's own game files (never from live game memory).
6. **Parse Meter module** — replace ACT's "mini parse" window (combatant/DPS), on the same core via an encounter adapter.

### Open decisions

- **Overdue escape hatch.** If a timer goes overdue and the reset *never* comes (e.g. the mob died), the overdue element would linger forever. Candidate resolutions: clear overdue timers on encounter end / zone change (recommended — matches "the fight's over"); a max-overdue-linger constant; or both. To be settled with input from the spike (what ACT itself does around encounter end).
- **Multiple simultaneous Imminent timers** — how several big center pies arrange at once (stack, row, prioritize one). Minor; decide during Phase 1.
