# eq2auras — Design Spec

**A personal ACT overlay suite for EverQuest 2.** This document is the source of truth for what eq2auras is and how it works. It is organized as: the suite vision and architecture (Part I), then the Phase 1 feature — the Timer Overlay — in full (Part II), then the Phase 2 feature — the Parse Meter — (Part III), then cross-cutting concerns, unknowns, and the roadmap (Part IV).

Status: **live and iterating.** The timer overlay is shipped and guild-verified through slice 3 (escalation, knob model, palette colors). The Parse Meter (Part III) is designed and in development. Present-tense descriptions below describe the system as designed; anything not yet built is scoped to a phase or the roadmap.

---

## Part I — The Suite

### Vision

The north star is *"WeakAuras for EQ2"*: a framework that lets a player build configurable, good-looking combat overlays, hosted in one place. WeakAuras is beloved because it is a *construction kit* — many "auras" on one engine — not a fixed window. EQ2 has nothing comparable. eq2auras aims there, built incrementally.

### The one hard constraint: ACT owns the data

eq2auras is an **ACT plugin that reads ACT's live data and renders its own UI**. It does **not** reimplement triggers, timers, or log parsing. Triggers and spell timers stay in ACT's native framework, which matters because the team already shares timer configurations through ACT's built-in sharing system — a teammate who does not run eq2auras still receives and uses those timers normally. eq2auras owns *presentation only*.

### Architecture: shared core + feature modules

The suite is deliberately layered so that each new overlay idea is a module on common plumbing, not a new project:

- **Core (reusable, feature-agnostic):**
  - Overlay window framework — a transparent, top-most window whose interaction is **parameterized per module by three orthogonal axes** (defined in §The meter window, Part III): click-through baseline (timer windows: through; meter windows: interactive), locked (geometry frozen), and interactive content (clickable menus/rows). "Click-through" is a per-window-type property, not a framework constant.
  - The render loop.
  - Rendering primitives — the **configurable row/bar primitive** (shared by timer rows and meter rows — §The shared rendering substrate, Part III), text, radial/pie, free positioning — and the escalation/conditions engine that maps *state → appearance*.
  - Configuration.
  - Diagnostic logging.
- **ACT data adapters (feature-specific, thin):** small layers that pull from ACT and normalize into the core's model. The *timer adapter* (Phase 1) reads `FormSpellTimers.GetTimerFrames()`; the *encounter adapter* (Phase 2) reads combatant/encounter data from ACT's live parse model.
- **Feature modules:** built on the core.
  - **Timer Overlay** — Phase 1 (this spec, Part II).
  - **Parse Meter** — Phase 2 (this spec, Part III): replacement for ACT's "mini parse" names/DPS window. Different data source, same core plumbing — this module is the reason the core is kept feature-agnostic, and its construction is what drives the timer/meter rendering convergence (§The shared rendering substrate).

### Packaging

Ships as a **single ACT plugin** — one `.dll` dropped into ACT's `Plugins` folder. The `Core` project's sources are **compiled directly into the plugin assembly** (shared source via `<Compile Include>`), not shipped as a second DLL: ACT's plugin scan (`Assembly.GetTypes()`, which runs *before* `InitPlugin`) resolves the types of every field in the assembly — including compiler-generated async state machines — so all types the plugin's fields can mention must live in the plugin assembly itself or the GAC. Single-assembly packaging makes that hold by construction (no `AssemblyResolve` shim, no ILRepack, one-file self-update). The `Core` project still exists as a `netstandard2.0` build of the same sources for Mac-side `dotnet test`. Features are individually toggleable, so a teammate can install the package and enable only what they want.

**Core stays the suite's shared plumbing at the source level.** If the suite later splits into sibling plugins (e.g. a standalone Parse Meter), each plugin compiles the same Core sources into itself — shared development, self-contained binaries. That also sidesteps the DLL-hell a shared `Core.dll` would create between independently-updating plugins: each plugin ships the Core it was built and tested with.

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

Everything is one loop, run on a **poll/state tick** (100 ms WinForms timer — ~10/s; visuals stay smooth between ticks via retained WPF animations, per the wall-clock rule):

> **read timer data → route to timer groups → derive each timer's state per group → update the view model (and emit diagnostics)**

Each tick reads `ActGlobals.oFormSpellTimers.GetTimerFrames()` (a `List<TimerFrame>`) for a smooth live countdown. The tick updates *state*, not pixels — WPF animates at display refresh off that state — so this is a poll/state rate, not a manual draw rate. ACT's lifecycle events (`OnSpellTimerNotify` / `Warning` / `Expire` / `Removed`, each handing us the whole `TimerFrame`) are available and may be used for precise transition moments, but polling covers Phase 1.

### Timer groups: N instances of one pipeline

The overlay is organized as **timer groups**. A group is a complete, independent instance of the escalation pipeline described in this Part — its own calm list window, its own center escalation zone, its own knobs (`ColorSource`, `EscalationStyle`), and its own persisted window positions (§Moving the overlay). Everything below described in the singular ("the list", "the center zone") applies per group.

