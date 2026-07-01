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
- **Teardown matters.** ACT loads and *reloads* plugins at runtime, so `DeInitPlugin()` must stop the render loop, unsubscribe every ACT event, close the overlay window, and flush/close the diagnostic log. Leaked windows, timers, and event subscriptions are a known ACT-plugin failure class.

---

## Part II — Phase 1: The Timer Overlay

### Goal

Replace ACT's cramped, static spell-timer window with a **calm, glanceable list of upcoming timers that escalates each timer as it approaches its moment** — so salience tracks urgency instead of everything looking the same. This is the concrete pain being solved: ACT's window treats a 40-seconds-away timer identically to a 3-seconds-away one, in the same cramped spot off to the side.

Phase 1 ships the **general engine with a single baked-in preset** (constants), not a configuration editor. The escalation behavior is expressed as condition values that happen to be fixed constants for now; adding configuration later is *exposing knobs that already exist internally*, not a rewrite.

### The core loop

Everything is one loop, run on a render tick (target ~15–30 fps):

> **read timer data → derive each timer's state → update visuals (and emit diagnostics)**

Each tick reads `ActGlobals.oFormSpellTimers.GetTimerFrames()` (a `List<TimerFrame>`) for a smooth live countdown. ACT's lifecycle events (`OnSpellTimerNotify` / `Warning` / `Expire` / `Removed`, each handing us the whole `TimerFrame`) are available and may be used for precise transition moments, but polling covers Phase 1.

### Rendering technology

Drawing the overlay is the hard part, and the choice drives whether the pulse/flash effects that *are* the feature come out smooth. On .NET Framework 4.x the realistic options:

- **WPF layered window** *(recommended)* — a borderless `Window` with `AllowsTransparency=true` (per-pixel alpha), `Topmost=true`, and click-through added via `WS_EX_LAYERED`/`WS_EX_TRANSPARENT` on its `HwndSource`. WPF's retained-mode compositor gives smooth alpha-blended pulse/fade/scale animation essentially for free — exactly what the escalation visuals need — and it hosts fine as a standalone window launched from the WinForms ACT plugin.
- **Transparent WinForms + GDI+** — the most common ACT-overlay precedent (Triggernometry), but `TransparencyKey` is 1-bit (no true per-pixel alpha); smooth alpha animation requires a hand-composited `UpdateLayeredWindow` bitmap — more code than WPF for a worse result.
- **Direct2D/SharpDX or CefSharp (HTML/CSS)** — maximum control or full CSS animation respectively, but each adds a heavyweight dependency out of proportion to Phase 1 (CefSharp bundles ~100+ MB of Chromium).

**Decision: WPF layered window**, validated in the spike (which opens a window anyway — it renders a pulsing test element over the game to confirm transparency, click-through, always-on-top over borderless EQ2, and animation smoothness before any real UI is built). Fall back to WinForms + `UpdateLayeredWindow` only if WPF interop inside ACT proves troublesome.

**CEF/HTML is a deliberate future upgrade path, not just an alternative.** When the customization/authoring capabilities arrive (Phase 2+ — a config editor, per-element theming, import/export strings), web tech is the natural fit and the Chromium weight becomes justified. It is out of proportion for a Phase 1 timer overlay, so WPF starts; a later migration to CEF for the authoring era is anticipated, and the core's rendering-primitive boundary is meant to make that swap feasible.

### Concurrency with ACT's live data

`GetTimerFrames()` returns ACT's **live** `List<TimerFrame>`, mutated on ACT's own threads. Enumerating it from the render-loop thread risks an `InvalidOperationException` (*collection modified during enumeration*) — a classic ACT-plugin bug. Strategy: each tick, **snapshot** — copy the fields we need out of ACT's objects into our own immutable per-tick value objects as quickly as possible (defensively, tolerating concurrent mutation), then render exclusively from that snapshot; the render loop never touches ACT's live objects after the read. Whether the copy must be marshaled onto ACT's UI thread or guarded with a lock is one of the things the spike verifies against a live build.

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

**Escalation never relies on color alone.** `FillColor` is user-owned data — a timer may already be red or dark — so color is not a channel we control, and could even collide with the Overdue red. Escalation is always carried by **size, position (into the center zone), motion/pulse, and the LATE tag**; color rides on top as decoration, never as the signal.

### The timer lifecycle

Escalation state is derived from each timer's live `TimeLeft` versus its `WarningValue`, evaluated every tick. A timer is in one of three states:

1. **Calm** — `TimeLeft > WarningValue`. A row in the side **list**, auto-sorted soonest-to-expire, drawn as a horizontal bar (name + countdown + draining fill), colored by `FillColor`.
2. **Imminent** — `0 < TimeLeft ≤ WarningValue`. **Removed from the list** and promoted into the **center escalation zone** as a big radial pie (escalation Model A: escalated timers leave the list and move toward center). See the pie semantics and the zone layout below. Pulses.
3. **Overdue** — `TimeLeft ≤ 0`. The ability is *late* — a deterministic countdown is lost, the scariest state. Escalated further (see Overdue visual). It remains **only as long as ACT keeps the frame**; when ACT removes it at `RemoveValue` (its normal behavior), it disappears. **Phase 1 does not override ACT's removal** — the overdue element behaves as ACT does today, just louder.