Each group has a **source**: a rule selecting which ACT timers feed it. The shipped configuration is **exactly two groups**, wired to the only routing data ACT exposes — the per-timer panel booleans (`TimerData.Panel1Display` / `Panel2Display`, set per timer in ACT's triggers window): **Panel A** (timers flagged for ACT panel 1) and **Panel B** (panel 2). In guild practice the panels carry semantics: A = fight/boss timers, B = personal cooldowns and buff durations.

Routing mirrors ACT exactly: a timer appears in every group whose source matches — flagged for both panels → both groups; flagged for neither → shown nowhere. `FormSpellTimers.AllowPanel2` is ignored: it governs ACT's native window, not ours. (Measured 2026-07-02: the no-arg `GetTimerFrames()` poll reports panel-2 timers, so routing needs only the per-reading booleans.)

In code, Core's `OverlayEngine` owns the whole multi-group policy: it builds one `EscalationTracker` per configured group (`Settings.Panels`), routes each tick's readings by source, and returns one frame per group. The plugin stays instantiator and data supplier — it reads ACT into readings and hosts a window pair per group. Group add/remove UI and richer sources (category, name match) are roadmap — the WeakAuras "N configurable groups" north star; growing there changes only the routing predicate, not the group model's shape.

The one deliberately global piece across groups is **color identity** — a single name→slot palette map (§Timer colors), so an ability keeps one color everywhere it appears, while each group still applies its own `ColorSource`.

### Rendering technology

Drawing the overlay is the hard part, and the choice drives whether the pulse/flash effects that *are* the feature come out smooth. On .NET Framework 4.x the realistic options:

- **WPF layered window** *(recommended)* — a borderless `Window` with `AllowsTransparency=true` (per-pixel alpha), `Topmost=true`, and click-through added via `WS_EX_LAYERED`/`WS_EX_TRANSPARENT` on its `HwndSource`. WPF's retained-mode compositor gives smooth alpha-blended pulse/fade/scale animation essentially for free — exactly what the escalation visuals need — and it hosts fine as a standalone window launched from the WinForms ACT plugin.
- **Transparent WinForms + GDI+** — the most common ACT-overlay precedent (Triggernometry), but `TransparencyKey` is 1-bit (no true per-pixel alpha); smooth alpha animation requires a hand-composited `UpdateLayeredWindow` bitmap — more code than WPF for a worse result.
- **Direct2D/SharpDX or CefSharp (HTML/CSS)** — maximum control or full CSS animation respectively, but each adds a heavyweight dependency out of proportion to Phase 1 (CefSharp bundles ~100+ MB of Chromium).

**Decision: WPF layered window**, validated in the spike (which opens a window anyway — it renders a pulsing test element over the game to confirm transparency, click-through, always-on-top over borderless EQ2, and animation smoothness before any real UI is built). Fall back to WinForms + `UpdateLayeredWindow` only if WPF interop inside ACT proves troublesome.

**WPF thread model (spike resolves):** WPF requires an **STA thread with a running `Dispatcher` message pump**. The overlay either runs on ACT's existing WinForms UI thread (already STA, shares the pump) or on a **dedicated STA thread with its own `Dispatcher.Run()`**. The choice governs how the poll/render tick marshals its per-tick snapshot onto the UI thread, so the spike (which opens a WPF test window anyway) settles the thread model up front.

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
| `TimerValue` / `TimerFinalDuration` | def / instance | Total duration (post-mods; `Modable` timers get recast mods baked in per instance at creation) |
| `MasterTimer` | `SpellTimer.MasterTimer` | Display candidacy — non-master (DoT-tick) instances are diagnostics-only (§Timer identity) |
| `StartTime` | `SpellTimer.StartTime` | Governing order (newest master wins) + the smooth wall-clock remaining |
| `FillColor` | `TimerData.FillColor` | Element color (the only color ACT exposes) |
| `Category` | `TimerData.Category` | Grouping (future) |
| `RadialDisplay` | `TimerData` / `TimerFrame` | ACT's bar-vs-radial preference |
| `Panel1Display` / `Panel2Display` | `TimerData` | Group routing (§Timer groups) |

ACT exposes **no per-timer icon/image**, **no `CategoryData` object** (category is a bare string), and **no separate warning color** (only the threshold + an optional warning sound). Icons and per-category theming are therefore owned by eq2auras in later phases, not inherited.

### Escalation is driven by ACT's `WarningValue`

We do **not** invent thresholds. Each timer already carries its own `WarningValue` (the team sets it per-timer in ACT). Escalation pivots on it. This keeps Phase 1 honest with the "configuration in mind" principle — the threshold is real data we read.

**Escalation never relies on color alone.** A timer's display color is its *identity* (§Timer colors) — it may be anything from the palette, a user's own `FillColor` under `ActColor` mode, or deliberately non-distinctive under `Greyscale` — so it can never double as the urgency channel (a rose or crimson identity would false-signal against the Overdue red). Escalation is always carried by **size, position (into the center zone), motion/pulse, and the LATE tag**; color rides on top as identity, never as the signal.

### The timer lifecycle

Escalation state is derived from each timer's live `TimeLeft` versus its `WarningValue`, evaluated every tick. A timer is in one of three states:

1. **Calm** — `TimeLeft > WarningValue`. A row in the side **list**, auto-sorted soonest-to-expire, drawn as a horizontal bar (name + countdown + draining fill), colored by the timer's resolved display color (§Timer colors).
2. **Imminent** — `0 < TimeLeft ≤ WarningValue`. Presentation follows the **`EscalationStyle` knob**:
   - **`CenterRadial` (default, the brainstorm's Model A):** the timer is **removed from the list** (except on overflow — see §The center escalation zone) and promoted into the **center escalation zone** as a big pulsing radial pie. See the pie semantics and zone layout below.
   - **`HighlightInPlace` (Model C):** the timer **stays in the list**, escalated by its highlight outline — the same treatment overflow rows already get. The center zone stays unused for pies; overdue timers (linger-configured) render as LATE-styled rows in the list. (The original brainstorm explicitly kept both models available as configuration — this knob is that decision landing.)
3. **Overdue** — `TimeLeft ≤ 0` *while ACT still reports the timer*. The ability is *late* — a deterministic countdown is lost. Escalated further (see Overdue visual). **The overdue window is the timer's own `RemoveValue` config** — the same inherited-not-invented principle as `WarningValue`: a timer set to remove at 0 never shows an overdue state at all; a timer set to linger (negative `RemoveValue`) keeps being reported with negative `TimeLeft`, and LATE shows for exactly that configured window. Nothing on screen ever outlives the data.

**Transitions** follow the governing instance's `TimeLeft` directly: Calm → Imminent → Overdue as it decreases. When the ability fires again, ACT never resets an instance in place — it **appends a new instance** to the frame, a **master** when it lands outside ACT's 12s tick window (§Timer identity; `docs/act-timer-engine.md`). A new master governs immediately, so the key snaps back to **Calm** at full duration on the next tick — even if the previous, now-overdue instance still lingers in the frame awaiting its purge. A non-master (tick) append changes nothing on screen — that's the point of the rule. No special reset-detection is needed — a reset is simply the newest master changing.

**List motion (Phase 1).** Row entry, exit, and re-ordering — including the top→bottom jump when a timer resets to full — are **instantaneous**. Smoothing/animated reordering is deferred to a later phase.

**On state and removal.** Because Phase 1 respects ACT's removal, the overlay is close to a **stateless mirror** of `GetTimerFrames()`: presence, escalation, and de-escalation all fall out of the per-tick readings. The only cross-tick state is the identity key (below) and the session-stable palette map (§Timer colors) — bounded, and neither reintroduces the unbounded hold-until-reset model. (An earlier draft also carried an Overdue zero-crossing time and a minimum-display floor; the spike measured `TimeLeft` unclamped-negative and the floor was field-rejected — see §The Overdue visual — so neither exists.)

**Timer identity.** Threading each tick's readings into a per-timer sequence — needed for stable list ordering, display dedupe, and retained-visual reuse — keys on **`Name` + `Combatant`** (`TimerFrame.Name` + `TimerFrame.Combatant`); a `Name`-only key would wrongly collapse the same ability on two targets or from two casters into one entry. A frame holds many concurrent `SpellTimer` instances (re-fires append, never reset — `docs/act-timer-engine.md`), so one instance must govern the key's display each tick — resolved by the **newest-master-governs** rule:

- **Masters only.** Only instances flagged `SpellTimer.MasterTimer` are display candidates. ACT assigns the flag at trigger time per the timer's own `OnlyMasterTicks` config, so user config is respected with zero logic on our side: DoT-tick re-triggers inside ACT's 12s window arrive as non-masters and are **diagnostics-only — never displayed, never escalated** (exactly as ACT's native window treats them).
- **Newest `StartTime` wins** — *last triggered dictates the cooldown*. Every trigger event means the ability just fired, so every older prediction is falsified by definition. This is deliberately **not** ACT's native largest-master-value display: timer mods (`Modable`, baked per instance at creation, mutable on owner-death/dispel) can leave an older master with more remaining time than the newest, and the newest is still the truth. Tie-break (unreachable via ACT's 2s trigger dedup, defined for totality): larger `TimeLeft` wins.
- **No masters → the key displays nothing** that tick. In practice ACT kills a master-less frame in the same engine pass; this covers a poll landing mid-purge.
- **Fallback edge (documented, no machinery):** if the newest master purges at its own `RemoveValue` while an older, mod-lengthened master lingers, governance falls back to that older master for its remaining window — the display jumps up to a real-but-stale countdown, then dies with the frame. Rare, data-truthful, self-healing.

(Supersedes the Phase-0 **soonest-instance-governs** rule — see §Resolved by the Phase-0 spike for why the original measurement misled.) Captured regex groups in `SpellTimer.ExtraInfo` might offer a finer per-instance key but remain unverified (*Still open*).

### The escalated radial pie — warning-window semantics

When a timer escalates to **Imminent**, the pie represents the **warning window, not the whole duration**:

- Fill fraction = `TimeLeft / WarningValue` (clamped to `[0,1]`).
- At the instant of escalation, `TimeLeft == WarningValue` → the pie is **full**; it drains to empty as `TimeLeft → 0`.

A 90s timer with a 10s warning escalates at 10s-left and gives a full, fast-draining pie for those last 10 seconds — rather than a barely-moving sliver of the whole 90s. The pie's motion is calibrated to the window that actually matters, which is easier to read at a glance. The pie shows a big seconds-left number and the timer name, tinted by the timer's resolved display color (§Timer colors).

### The Overdue visual

A count-*up* pie would be odd — the pie represents the draining *warning window*, which is meaningless once time is negative — so Overdue **drops the pie** and instead shows a **pulsing, escalated alert with a "LATE" tag and a count-up of how late it is** (e.g. `LATE +5s`): red, fast pulse, strong emphasis (candidate: screen-edge flash). It conveys that timing is lost and how overdue the ability now is; it disappears when ACT removes the timer (per that timer's `RemoveValue`). Exact styling is a tunable Phase 1 constant.

Lateness is `−TimeLeft`, read directly from ACT's (measured: unclamped, negative) reporting.

**No artificial floor.** The LATE alert lives exactly as long as ACT reports the overdue timer — its duration is the timer owner's `RemoveValue` choice, not an overlay constant. (An earlier design carried a ~2s minimum-display floor; live testing showed it made remove-at-0 timers display a phantom "LATE" the owner had explicitly configured away. Field-tested verdict: never outlive the data.) A reset is naturally instantaneous: a re-fire that lands as a **master** (outside ACT's 12s tick window — §Timer identity) governs the moment ACT reports it, clearing the LATE immediately even while the old overdue instance lingers in the frame awaiting its purge. A re-fire arriving *inside* a still-running tick stream stays non-master, so LATE correctly holds until the overdue master purges (the frame kill) — same as ACT's native window. (Raid-measured 2026-07-05, pre-amendment: soonest-governs kept LATE alive up to `|RemoveValue|` seconds *past* a live master recast — the newest-master rule is the fix.)

### The center escalation zone

Model A moves escalated timers out of the list and toward center — but in a real raid several timers routinely cross their `WarningValue` in the same window, so this is the normal case, not an edge case, and the arrangement is a **Phase 1 design decision**, not a deferred one. All escalated elements — Imminent pies **and** Overdue LATE alerts — share one **center escalation zone**, arranged **most-urgent first** (Overdue ahead of Imminent, then soonest-to-expire). Provisional layout (a Phase 1 constant): a vertical stack anchored near screen-center, growing outward, capped at a small count. Overflow imminent timers (lowest priority) have already left the list conceptually, but with no center slot free they **wait as escalated/highlighted rows in the side list** — visually promoted, not yet centered — and move into the zone in most-urgent-first order as slots free. This keeps "one thing screaming" in the common single-escalation case while degrading sanely when several fire together, and it resolves where Overdue alerts sit relative to Imminent pies (same zone, ranked first). **Confirmed for Phase 1: a vertical stack** — chosen to get the state handling built; the arrangement is a swappable constant we can revisit later (horizontal row, one-big-plus-smaller, etc.). Each timer group has its own center zone (§Timer groups): escalated timers converge within their group, never across groups.

### Diagnostic logging (first-class Phase 1 feature)

Because the whole thing is one "read → diff → update" loop, tapping that loop yields a complete picture of ACT's behavior and our own. eq2auras writes **structured, timestamped diagnostics** (JSON-lines, BOM-less UTF-8): **five record kinds and nothing else** — per-instance `poll` readings plus the four ACT frame events (`notify`/`warning`/`expire`/`removed`), each carrying the timer's identity, `TimeLeft`, `WarningValue`, total, and the panel-routing flags (so group routing is reconstructable offline). Overlay-side state transitions are **not** logged as record types — they are derivable from the poll stream (debug mode's full dump), which is the point of logging raw readings. (An earlier draft promised dedicated state-transition records carrying `RemoveValue`; they were never built, and derivable-from-polls made them unnecessary.) Per-instance records carry the instance's `master` flag — **every** instance is logged, non-master ticks included, so field captures explain themselves even though non-masters never display. Frame-event records (`notify`/`warning`/`expire`/`removed`) report the **largest-master** value — the value ACT's own event logic keys on — plus a live-instance count, never an arbitrary instance's reading (the raid-scale flaw that produced misattributed `warning` records). A `removed` record's value is **null** by construction (the event fires after the frame is emptied or de-mastered), and in the no-master frame-kill case null coexists with a *positive* live-instance count — the count is the evidence of killed non-masters. An empty log file is deleted on session close rather than left as a zero-byte artifact. This log is both the mechanism for the verification spike (below) and a permanent debugging tool.

**Debug mode (the volume knob).** A persisted tab checkbox — `DebugLogging` in `Settings`, global (not per-panel), **default off** (the DCJS 0-value rule holds: absent-from-disk deserializes to off). Off — the normal state — records **lifecycle events only** (`notify`/`warning`/`expire`/`removed`): small, always-on, so a problem report always arrives with usable logs attached. On = the **full per-tick instance dump** (what shipped as the always-on spike stream through v0.1.90), for deliberate capture nights; 10 records/sec × N instances grows fast, which is exactly why it is a choice. The toggle applies live at the next poll — no plugin reload. Either mode still deletes a session file that ends with zero records written.

**Retention (the disk knob).** At plugin load, before opening the session's log file, a **rolling-window sweep** deletes the oldest `spike-*.jsonl` files beyond **14 days** of age or beyond **200 MB** total folder size (newest files always win; both are Phase 1 baked constants, promotable to knobs if anyone asks). **Debug mode on suppresses the sweep entirely** — verbose logging is a declared intent to keep everything; turning debug off makes the next load sweep as usual. The sweep never touches the file it is about to open.

Logs write to an **app-data path on the Windows box** (`%APPDATA%\Advanced Combat Tracker\eq2auras\logs` — *not* a repo working tree; that machine has none). **Retrieval to the Mac** (where analysis happens) is via a synced/shared folder or a manual copy (the self-hosted-runner ferry is foreclosed once the repo is public — see §Development & test cycle — *Escape hatch*). Without a retrieval path the spike's findings are stranded on the machine that can't analyze them, so this is part of the spike's setup, not an afterthought. The repo's `.gitignore` entry for logs matters only once a log lands on the Mac.

### Baked-in constants for Phase 1

These are the values that become knobs later (promoted one at a time into `Settings` — see §Configuration). Phase 1 fixes them:
- List sort (soonest-to-expire) — growth/orientation is a knob now (§Window growth); positions, element dimensions, and spacing are Settings knobs (§Moving the overlay, §Element dimensions).
- Bar styling (fill alpha, spark — applied over the resolved display color; font is a knob now, §Typography).
- Pulse animation parameters (pie size is a knob now — §Element dimensions).
- Placement-grid line counts and tier colors (§Moving the overlay).
- Diagnostic log retention window: 14 days / 200 MB (§Diagnostic logging).
- Overdue visual (count-up styling, flash).
- Poll/state tick rate.
- Target display & DPI: **primary monitor, system DPI** for Phase 1 (per-monitor DPI and monitor selection are later config; stated now to preempt WPF layered-window coordinate and click-through hit-testing bugs).
- Fallback when a timer has no usable `WarningValue` (`0`, or `≥` total): escalate at a **fraction of total duration** (e.g. the last 25%), not a fixed number of seconds — a fixed default would make a short timer permanently Imminent and scales badly across timer durations. If total duration is *also* unavailable, last-resort to an **absolute threshold** (e.g. the final 10s).

### Configuration: the knob model

Every tunable behavior is a **knob**: a typed value with a baked-in default, held in one plain `Settings` object in Core. The abstraction is deliberately thin — no framework, no reflection — but it is the single source of truth every later configuration phase builds on (per-timer overrides, the visual editor, import/export sharing strings all read/write the same store).

- **Store:** `Settings` (Core, pure, serializable) persisted to `%APPDATA%\Advanced Combat Tracker\eq2auras\settings.json` via `DataContractJsonSerializer` (never `System.Web.Extensions` — breaks the WPF markup compiler). Missing file or missing fields → defaults, so old settings files survive new knobs (forward-compatible).
- **Per-group settings:** `Settings.Panels` holds one `PanelSettings` per timer group — the group's knobs (color source, escalation style, font family + base size), its four window-position values (list Left/Top, center-zone Left/Top), its element dimensions and spacing (`RowWidth`, `RowHeight`, `RadialSize`, `RowSpacing` — §Element dimensions), and its per-window grow directions (`ListGrowDirection`, `CenterGrowDirection` — §Window growth). Positions are **nullable**: DCJS materializes missing numeric fields as `0` (a real screen corner), so `null` — never zero — means "unset, use the default layout". `Parse` normalizes the list to exactly the shipped two groups. **Legacy migration runs both directions:** an old flat file seeds Panel A from its top-level knobs (Panel B starts at defaults); on save, the top-level knobs are written mirroring Panel A, so an older build reading a newer file stays sensible.
- **Consumption:** Core policy (engine, tracker, builder, color assignment) takes the `Settings`/`PanelSettings` instance as input — keeping policy pure and Mac-testable. Renderers read display knobs the same way.
- **Surface:** minimal WinForms controls on the plugin's ACT tab, added per knob as needs arise — alongside the self-update controls ("check for updates", the `BetaChannel` opt-in checkbox, and the update-available notice — §Release channels & public distribution; there is no token-entry control). Per-group knobs appear as one labeled control set per group ("Panel A" / "Panel B" group boxes) — no group selector. This is deliberately **not** the WeakAuras-style editor; that later phase edits the same `Settings`.
- **Current knobs (per group):** `ColorSource` — `Palette (default) | Greyscale | ActColor`; `EscalationStyle` — `CenterRadial (default) | HighlightInPlace`; font family + base size (§Typography); element dimensions and row spacing (`RowWidth`/`RowHeight`/`RadialSize`/`RowSpacing` — §Element dimensions); per-window grow directions (§Window growth); the four window positions (set by dragging — §Moving the overlay). **Global knobs:** the color palette (`Settings.PaletteArgb` — §Timer colors); `DebugLogging` (§Diagnostic logging). Everything still listed under *Baked-in constants* is a future knob awaiting promotion into `Settings`.

### Moving the overlay: unlock/move mode

The overlay windows are click-through by design, so repositioning needs an explicit mode — the WeakAuras "unlock frames" pattern. A **"Move overlay windows" checkbox on the plugin tab** unlocks every overlay window at once:

- **Unlocked:** each window clears `WS_EX_TRANSPARENT` and shows move chrome — a dashed outline, a translucent fill, and a label chip naming the window ("Panel A — list", "Panel B — escalation"). The chrome is also the hit-test surface: a transparent WPF window is mouse-invisible even without the click-through style, and the fill gives empty windows (a quiet list, an idle center zone) a visible, grabbable footprint. Dragging anywhere on the window moves it (`DragMove`).
- **Positions persist** into the group's `PanelSettings` on every drag-end and again on re-lock, so a crash while unlocked loses nothing. (Drag-end saves run on the overlay's STA thread while tab-knob saves run on ACT's UI thread — `SettingsStore.Save` serializes writers.)
- **Locked (default):** chrome hidden, click-through restored.
- Unlock shows **every** overlay window regardless of each group's `EscalationStyle` — a center zone unused under `HighlightInPlace` still shows its chrome and can be positioned before styles are flipped.
- **Unlock mode moves windows; it does not size them.** Sizing follows the WeakAuras division of labor — *drag is for position, numbers are for size* — because dimensions want to be exact and reproducible, not gestured. (A corner resize grip shipped briefly and was field-rejected as "guessing dimensions"; it is retired.)

- **Unlock also shows a full-screen placement grid** — a reference for positioning, pinned to the primary monitor's bounds and **permanently click-through** (it cannot be moved because it is not movable: no chrome, no drag handling, `WS_EX_TRANSPARENT` set once and never toggled). The lattice has **fixed line counts — 64 columns × 32 rows** — with cell size calculated from the screen, so the grid always fits edge to edge exactly and its lines land on exact screen fractions at any resolution. Brightness is a **three-tier hierarchy that tells you where you are**: the true center cross (W/2, H/2) is brightest; the four quarter-center lines (W/4, 3W/4, H/4, 3H/4 — the centers of the quadrants the cross creates) are second; every other line is uniformly faint. Counts are divisible by 4 so center and quarters always land on lines. Lines are 1 DIP, aliased (device-pixel-exact only at 100% scaling — consistent with the Phase-1 DPI stance). (The first version used a fixed 1-logical-cm pitch; the field rejected it — screens aren't cm multiples, so the last column/row chopped and no line ever marked screen center.) **The grid sits beneath the overlay windows**, kept there deterministically: when the grid shows, each overlay window re-asserts its topmost position above it, and being click-through the grid never activates and never re-raises — the windows being positioned always draw above their reference. (Other applications' topmost windows may sit below the grid while move mode is open; only this plugin's windows are lifted.) Drawn once (no per-tick work); appears with move mode, disappears on re-lock; no persisted state. Grid line counts and tier colors are Phase-1 constants (§Baked-in constants). The grid lives with the reusable overlay-framework pieces (`ClickThrough`, `MoveChrome`) — shared plumbing for every future feature module in this one plugin.

Positions are WPF device-independent units on the primary monitor (per the Phase 1 DPI stance). A null stored position means "use the built-in default layout": Panel A's windows where they have always been, Panel B's beside/below them, non-overlapping.

### Element dimensions: the timer element and its display forms

A group displays one logical **timer element** per timer, in one of three **display forms** depending on state and `EscalationStyle`: a **row** (calm; and everything in `HighlightInPlace`), a **radial** (imminent under `CenterRadial`), and the **LATE card** (overdue). The forms have real, per-group dimensions in `PanelSettings` — the first element-level customization on the panel → window → **element** trajectory:

- **`RowWidth` × `RowHeight`** (null = 250 × 26, today's look). Width-flavored geometry (fill drain math) derives from width; height-flavored geometry (corner radii, margins, spark thickness) derives from `RowHeight / 26`. **The configured dimension always wins**: text that doesn't fit the row clips at the row bounds — the font knob and the height knob sit side by side on the tab, and reconciling them is the user's call. (A text-fit floor shipped briefly; the field showed it silently rendered rows taller than their configured height, leaving the tab and the screen disagreeing — the same lesson as the Overdue minimum-display floor: **never override the knob**.)
- **`RadialSize`** (null = 110, the pie's diameter). One value, not W×H: the pie renderer draws circles, and an oval radial is real elliptical-wedge work for a use case nobody has asked for — the door stays open. The LATE card derives from the radial (~1.55× wide, matching today's 170/110 proportion).
- **`RowSpacing`** (null = 4 — today's look *at the default row height*; the retired derivation scaled the gap with row height, so scaled-height users see a one-time change on update): the vertical gap between rows, in **raw DIPs — flat, never derived or scaled** (the configured value wins). `0` = rows touching; rows never overlap. Clamped 0–50.
- Dimensions and spacing are **tab knobs** (numeric fields in each panel's group box), clamped to shared `Settings` constants — row width 100–800, row height 16–100, radial size 40–400, spacing 0–50 — live-applied through the same rebuild-once path as the font knob, persisted like every other knob. Text sizes never change with element dimensions.
- Window sizes are consequences, never configuration: the list window is as wide as its rows and as tall as their stack; the center zone fits its radials/cards.
- *(Supersedes the first customization slice's window-level `ListScale`/`CenterScale` — retired keys in existing settings files are ignored by deserialization; nothing stable ever shipped them.)*

### Window growth: per-window grow direction

Each window carries its own **`GrowDirection` — `Down (default, today) | Up`** (`ListGrowDirection` / `CenterGrowDirection` in `PanelSettings`): a grow-up window keeps its **bottom edge anchored** and extends upward as content appears — for players who place a list above other UI. The persisted vertical coordinate is the **anchored edge** (top edge when growing down, bottom edge when growing up); at runtime the anchored edge stays fixed by compensating `Top` with the content-height delta on every size change. **Flipping the knob converts and persists the anchored edge from the window's actual on-screen geometry** (bottom = top + current height, and back) — even when no position was stored yet — so the window visibly stays put: the knob changes how the window *grows*, never where it *is*. The null rule governs only states a flip can't produce (cold starts, hand-edited files): with no stored position, the default layout's vertical coordinate is used as the anchored edge directly — an Up window grows upward from where a Down window would begin. Deliberately **per window, never per panel**: this is the first knob of the *window configuration* trajectory (element/group arc) — a group's list can grow up while its escalation stack grows down, and future windows each bring their own growth, whatever display logic runs under them.

**Row order anchors with the window** (field finding, 2026-07-05): the soonest-to-expire row sits nearest the **anchored edge** — grow-down = soonest at top (the original behavior, correct by accident: anchor = top), grow-up = soonest at bottom, so the most urgent timer stays put while calmer rows stack away from it. Implemented as a visual-order reversal in the list window's render under `Up`; the engine's sort is untouched. **Deliberately lists-only** (Alex, 2026-07-09): the center escalation stack keeps most-urgent-first engine order under either growth — its grow knob governs window geometry, not element order. Ordering rules per window type are exactly the kind of per-window configuration the element/group arc owns; bolting a second special case on now would clutter that goal.

### Timer colors: session-stable palette assignment

ACT's per-timer `FillColor` is user data that overwhelmingly sits at the default blue, so it fails as a visual identity. Default color policy (`ColorSource = Palette`):

- The palette is a **global knob**: `Settings.PaletteArgb`, a variable-length list of ARGB colors (clamped 1–16), defaulting to the guild-approved 5 in `ColorPolicy.DefaultPaletteArgb` (sky, amber, teal, rose, indigo). Missing/empty in the file → the default (the same DCJS null-means-default pattern as `panels`). Deliberately global, never per-panel — color is ability identity: one assignment map, one palette. Custom colors render **as-is**, exactly like the built-in ones (the slate-soften stays `ActColor`-only; a user who picks hot pink owns hot pink). The greyscale ramp is a designed accessibility mode and stays fixed. Tab surface: a row of clickable color swatches (native `ColorDialog` per swatch), add/remove within bounds, and a reset-to-default button.
- **Color is keyed by normalized timer NAME — and nothing else.** The color's job is to identify *the ability as players think of it*, and the name is its stable proxy: the same ability cast by different boss variants (different `Combatant`s) or under zone-categorized trigger sets keeps one color. Keying by `(Name, Combatant)` or by category would recolor the same ability across boss versions/zones — exactly the confusion this feature exists to prevent. (If a user names zone-variant triggers differently, they get different colors — that's their expressed intent, fixable by renaming.)
- Assignment is **first-fired order**: the first time a name fires in the session it takes the next palette slot, and keeps it for the **plugin-instance lifetime** — stable across wipes, re-pulls, and boss versions. Consistency is a *repeated-attempts* feature; a plugin reload (i.e. taking an update) resetting the map is accepted (one-shot kills never needed consistency). Past the palette's length, assignment cycles. Explicitly per-ACT-instance; never synchronized across users. The map is also **global across timer groups** — one map per plugin instance, so a dual-flagged timer keeps one color in both panels (each group still applies its own `ColorSource` to the shared slot).
- **Display identity is unchanged** — rows/dedupe still key on `(Name, Combatant)`; two mobs casting the same ability get two rows that *share* the ability's color, which is correct under this model.
- `Greyscale` uses the same assignment mechanism over a grey ramp. `ActColor` restores the timer's own `FillColor` (with the slate-soften pass). Palette/greyscale colors are designed and render as-is.

### Typography: per-panel font

Each panel carries a font knob applying to both of its windows: `FontFamily` (any installed Windows font, by name; null = system default — today's look) and `FontBaseSize` (in WPF device-independent pixels; null = 13 — today's look. The native font picker speaks *points* — the surface converts, `points × 96 / 72`). The overlay's text roles derive from the base: **row text, names, and both LATE roles render at the base size as-is**; only the radial's big seconds number keeps a proportional boost (≈ 2.6×) — it is the escalation focal glyph. (The LATE tag originally carried a ≈ 1.7× boost; the field read it as a bug — "much larger than the selected font" — and special-cased typography is a corner: when windows are individually configured, a LATE-dedicated window gets its own font like any other. Until then, LATE respects the font as-is.) Per-role customization is a later phase (trajectory: per-window, then per-element, as customization abstracts). Tab surface: a "Font…" button in each panel's group box opening the native `FontDialog` (family + size in one dialog), with a label showing the current choice **in points — the picker's unit** (an earlier label showed raw DIPs; the field found "pick 16, see 21" confusing — the label must speak the unit the user chose in). Font and element dimensions (§Element dimensions) are deliberately orthogonal: **dimensions set how much space an element takes; font sets how readable its text is** — text never changes with element dimensions.

### Explicitly out of scope for Phase 1

The configuration editor; per-timer / per-category customization; import/export sharing strings; element types beyond bar + radial; game icons/art; reading the combat log directly; group add/remove UI and non-panel routing sources (two ACT-panel-fed groups ship — §Timer groups); the Parse Meter module (now Phase 2 — Part III). All are later phases on the same core.

---

## Part III — Phase 2: The Parse Meter

### Goal

Replace ACT's "mini parse" window with a **configurable, interactive damage/healing meter** — Details!-class display quality over ACT's parse data. The design inputs are two ground-truth docs, read before any of this was decided: `docs/act-parse-engine.md` (ACT's combat-data pipeline, decompiled) and `docs/details-addon-reference.md` (the display-architecture gold standard, read from source). Where this Part states an ACT engine fact without argument, that doc is the evidence.

The meter is a **module in the same plugin** (one DLL, own tab toggle — §Packaging), on the same Core. It ships in slices like the timer overlay did; **slice 1 is a groundwork slice** — its success criterion is that the shapes below are correct to build on, not guild-facing polish.

### The one data rule: ACT is the data layer, full stop

The meter reads **ACT's computed combat model** (`CombatantData` / `EncounterData` properties) and never builds a parallel accumulator over raw swings. By the time a value lands in `CombatantData.Damage`, ACT's EQ2 parsing plugin has already applied every correction that makes the number right — ward-absorb recalculation, multi-type damage merging, pet self-hit dropping, rename/redirect fixes. Re-deriving any of that would re-litigate solved problems and take ages of field testing to get right; this is the parse-side expression of "ACT owns the data".

Consequences the design accepts knowingly:

- **The metric vocabulary is bounded by what EQ2 logs.** There is no overheal metric because EQ2 logs only *effective* healing — the data structurally does not exist. The first filter for any proposed metric is "is this in the game's logs at all?", then "does ACT compute it?".
- **Windowed/rolling metrics need no swing access either**: rolling DPS is a ring buffer of *polled cumulative totals* (Details samples actor totals the same way), added when a slice wants it.
- **Drill-down stays possible without our own store**: ACT retains every raw `MasterSwing` under `AttackType.Items`; a future custom display iterates ACT's own retained swings under the lock.
- **Pets display as ACT reports them** (full pet names as separate combatants); owner-merged attribution is a later slice applying the parser's own `petSplit` convention, never a parallel data path.

**Read discipline** (from `docs/act-parse-engine.md` §Thread safety, non-negotiable): all reads happen **briefly under `ActGlobals.oFormActMain.AfterCombatActionDataLock`** — even property getters mutate cache fields — snapshot into Core DTOs, release, render only from the snapshot. Never hold `EncounterData` references across fights (ACT culls at every combat end — resolve by handle per poll, degrade to empty when gone); never touch `EncId`/`GetHashCode()` on a live encounter (O(all swings)).

### Segments mirror ACT's encounter list

A meter window shows one **segment** — a slice of combat time. The segment model is deliberately **ACT's own encounter list, nothing more** — anyone who reads ACT's encounter dropdown already understands our meter:

| Segment | ACT source | Slice |
|---|---|---|
| **Current** | `ActiveZone.ActiveEncounter` — live during combat, final totals after | slice 1 (the only segment) |
| **History** | `ZoneData.Items` — each past encounter, as ACT retains them | later slice |
| **Overall** | `Items[0]`, ACT's live-fed zone "All" merge — exists only when ACT's "Zone All listing" option (`PopulateAll`) is on; the picker surfaces "unavailable" when off | later slice |

We consciously give up Details-style overall *policy* (boss-only folds, per-named resets): ACT's overall means "everything since you zoned in", which is coherent and explainable. Policy folds are a roadmap knob if anyone asks. Adding a segment later is "new data source, same row pipeline" — the display never changes shape.

**Current-segment lifecycle (slice 1):** the window renders `ActiveEncounter` whenever it exists — live while the encounter is active, **frozen at final totals** once combat ends (ACT drain-waits its swing queue before `OnCombatEnd`, so end-of-fight totals are complete), and empty at session start / after a zone change until the first fight. Slice 1 is **poll-only** — no ACT event subscriptions; activity, reset, and finalization all derive from the per-poll read (`InCombat` / `encounter.Active`), matching the timer module's poll-first precedent.

### Rates come from our wall clock

Every ACT `…DPS`/`…HPS` property divides by **log-time** duration, which freezes during log silence — the same lurch class the timer overlay works around. So rate metrics never read ACT's rate properties: they read the raw **total** (cheap, monotonic) and divide by the encounter's **estimated-live duration** — `ActGlobals.oFormActMain.LastEstimatedTime − encounter.StartTime` while the encounter is active (ACT's own `{duration}` wall-clock logic), and the encounter's finalized log-time `Duration` once it ends. The wall clock owns the visuals; ACT's totals own the truth.

### The metric registry

One **flat registry** of metric definitions is the meter's entire vocabulary — for the picker menu, for row values, and later for user-defined metrics. A definition is:

- `key` — stable id, borrowing **ACT's ExportVariables names** (`encdps`, `enchps`, `cures`, …) as the canonical vocabulary: EQ2-curated by the parser's author, familiar from the mini parse, and ready-made for a future `{var}` format-string feature.
- `label`, `category` — picker display and menu grouping. Grouping is a display attribute, **never a dispatch axis** (Details' if-chain scar).
- `select` — a function `CombatantReading → double` producing the raw number that drives sort, bar scale, and percent.
- `format` — value → display string (K/M/B abbreviation family; counts format as plain integers).

**Selectors read ACT properties directly — never ACT's `ExportVariables` formatter callbacks.** Two reasons, both from the decompile: the EQ2 parsing plugin `Clear()`s and repopulates those dictionaries at its own init (anything registered or depended on there can be wiped by plugin load order), and the formatters return pre-scaled display *strings* where the pipeline needs raw *numbers*. ACT's names, our plumbing.

**Rows are two-tier.** The row model carries a **primary metric** — what the window's picker selects; it alone drives bar width and sort order — plus a **`secondaries` list**: additional metric values displayed as text on the row, selected independently of the primary (guild-requested: "show an arbitrary extra number on the row"). A count metric like cures needs no bar of its own to be useful — it can ride any row as a secondary. Slice 1 ships the DTO shape (primary + secondaries list) but renders **primary-only**; the secondary-selection UX and row text slots are the immediate follow-on slice.

**Slice-1 metrics** — deliberately two rates + one count, so both formatter paths and the "count as primary" case are exercised from day one:

| key | label | select | kind |
|---|---|---|---|
| `encdps` | DPS | `Damage` total ÷ wall-clock duration | rate |
| `enchps` | HPS | `Healed` total ÷ wall-clock duration | rate (includes wards — the EQ2 parser folds absorbs into `Healed`, correctly) |
| `cures` | Cures | `CureDispels` total | count (proportional bar, integer value text) |

Adding a metric is appending a definition — zero pipeline edits. A future *user-defined* metric is just a definition whose selector is built from declarative choices (source/target/ability filters) or, later, a script — first-class from day one because `select` is already a function; deferred but architecturally unblocked.

**Displayed combatants:** the encounter's **allies** (`EncounterData.GetAllies()` — ACT's own ally classification), sorted descending by the primary metric with a deterministic name tie-break (never Details' epsilon-seeding wart), truncated to a max-row-count baked constant (10). Percent = the combatant's share of **all** allies' total for the primary metric (truncation never changes anyone's percent); the header total is that same all-allies sum.

### The meter window

A meter window is a **fat, dumb viewport** (Details' proven shape): all data lives in the poll/registry pipeline, which doesn't know windows exist; the window owns only *which* segment/metric it shows plus its geometry — so N windows over the same data are nearly free (multi-window is the natural slice-2 step, not a re-architecture).

**Interactive, not click-through.** This is the meter's deliberate divergence from both the timer windows and ACT's own mini parse: the data-selection menu *is* the product — a click-through meter is just the mini parse with nicer paint. The general model this forces (recorded in Part I's architecture) is **three orthogonal axes per window**:

- **click-through baseline** — timer windows: through (game clicks pass); meter windows: interactive. (A per-window "make this meter click-through too" knob is a later behavior option.)
- **locked** — geometry frozen: position and size cannot change by dragging. **Lock freezes geometry only** — menus and future row clicks keep working. Toggled per meter window from its own context menu (default unlocked), persisted. The timer module's global move-mode checkbox does not govern meter windows: their interactivity makes a separate unlock mode unnecessary (move mode's bundled unlock exists precisely because timer windows are click-through).
- **interactive content** — the meter window's content surface accepts clicks (menu now; row drill-down later).

**Header (the interaction surface).** A persistent header strip: **`(duration) title — metric` on the left, the metric's total on the right**, plus a small affordance glyph hinting the menu. The header is the **drag handle** (drag-end persists position, same crash-safe pattern as timer windows) and **right-click on it opens the menu**: the metric picker (slice 1: DPS / HPS / Cures) and the lock toggle. The title is the encounter's **strongest-enemy-so-far**, computed per poll (`GetStrongestEnemy()` — ACT only finalizes `Title` at combat end); it can flip mid-fight as damage shifts between mobs — **known, accepted behavior**, not a bug. Richer header stats are a later slice.

**Rows.** The settled layout (mocked and picked, 2026-07-15): name on the left, value + percent on the right, both overlaid on a colored proportional fill — the house style, matching timer rows; **no rank number**. Row color comes from the same global name-keyed palette assigner the timer module uses (an ally keeps one color across fights and across both modules — color is identity, everywhere).

**Row animation — the meter's target model.** Timer bars drain on the wall clock; meter bars **lerp toward a data-driven target width** re-computed each poll (rate-limited catch-up, a tunable constant). Rows are **slot-keyed**: a retained row pool where each visual row is a fixed rank slot and combatants **re-bind** to slots as sort order changes. **Row-reorder position animation is deliberately absent, not missing**: an overtake reads seamlessly because the two bars' widths converge before the rebind swaps them (the rising bar grows toward the falling one) — adding sliding rows later would fight this mechanism, so don't. Rows fade in/out on enter/exit. Retain elements, animate properties — per-tick rebinds re-target animations, never rebuild visuals.

### The shared rendering substrate (the convergence)

The meter is the **second concrete consumer** of the overlay machinery the timer built — and by decision (2026-07-15) it is the forcing function that starts the element/group convergence *now*, incrementally, instead of a someday big-bang framework. Two components are extracted from the timer implementation and consumed by **both** modules:

1. **The overlay-window base** — geometry + position persistence, the three-axis interaction model (parameterized: timer = click-through/move-mode, meter = interactive/lock), child-element layout (grow direction, spacing, anchored edge), and the retained-element pool discipline. The placement machinery (drag, persistence, grid) generalizes with it.
2. **The row/bar primitive** — one configurable component: horizontal bar with animatable proportional fill, fill color, leading label, trailing value text, and **optional features as row configuration** — the timer's spark is a customization of the row (`spark: on`), not a reason for a separate timer bar. Timer row = fill-drain + spark + countdown text; meter row = value-lerp + value/percent text. The pluggable part is the **animation target source** (wall-clock drain vs. data-driven lerp).

**The convergence guardrail, both brackets** (decision, 2026-07-15):

- *Ceiling — no speculative generality:* extract only the union of what the timer and meter concretely need. Two real consumers define the shape; imagined third consumers don't. Genuinely single-consumer forms (the radial pie, the LATE card) stay timer-only.
- *Floor — no lazy divergence:* "the timer's version is a little different" is **not** grounds to skip sharing. If a common component is reachable, take it, do the work, accept the regression risk. **The burden of proof is on *not* sharing.**

**Accepted consequence:** the timer module re-seats onto the extracted base and primitive, so the meter's first slice carries **timer-regression risk** — the field-tuned bar visuals (drain, spark, palette color, dimensions) and window behaviors (positions, growth, move mode) must be re-verified identical. Core TDD pins the extracted policy; the slice's merge-gate live script includes an explicit **timer-regression pass** alongside meter verification.

Roadmap consequence: this merges the previously separate "element/group model" arc and "Parse Meter" roadmap items into one incremental trajectory — each extraction lands when a concrete second consumer forces its shape ("extract-don't-copy"), tracked in the backlog rather than a standalone framework plan.

### Assembly split & polling

Same split as Phase 1, same reasons (§Development & test cycle):

- **Core (netstandard2.0, Mac-testable, TDD):** the metric registry and selectors, rate/percent/sort/rank math, the two-tier row DTOs, the per-poll `MeterEngine` (readings in → one renderable frame out — the meter-side sibling of `OverlayEngine`, standalone; any deeper engine-level sharing must earn its way in under the convergence guardrail), row-pool binding policy, lerp targeting, and `MeterSettings`.
- **Plugin (net472/WPF):** the **encounter adapter** — the per-poll ACT read (lock → snapshot allies' totals + encounter identity/duration into Core DTOs → release) — and the meter window (header, menu, row visuals) on the shared base.

**Poll cadence:** the meter samples on the plugin's existing 100 ms poll tick at a divider (baked constant, every Nth tick, effective ~2–4 Hz — Details' refresh-class cadence; the mini parse's default is every 5 *seconds*). The snapshot cost stays in the engine doc's safe zone by construction: per-combatant **total** properties (the incremental-cached kind) plus one ally-list read and one `GetStrongestEnemy()` call for the title — encounter-level aggregate calls are "fine at 1–10 Hz" per `docs/act-parse-engine.md` §Performance, and this design makes exactly one of them per poll. The expensive shapes (per-combatant multi-segment `Duration`, `EncId`/hashing) are never read.

**Settings:** a `MeterSettings` section on the global `Settings` (DCJS rules apply: nullable position values — null, never zero, means "unset, use the default placement", per the timer windows' convention; enum/bool defaults at the 0-value). Slice-1 persisted state: module enabled (default **off** — opt-in while groundwork), window position, selected metric key (missing or unknown key → the DPS default — the registry lookup is the forward-compat guard), locked flag. Dimensions/font knobs arrive with later slices; slice 1 renders at baked defaults matching the timer's look.

**Diagnostics:** slice 1 adds **no meter-specific log record kinds** — the JSONL stream stays timer-only. Meter diagnostics are a later-slice decision once field behavior shows what a capture needs (recorded so the omission reads as chosen, not forgotten).

**Teardown:** the module obeys the same discipline as everything else — `DeInitPlugin()` closes meter windows and stops sampling; the tab toggle does the same live.

### Slice map

- **Slice 1 (this design):** one meter window · current segment · allies · metric picker over {DPS, HPS, Cures} · the `(duration) title — metric | total` header · overlaid rows · width-lerp animation · lock · the two shared-substrate extractions + timer re-seat.
- **Deferred to later slices:** segment picker (history/overall); multiple windows; secondary-data-point selection UX (multi-value row text — guild-requested, next in line); richer header stats; actor-filter modes; owner-merged pets; per-window dimension/font knobs; click-through-opt-in knob; row drill-down; skins/chrome; user-defined metrics (declarative, then scripted); rolling DPS; boss-only overall policies.

### Testing strategy (Parse Meter slice 1)

- **Core TDD** (the fast Mac loop): registry selection/format, wall-clock rate math (including the freeze-at-final transition), sort/tie-break/percent/truncation, two-tier row DTO shape, slot re-binding, lerp target computation.
- **Live verification on the box** (merge-gate script, concrete "do X, expect Y"): meter appears with tab toggle; solo combat against any target shows a live DPS row within a poll interval; metric switch via header right-click; drag + lock + position persistence across reload; combat end freezes totals; zone change empties; **timer-regression pass** — timer overlay looks and behaves identically to the shipped build (drain, spark, colors, dimensions, positions, move mode, grow directions).
- The encounter adapter's ACT-facing claims (lock discipline, ally list shape, title flips) are exactly the `[decompiled]`→`[field]` promotion path `docs/act-parse-engine.md` §Sources defines — field notes land there as raid captures confirm them.

---

## Part IV — Cross-cutting

### Development & test cycle

Development happens on **macOS** (with Claude); ACT and EQ2 run on a **separate Windows machine that stays a passive test target** — no build toolchain, no dev work, nothing installed beyond ACT and this plugin. That split is forced by one constraint and enabled by one architectural response.

**Constraint:** ACT plugins are Windows + .NET Framework only (WPF, .NET Framework, and ACT itself do not run on macOS). The build must happen on Windows — but not on the dev Mac (can't) and not on the personal Windows box (kept clean), so it happens in **cloud CI**.

**Project split** (the concrete form of the core/module architecture):
- **`eq2auras.Core`** — pure logic (state model, escalation rules, per-tick snapshot types, config). Target **.NET Standard 2.0**, so it **builds and unit-tests on the Mac** with `dotnet test`. Most TDD lives here.
- **`eq2auras.Plugin`** — the Windows-only shell (`IActPluginV1`, timer adapter, WPF overlay, self-updater). Target **.NET Framework 4.7.2**. **Compiles the Core sources into itself** (shared source — see Packaging) rather than referencing `Core.dll`, so the shipped plugin is one self-contained assembly. Compiles and runs only on Windows.

**Referencing ACT:** the plugin references `Advanced Combat Tracker.exe` itself (namespace `Advanced_Combat_Tracker`) via a relative `HintPath` with `SpecificVersion=False` and `Private=False` (not copied to output). The exe is **not committed to this (public) repo**; CI downloads ACT's **latest public release** (`ACTv3.zip` from `EQAditu/AdvancedCombatTracker`) and extracts `Advanced Combat Tracker.exe` to the `HintPath` before `msbuild`, **tracking ACT's latest rather than a pinned version** (rationale in §Release channels & public distribution — *Building against live ACT*). Nothing of ACT's is redistributed — CI pulls the author's canonical download.

**Build & publish (CI):** a GitHub Actions `windows-latest` workflow, on push, runs **`msbuild`** (not `dotnet build` — WPF's XAML compile requires MSBuild) and publishes the built plugin as the asset of a rolling prerelease. The **version/tag is stamped by CI** (e.g. the run number or `git describe`), never by hand or from wall-clock in code. Branch pushes run the same workflow **verify-only** (tests + compile + artifact, no publish); publishing is a `main`-only event. This publishes the **`dev-latest`** channel; the curated **`stable`** channel is a separate manual promotion (§Release channels & public distribution).

**README (player-facing) & status badges.** Now that the repo is public, the README is written for a **prospective user first**: it leads with what the plugin offers a player (the glanceable, escalating, palette-coloured timer overlay sourced from their existing ACT timers), carries a **Getting Started** quickstart (download → add to ACT → enable → check for updates) linking to the full installation guide (`docs/install.md`), and demotes internals/architecture to a short pointer to this SPEC and the backlog rather than leading with them.

The README header carries live **shields.io badges** that read GitHub directly — build status, the latest **stable** release version, and the license — plus quick links to both release channels, the SPEC, and the backlog. Because the repo is public, shields.io reads workflow/release/license state itself, so the badges **self-update with no CI stamping**: no machine-owned block, no `[skip ci]` bot commit per release, no perl-rewrite in either workflow. The **beta** channel (`dev-latest`, a rolling prerelease) is a text link rather than a version badge — shields cannot pin a specific rolling-prerelease tag's version, and with a stable channel live it isn't needed. (Earlier the pills were hand-stamped *static* badges, forced by the private repo; going public retired that machinery.)

**Installation guide (`docs/install.md`).** A standalone, user-facing guide covering the **first manual install** and ongoing updates. It documents mechanisms already specced here (self-update §"Deploy & reload", channels §"Release channels & public distribution") rather than defining new behaviour, and stays scoped to eq2auras: ACT itself is a **prerequisite that links out** to ACT's own setup docs, not re-documented. Structure: prerequisites (a working ACT + EQ2 parsing; EQ2 in borderless-windowed) → download the stable-release `eq2auras.dll` **into ACT's plugins folder** (`%APPDATA%\Advanced Combat Tracker\Plugins` — the self-updater's write target; see §Deploy & reload) → enable it in ACT → verify the tab and overlay appear → updating (the check-for-updates button + startup notify) → the beta-channel checkbox → troubleshooting (including the update-no-ops-if-installed-elsewhere case).

**Single assembly by shared-source compilation** (see Packaging): the plugin csproj `<Compile Include>`s the Core sources, and CI ships exactly one `eq2auras.dll`. No ILRepack (with its netstandard→WPF merge hazards) and no runtime dependency resolution at all — the scan-safety property holds by construction and self-update moves one file.

**Deploy & reload (in-plugin self-update):** the intended mechanism (the `ACT_Adder` pattern) is: on startup or a "check for updates" action, a **background thread** (never `InitPlugin`'s thread — a GitHub call + download must not block ACT's UI at launch) queries the selected channel's GitHub release; if its identity differs from the installed build (by identity, not version order — see §Release channels & public distribution), it downloads the DLL, strips the Windows mark-of-the-web (or ACT refuses to load it), overwrites the plugin file, and toggles `ActPluginData.cbEnabled` off→on. Needs no ACT-assigned plugin ID; works from an arbitrary URL. (This MOTW strip is the **self-updater** path; on the **first manual install** the user drops in a freshly-downloaded — and therefore MOTW-marked — DLL, and ACT prompts to unblock it at load time. The install guide tells the user to accept that prompt rather than pre-unblocking via file properties.)

The overwrite targets **ACT's plugins folder** (`AppDataFolder\Plugins`, hardcoded — `Eq2AurasPlugin.cs`), whereas the reload re-reads the plugin from its **registered path** (`PluginGetSelfData(this).pluginFile` — wherever ACT loaded it from). The manual install must therefore place the DLL in that same plugins folder, or a self-update writes bytes ACT never reloads (it re-enables the stale original in place) and the update silently no-ops. The install guide directs the drop there for this reason. (A more robust alternative — overwrite the *registered* path so the plugin works from anywhere — is a backlogged code fix; today the install location must match the updater's hardcoded target.)

**Re-enabling runs the new bytes — measured.** ACT loads plugin assemblies from bytes and **re-reads the file at enable-time**, so toggling `cbEnabled` after an overwrite runs the new build live (colour-coded-build test; see the Phase-0 resolved list above for the full evidence and the one open changed-`Core.dll` sub-case). No loader-plugin indirection and no restart-prompt is needed; the WPF payload reloads cleanly because ACT — not the plugin — performs the byte-loading.

**No token (public repo):** a public repo's release assets download unauthenticated, so the self-updater carries **no credentials** and fetches the asset's `browser_download_url` directly. There is no token to paste, store, or protect; the earlier fine-grained-PAT-in-DPAPI scheme is retired (§Release channels & public distribution).

**The loop:** edit on Mac → `dotnet test` Core locally → push → CI builds & publishes → plugin self-updates in ACT **live, no restart** (a few minutes end to end — measured working). The fast inner loop is the local Core tests; the CI round-trip is only for the WPF/ACT shell, which can only be exercised on Windows anyway.

**Escape hatch — foreclosed by going public.** While the repo was private, a **self-hosted GitHub Actions runner** on the Windows box was an opt-in faster-inner-loop option (build on push, copy the DLL straight into `%APPDATA%\Advanced Combat Tracker\Plugins`, ~1–2 min, no fetch). **Going public removes it — self-hosted runners must never attach to a public repo** (a fork PR could run arbitrary code on the personal machine). So the inner loop is the CI round-trip, mitigated by the local Core tests (which need no Windows). Even when it was available it was never a shortcut around reload: a copied DLL faced the **same DLL-lock and new-bytes-reload questions** as the self-updater's overwrite (copying a file no more runs new code live than the overwrite does).

**Teardown discipline** (see Platform facts) is what makes live reload safe: `DeInitPlugin()` must fully release windows, timers, event subscriptions, and log handles, or repeated hot-reloads leak.

### Release channels & public distribution

**Going public.** The repo is public — source and release assets both open. This is what makes installing and updating easy for people other than the author: a public repo's release assets download **unauthenticated**, so the self-updater fetches the plain `browser_download_url` and carries no `Authorization` header. There is no update token to paste, store, or protect — the DPAPI token path (`TokenStore` and its tab UI) is retired. Two public-repo consequences follow: the self-hosted-runner escape hatch (§Development & test cycle — *Escape hatch*) is **foreclosed** (self-hosted runners must never attach to a public repo), and the README uses **dynamic shields.io badges** (self-updating, no CI stamping) now that shields.io can read the repo.

**Two channels, one boolean.** Users ride one of two release tags:
- **`dev-latest`** — the rolling prerelease, republished on every `main` push (bleeding edge; the author and streaming testers).
- **`stable`** — a curated release the public rides by default.

A single persisted knob `BetaChannel` (bool, default `false`) selects the tag: unchecked → `stable`, checked → `dev-latest`. Default-`false` means the public gets stable, and the `false` default survives DCJS deserialization with no field initializer (the enum-knob rule's boolean analogue). It is one checkbox on the tab — a channel opt-in, not a multi-option picker.

**Stable is a promotion, not a rebuild.** A stable release is one specific dev build's **exact bytes**, republished under the `stable` tag — never a recompile. This is what carries "I playtested this build" through to the public: the bytes they receive are the bytes tested, not a fresh compile of the same source. Promotion is a manual `workflow_dispatch` (the author's in-the-moment "bless it" action): it takes a build version — **defaulting to whatever `dev-latest` currently holds** — fetches that build's DLL artifact from its CI run, and publishes it to the `stable` tag. Because it pins a specific version rather than "current `dev-latest`," a `main` push landing between playtest and promotion cannot silently ship an untested build; naming an older version is the rarely-used escape hatch. The stable release records the **exact source commit SHA** for traceability and, for now, carries the promoted dev build's version number (e.g. `0.1.150`). `stable` is **not** flagged prerelease — that flag is what distinguishes it from `dev-latest`. A dedicated versioning scheme (clean semver for stable vs. the dev build-number firehose, and how the DLL's baked-in version relates to the stable release name when the bytes aren't recompiled) is a **deferred decision** (§Open decisions).

**Updates target by channel identity, never by version order.** The updater installs whatever asset the selected channel's tag currently holds whenever that release's **identity differs** from what is installed — an equality check on identity (version string / asset digest), *not* a "is it newer?" comparison. This is required, not incidental: opting out of beta routinely means going *numerically backwards* (`0.1.200` → `0.1.150`), and that must install cleanly. Toggling the `BetaChannel` checkbox triggers a check against the now-selected channel, so opting out of beta pulls the user onto stable immediately even when stable is the lower number. **The identity check compares build-metadata-free version cores:** the installed side is the assembly's `InformationalVersion`, which the .NET SDK stamps with a `+<sha>` suffix (`0.1.98+362dc28…`), while the release name is bare (`0.1.98`); both sides are truncated at the first `+` before comparison, since per semver the `+build` segment is excluded from identity and the `0.1.<run>` core (unique per CI run) is the real identity token. Without this, the raw strings never match and the updater reports "update available" forever.

**Notify on startup.** On startup a background check (the same background thread the self-update uses — never `InitPlugin`'s thread) queries the selected channel's release; if its identity differs from the installed build, the plugin surfaces an "update available: v‹X›" string both on the tab (next to the update button) and in ACT's plugin status label (`pluginStatusText`). This is **notify-only** — it never auto-installs; the user still clicks "check for updates" to apply. A richer ACT toast-with-button is deliberately *not* part of this design: ACT's toast belongs to its *registry-plugin* update mechanism, which this side-loaded, self-updating plugin does not join; whether `oFormActMain` exposes a plugin-callable notification is an unverified spike (§Open decisions), not a commitment.

**Building against live ACT (ACT.exe is fetched, not vendored).** The plugin compiles against `Advanced Combat Tracker.exe`, but a public repo must not carry that third-party binary in its tree. Resolution: the exe is **untracked** (`git rm --cached` + `.gitignore`) and CI downloads it at build time from ACT's own public GitHub release (`ACTv3.zip`, `EQAditu/AdvancedCombatTracker`), extracting it to the reference `HintPath`. CI fetches ACT's **latest** release, **not a pinned version** — a deliberate choice: users auto-update ACT and run versions outside our control, so building against latest turns an ACT API break into a **loud build failure** (the signal we most want) instead of hiding it behind a stale reference that ships false confidence. Each build **records the ACT version it compiled against** — the traceability a pin would have given, without freezing the target. A cached last-known-good zip is a **network fallback only** (used when the download is unreachable), never a mask over a real compile break against a new ACT. This stays clean of reproducibility concerns because **stable ships promoted bytes, not a recompile**, so a moving build reference never alters an already-blessed artifact. *Scope:* compiling against latest catches **API-surface** breaks; **behavioral** ACT changes compile fine and still need live/canary verification (§Open decisions — the deferred canary). Separate item: `ThirdParty/ACT_English_Parser.cs` is **not part of the build** (referenced nowhere in `src/`; the plugin csproj globs only Core sources), so untracking it does not affect compilation — the open half is whether it is ours to host publicly.

### Testing strategy

- **Verification spike is the first implementation task.** A barebones plugin that subscribes to the four `OnSpellTimer*` events and polls `GetTimerFrames()`, writing the diagnostic log described above. Run it against synthetic timers and a real fight to *observe* — not guess — exactly: when ACT drops a frame (the `RemoveValue` behavior we inherit), whether `TimeLeft` goes negative or clamps at zero (which decides how the Overdue count-up is measured), and what a reset looks like in the data. This confirms the removal timing and the Overdue measurement. It also reports the **distribution of `WarningValue`** across the team's real timer set — validating the central premise that timers carry meaningful warning values before we build on it (if most lack one, escalation would flood the center via the fallback). The diagnostic-logging feature and this spike are the same code.
- **Reload validation (same early spike) — test that *new bytes execute*, not just that the lifecycle fires.** Bump a version constant, overwrite the DLL on disk, toggle `cbEnabled` off→on, and assert the **new version string is what's now running** (the decisive test). *Separately* confirm the toggle drives `DeInitPlugin()`→`InitPlugin()` cleanly with no leaked windows/timers/subscriptions — a distinct question that can pass while stale code still runs. Also test whether a separate dependent `Core.dll` blocks reload (the ILRepack premise) and settle the WPF thread model (ACT's UI thread vs. a dedicated STA `Dispatcher`). Because the payload is WPF, also confirm whether the loader-plugin fallback works at all with WPF resource/`pack://` resolution — if not (the expected outcome), restart-prompt is the WPF deploy path. Outcomes select: live self-update vs. loader-plugin vs. restart-prompt, and two-DLL vs. ILRepack (see Development & test cycle).
- **Synthetic timers for desk development.** We drive test timers without being in a raid — via ACT manual triggers and/or `FormSpellTimers.NotifySpell` — so the overlay can be developed and tuned at the desk.
- Standard unit tests for the state model / transition logic (pure functions over sequences of `(TimeLeft, WarningValue)` readings).

### Resolved by the Phase-0 spike (measured, 2026-07-01 — details in `docs/plans/2026-07-01-spike-findings.md`)

- **`SpellTimer.TimeLeft` is `int`** (whole seconds), `duration − elapsed`, **no clamp — goes negative after expiry** (observed `… 1 → 0 → −1`). Overdue lateness = `−TimeLeft` directly.
- **Removal timing — governed by the timer's own `RemoveValue` config.** The measured "~1s past zero" removal was a timer *configured* to remove at ~0 (initially misread as contradicting the `-15` default). Timers configured to linger keep reporting negative `TimeLeft` for their configured window. **Consequence: the overdue window is per-timer user config, inherited like `WarningValue` — the overlay shows LATE for exactly as long as ACT reports the timer, no floor.**
- **Events fire exactly as modeled**: `warning` at `tL = WarningValue`, `expire` at `tL = 0`; a **reset** appears as `TimeLeft` jumping back to full.
- **Live reload WORKS — no ACT restart.** Toggling the *existing* plugin entry's Enabled checkbox **re-reads the DLL from disk** (proof: ACT's own mark-of-the-web unblock prompt fires at enable time) and runs the new bytes — verified with a colour-coded build (green → overwrite → toggle → orange). The DLL is **not file-locked** while loaded (ACT byte-loads it), so overwrite-in-place works mid-session. **Self-update = the full ACT_Adder pattern: download → overwrite → toggle own `cbEnabled` → new version live.** The predicted WPF↔live-reload tension dissolved: ACT does the byte-loading, the plugin never calls `Assembly.Load(byte[])` itself. (Re-*adding* the plugin is a different code path and is rejected as a duplicate — updates go through the toggle, never Browse-add.) ACT does **not** probe the Plugins folder for a plugin's *dependencies*, and its pre-`InitPlugin` type scan resolves all field types — both facts drove the **single-assembly packaging** (see Packaging): with every type compiled into `eq2auras.dll`, no dependency resolution exists to go wrong, and self-update moves one file. (The two-DLL era's `AssemblyResolve`/byte-loading mechanics and the `LoadFrom` file-lock hazard are recorded in `docs/plans/2026-07-01-spike-findings.md`.)
- **WPF confirmed live over the game**: transparent, top-most, click-through, storyboard-animated, on a **dedicated STA thread + Dispatcher** — inside ACT's WinForms process. SDK-style `net472` + `UseWPF` builds in CI (no legacy csproj needed). One CI landmine: `System.Web.Extensions` breaks the WPF markup compiler — the self-updater must parse JSON without it.
- **Diagnostic logging is real-time**: ~109 ms measured poll cadence; JSONL is BOM-less UTF-8.

Still open (non-blocking, gathered during normal play / next phase):
- `SpellTimer.ExtraInfo` contents (per-instance identity for the feature plan — see below).
- `WarningValue` distribution across the team's real timer set (one timer type sampled so far).
- Whether ACT exposes a distinct warning color (irrelevant to us — we never inherit it).

**Surfaced by live data (slice 2), superseded at raid scale (2026-07-05 raid capture + engine decompile — `docs/act-timer-engine.md`):** timers not bound to a caster report `Combatant = "none"`, so the `(Name, Combatant)` key cannot distinguish two concurrent instances of the same timer; concurrent instances share **one `TimerFrame`** whose `SpellTimers` is a list. The adapter snapshots **every** instance into readings; display renders one countdown per key under **newest-master-governs** (§Timer identity). The slice-2 **soonest-instance-governs** rule was tuned on single-trigger idle-log tests and misread three ways: the re-fire landed inside ACT's 12s window, so it arrived as a *non-master* — the "phantom countdown" that killed the naive newest-wins policy was a tick instance, not a reset; "the whole frame dies with the soonest" was actually the no-master-remaining frame kill; and "matching ACT's native window" held only because with one master, soonest, newest, and largest coincide. At raid scale the old rule let an overdue corpse outrank a live recast for up to `|RemoveValue|` seconds — the LATE-survives-refire bug, observed 51× on 2026-07-05.

### Roadmap (later phases, same core)

1. **Timer Overlay Phase 1** — this spec.
2. **Open the knobs** — *(started, on guild feedback: the knob model with `ColorSource` and `EscalationStyle` as the first knobs, now per-group with dragged window positions and element dimensions — see §Configuration, §Timer groups, §Element dimensions)* — expose the remaining baked-in constants; the **display matrix** — `EscalationStyle` becomes a preset over a real `state → form @ zone` table (the WeakAuras conditional-display model: one data source, per-state display definitions — enabling e.g. a radial calm list); then per-timer / per-category overrides; a richer config surface (config strings, then an in-ACT editor with live preview) reading the same `Settings`; **group management** — add/remove timer groups and richer sources (category, name match) beyond the two ACT-panel-fed groups.
3. **Sharing** — import/export configuration strings, so overlay layouts travel the way timers do today.
4. **Richer elements** — more display types (icon w/ cooldown swipe, plain text, alternate bar styles), an intermediate "approaching" visual tier, animations.
5. **Hold overdue until reset** — optionally override ACT's `RemoveValue` removal so an overdue timer is *held and escalated* until the ability actually fires (a reset), for abilities a player must not lose track of. Deferred from Phase 1 (which keeps ACT's removal) because it reintroduces the cross-removal state model and a "never resets" escape hatch.
6. **Icons** — a name→icon mapping; possibly sourcing art from EQ2's own game files (never from live game memory).
7. **Parse Meter module** — *(started — designed in full as Phase 2, Part III)* replace ACT's "mini parse" window (combatant/DPS), on the same core via an encounter adapter. Its shared-substrate extractions (window base, row primitive — §The shared rendering substrate) begin the element/group convergence of item 2 incrementally — decided 2026-07-15: extractions land as concrete second consumers force their shape, never as an up-front framework.

### Open decisions

No open *design* decisions remain for Phase 1 — the two that were open are resolved:

> *Resolved — multiple simultaneous Imminent timers:* they arrange in the center escalation zone (see §The center escalation zone), most-urgent-first, as a **vertical stack** for Phase 1.

> *Resolved — overdue "escape hatch":* a non-issue for Phase 1 — an overdue timer disappears when ACT removes it at `RemoveValue`, exactly as ACT behaves today (the deferred "hold until reset" aspiration is in the roadmap).

The empirical questions that were open at design time — the reload/hot-swap mechanism and the packaging premise — are also resolved (§Resolved by the Phase-0 spike: live self-update proven, single-assembly packaging decided). All that remains open from Phase 1 is the non-blocking list under *Still open*: `ExtraInfo` contents, `WarningValue` distribution, and ACT's warning-color question.

**Open — public distribution & release channels** (§Release channels & public distribution):

> *Resolved — ACT.exe not vendored; CI tracks live ACT:* the exe is untracked; CI downloads ACT's **latest** public release at build time and stamps the version compiled against. Tracking latest (not a pin) is deliberate — it surfaces an ACT API break as a build failure rather than hiding it. Behavioral ACT breaks still need live/canary verification. See §Release channels & public distribution — *Building against live ACT*.

> *Deferred — versioning scheme:* dev stays the `0.1.<run_number>` build-number firehose; whether stable gets a hand-assigned clean semver, and how the DLL's baked-in version relates to the stable release name when the promoted bytes are not recompiled, is a dedicated discussion (its own follow-on). The stable **pointer mechanism** rides with it — the current design fetches a fixed `stable` tag by name; GitHub's prerelease-excluding `releases/latest` is an equivalent alternative, and both work with the identity-based updater.

> *Spike — ACT toast notification:* whether `oFormActMain` exposes a plugin-callable toast-with-button to a side-loaded plugin. If it does, it layers on top of the notify-on-startup status string; if not, the string stands. Not designed for here.

> *Deferred — automated canary (behavioral CI/CD gate):* an end-to-end check that replays a canned EQ2 log / captured `spike-data/` JSONL through the timer-engine pipeline and asserts the output, catching **behavioral** breaks (including ACT semantic changes) that a compile-against-latest cannot. Starts with a feasibility spike (can ACT be driven headlessly in CI at all; can the WPF plugin load there) before any design. Its own follow-on.