**Transitions** follow `TimeLeft` directly: Calm → Imminent → Overdue as it decreases. When the ability fires, ACT resets the timer to full duration, so `TimeLeft` jumps back above `WarningValue` and the timer returns to **Calm** on the next tick. No special reset-detection is needed — a reset is simply a high `TimeLeft` reading.

**List motion (Phase 1).** Row entry, exit, and re-ordering — including the top→bottom jump when a timer resets to full — are **instantaneous**. Smoothing/animated reordering is deferred to a later phase.

**On state and removal.** Because Phase 1 respects ACT's removal, the overlay is close to a **stateless mirror** of `GetTimerFrames()`: presence, escalation, and de-escalation all fall out of the per-tick readings. It keeps only small, *bounded* per-timer state at the edges — the identity key (below), the Overdue count-up's zero-crossing time (only if the spike shows ACT clamps `TimeLeft` at zero), and the Overdue minimum-display floor (see Overdue visual). None of it reintroduces the unbounded hold-until-reset model.

**Timer identity.** Threading each tick's readings into a per-timer sequence — needed for the Overdue count-up, stable list ordering, and telling a *reset* from a *new* timer — requires a key that is stable across ticks. Provisional key: **`Name` + `Combatant`** (`TimerFrame.Name` + `TimerFrame.Combatant`); a `Name`-only key would wrongly collapse the same ability on two targets or from two casters into one entry. Captured regex groups in `SpellTimer.ExtraInfo` may offer a finer key, but `ExtraInfo` is unverified — the spike confirms whether `(Name, Combatant)` is unique per logical timer (and whether `TimerFrame`'s own `IEquatable` identity suffices).

### The escalated radial pie — warning-window semantics

When a timer escalates to **Imminent**, the pie represents the **warning window, not the whole duration**:

- Fill fraction = `TimeLeft / WarningValue` (clamped to `[0,1]`).
- At the instant of escalation, `TimeLeft == WarningValue` → the pie is **full**; it drains to empty as `TimeLeft → 0`.

A 90s timer with a 10s warning escalates at 10s-left and gives a full, fast-draining pie for those last 10 seconds — rather than a barely-moving sliver of the whole 90s. The pie's motion is calibrated to the window that actually matters, which is easier to read at a glance. The pie shows a big seconds-left number and the timer name, tinted by `FillColor`.

### The Overdue visual

A count-*up* pie would be odd — the pie represents the draining *warning window*, which is meaningless once time is negative — so Overdue **drops the pie** and instead shows a **pulsing, escalated alert with a "LATE" tag and a count-up of how late it is** (e.g. `LATE +5s`): red, fast pulse, strong emphasis (candidate: screen-edge flash). It conveys that timing is lost and how overdue the ability now is, and it disappears when ACT removes the frame at `RemoveValue`. Exact styling is a tunable Phase 1 constant.

Lateness is `−TimeLeft` when ACT reports negative `TimeLeft`; otherwise it is measured from the tick the timer first crossed zero. The spike settles which.

**Minimum-display floor.** Because Phase 1 respects ACT's removal, the loudest state is also potentially the shortest-lived — if `RemoveValue` is small, the LATE alert could flash and vanish before it registers. Mitigation (a Phase 1 constant): once shown, the LATE alert is guaranteed a **minimum on-screen duration** (e.g. ≥ 2s) even if ACT drops the frame sooner — a bounded fade-out, *not* the deferred hold-until-reset (a fixed floor, not an unbounded hold). This is the one deliberate, bounded exception to the stateless-mirror model.

### The center escalation zone

Model A moves escalated timers out of the list and toward center — but in a real raid several timers routinely cross their `WarningValue` in the same window, so this is the normal case, not an edge case, and the arrangement is a **Phase 1 design decision**, not a deferred one. All escalated elements — Imminent pies **and** Overdue LATE alerts — share one **center escalation zone**, arranged **most-urgent first** (Overdue ahead of Imminent, then soonest-to-expire). Provisional layout (a Phase 1 constant): a vertical stack anchored near screen-center, growing outward, capped at a small count with any overflow left in the list. This keeps "one thing screaming" in the common single-escalation case while degrading sanely when several fire together, and it resolves where Overdue alerts sit relative to Imminent pies (same zone, ranked first). **Confirmed for Phase 1: a vertical stack** — chosen to get the state handling built; the arrangement is a swappable constant we can revisit later (horizontal row, one-big-plus-smaller, etc.).

### Diagnostic logging (first-class Phase 1 feature)

Because the whole thing is one "read → diff → update" loop, tapping that loop yields a complete picture of ACT's behavior and our own. eq2auras writes **structured, timestamped diagnostics** (JSON-lines or CSV) capturing per-timer readings and — especially — every state transition (calm→imminent→overdue, resets, removals), each with `TimeLeft` / `WarningValue` / `RemoveValue`. It is toggleable so it is quiet in normal play. This log is both the mechanism for the verification spike (below) and a permanent debugging tool. **Volume & rotation:** normal play records **transitions only** (optionally plus low-rate sampled snapshots); the full per-tick dump is a **spike/verbose toggle**, not the default, since 30 fps × N timers grows fast. Logs write to a dedicated git-ignored directory with a size/age cap and rotation so they cannot grow unbounded.

### Baked-in constants for Phase 1

These are the values that become configuration later. Phase 1 fixes them:
- List anchor position, size, orientation, and sort (soonest-to-expire).
- Bar styling (fill, font, colors derived from `FillColor`).
- Center-pie size and position; pulse animation parameters.
- Overdue visual (count-up styling, flash).
- Render tick rate.
- Fallback when a timer has no usable `WarningValue` (`0`, or `≥` total): escalate at a **fraction of total duration** (e.g. the last 25%), not a fixed number of seconds — a fixed default would make a short timer permanently Imminent and scales badly across timer durations.

### Explicitly out of scope for Phase 1

The configuration editor; per-timer / per-category customization; import/export sharing strings; element types beyond bar + radial; game icons/art; reading the combat log directly; multiple layout groups; the Parse Meter module. All are later phases on the same core.

---

## Part III — Cross-cutting

### Testing strategy

- **Verification spike is the first implementation task.** A barebones plugin that subscribes to the four `OnSpellTimer*` events and polls `GetTimerFrames()`, writing the diagnostic log described above. Run it against synthetic timers and a real fight to *observe* — not guess — exactly: when ACT drops a frame (the `RemoveValue` behavior we inherit), whether `TimeLeft` goes negative or clamps at zero (which decides how the Overdue count-up is measured), and what a reset looks like in the data. This confirms the removal timing and the Overdue measurement. It also reports the **distribution of `WarningValue`** across the team's real timer set — validating the central premise that timers carry meaningful warning values before we build on it (if most lack one, escalation would flood the center via the fallback). The diagnostic-logging feature and this spike are the same code.
- **Synthetic timers for desk development.** We drive test timers without being in a raid — via ACT manual triggers and/or `FormSpellTimers.NotifySpell` — so the overlay can be developed and tuned at the desk.
- Standard unit tests for the state model / transition logic (pure functions over sequences of `(TimeLeft, WarningValue)` readings).

### Unverified items to confirm against a live ACT build

Flagged by API research as documented-but-unconfirmed; the spike resolves them:
- Exact runtime type of `SpellTimer.TimeLeft` (doc says "seconds"; confirm `double` vs `int`).
- Whether ACT exposes a distinct warning **color** or hardcodes the warning tint (only the threshold + sound are in the API).
- `RemoveValue` semantics precisely (counts from `0`? from `-RemoveValue`? when does `OnSpellTimerRemoved` fire?).
- Contents/keys of `SpellTimer.ExtraInfo` (where captured regex groups / per-target labels live).

The design tolerates whatever the spike finds because eq2auras owns its own (small, bounded) state; the findings only tune the Overdue measurement, the identity key, and the snapshot/threading approach — not the overall model.

### Roadmap (later phases, same core)

1. **Timer Overlay Phase 1** — this spec.
2. **Open the knobs** — expose the baked-in constants as per-timer / per-category configuration; a config surface (config strings first, then an in-ACT editor with live preview).
3. **Sharing** — import/export configuration strings, so overlay layouts travel the way timers do today.
4. **Richer elements** — more display types (icon w/ cooldown swipe, plain text, alternate bar styles), an intermediate "approaching" visual tier, animations.
5. **Hold overdue until reset** — optionally override ACT's `RemoveValue` removal so an overdue timer is *held and escalated* until the ability actually fires (a reset), for abilities a player must not lose track of. Deferred from Phase 1 (which keeps ACT's removal) because it reintroduces the cross-removal state model and a "never resets" escape hatch.
6. **Icons** — a name→icon mapping; possibly sourcing art from EQ2's own game files (never from live game memory).
7. **Parse Meter module** — replace ACT's "mini parse" window (combatant/DPS), on the same core via an encounter adapter.

### Open decisions

- **Multiple simultaneous Imminent timers** — how several big center pies arrange at once (stack, row, prioritize one). Minor; decide during Phase 1.

> *Resolved:* the overdue "escape hatch" is a non-issue for Phase 1 — an overdue timer disappears when ACT removes it at `RemoveValue`, exactly as ACT behaves today. See the roadmap for the deferred "hold until reset" aspiration.
