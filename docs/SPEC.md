# eq2auras — Design Spec

**A personal ACT overlay suite for EverQuest 2.** This document is the source of truth for what eq2auras is and how it works. It is organized as: the suite vision and architecture (Part I), then the Phase 1 feature — the Timer Overlay — in full (Part II), then cross-cutting concerns, unknowns, and the roadmap (Part III).

Status: **live and iterating.** The timer overlay is shipped and guild-verified through slice 3 (escalation, knob model, palette colors). Present-tense descriptions below describe the system as designed; anything not yet built is scoped to a phase or the roadmap.

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

Everything is one loop, run on a **poll/state tick** (target ~15–30 fps):

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
| `TimerValue` / `TimerFinalDuration` | def / instance | Total duration (post-mods) |
| `FillColor` | `TimerData.FillColor` | Element color (the only color ACT exposes) |
| `Category` | `TimerData.Category` | Grouping (future) |
| `RadialDisplay` | `TimerData` / `TimerFrame` | ACT's bar-vs-radial preference |
| `Panel1Display` / `Panel2Display` | `TimerData` | Group routing (§Timer groups) |

ACT exposes **no per-timer icon/image**, **no `CategoryData` object** (category is a bare string), and **no separate warning color** (only the threshold + an optional warning sound). Icons and per-category theming are therefore owned by eq2auras in later phases, not inherited.

### Escalation is driven by ACT's `WarningValue`

We do **not** invent thresholds. Each timer already carries its own `WarningValue` (the team sets it per-timer in ACT). Escalation pivots on it. This keeps Phase 1 honest with the "configuration in mind" principle — the threshold is real data we read.

**Escalation never relies on color alone.** `FillColor` is user-owned data — a timer may already be red or dark — so color is not a channel we control, and could even collide with the Overdue red. Escalation is always carried by **size, position (into the center zone), motion/pulse, and the LATE tag**; color rides on top as decoration, never as the signal.

### The timer lifecycle

Escalation state is derived from each timer's live `TimeLeft` versus its `WarningValue`, evaluated every tick. A timer is in one of three states:

1. **Calm** — `TimeLeft > WarningValue`. A row in the side **list**, auto-sorted soonest-to-expire, drawn as a horizontal bar (name + countdown + draining fill), colored by `FillColor`.
2. **Imminent** — `0 < TimeLeft ≤ WarningValue`. Presentation follows the **`EscalationStyle` knob**:
   - **`CenterRadial` (default, the brainstorm's Model A):** the timer is **removed from the list** (except on overflow — see §The center escalation zone) and promoted into the **center escalation zone** as a big pulsing radial pie. See the pie semantics and zone layout below.
   - **`HighlightInPlace` (Model C):** the timer **stays in the list**, escalated by its highlight outline — the same treatment overflow rows already get. The center zone stays unused for pies; overdue timers (linger-configured) render as LATE-styled rows in the list. (The original brainstorm explicitly kept both models available as configuration — this knob is that decision landing.)
3. **Overdue** — `TimeLeft ≤ 0` *while ACT still reports the timer*. The ability is *late* — a deterministic countdown is lost. Escalated further (see Overdue visual). **The overdue window is the timer's own `RemoveValue` config** — the same inherited-not-invented principle as `WarningValue`: a timer set to remove at 0 never shows an overdue state at all; a timer set to linger (negative `RemoveValue`) keeps being reported with negative `TimeLeft`, and LATE shows for exactly that configured window. Nothing on screen ever outlives the data.

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

A count-*up* pie would be odd — the pie represents the draining *warning window*, which is meaningless once time is negative — so Overdue **drops the pie** and instead shows a **pulsing, escalated alert with a "LATE" tag and a count-up of how late it is** (e.g. `LATE +5s`): red, fast pulse, strong emphasis (candidate: screen-edge flash). It conveys that timing is lost and how overdue the ability now is; it disappears when ACT removes the timer (per that timer's `RemoveValue`). Exact styling is a tunable Phase 1 constant.

Lateness is `−TimeLeft`, read directly from ACT's (measured: unclamped, negative) reporting.

**No artificial floor.** The LATE alert lives exactly as long as ACT reports the overdue timer — its duration is the timer owner's `RemoveValue` choice, not an overlay constant. (An earlier design carried a ~2s minimum-display floor; live testing showed it made remove-at-0 timers display a phantom "LATE" the owner had explicitly configured away. Field-tested verdict: never outlive the data.) A reset is naturally instantaneous: the fresh frame's live reading replaces the LATE the moment ACT reports it.

### The center escalation zone

Model A moves escalated timers out of the list and toward center — but in a real raid several timers routinely cross their `WarningValue` in the same window, so this is the normal case, not an edge case, and the arrangement is a **Phase 1 design decision**, not a deferred one. All escalated elements — Imminent pies **and** Overdue LATE alerts — share one **center escalation zone**, arranged **most-urgent first** (Overdue ahead of Imminent, then soonest-to-expire). Provisional layout (a Phase 1 constant): a vertical stack anchored near screen-center, growing outward, capped at a small count. Overflow imminent timers (lowest priority) have already left the list conceptually, but with no center slot free they **wait as escalated/highlighted rows in the side list** — visually promoted, not yet centered — and move into the zone in most-urgent-first order as slots free. This keeps "one thing screaming" in the common single-escalation case while degrading sanely when several fire together, and it resolves where Overdue alerts sit relative to Imminent pies (same zone, ranked first). **Confirmed for Phase 1: a vertical stack** — chosen to get the state handling built; the arrangement is a swappable constant we can revisit later (horizontal row, one-big-plus-smaller, etc.). Each timer group has its own center zone (§Timer groups): escalated timers converge within their group, never across groups.

### Diagnostic logging (first-class Phase 1 feature)

Because the whole thing is one "read → diff → update" loop, tapping that loop yields a complete picture of ACT's behavior and our own. eq2auras writes **structured, timestamped diagnostics** (JSON-lines or CSV) capturing per-timer readings and — especially — every state transition (calm→imminent→overdue, resets, removals), each with `TimeLeft` / `WarningValue` / `RemoveValue`. It is toggleable so it is quiet in normal play. This log is both the mechanism for the verification spike (below) and a permanent debugging tool. **Volume & rotation:** normal play records **transitions only** (optionally plus low-rate sampled snapshots); the full per-tick dump is a **spike/verbose toggle**, not the default, since 30 fps × N timers grows fast. Logs write to an **app-data path on the Windows box** (`%APPDATA%\Advanced Combat Tracker\eq2auras\logs` — *not* a repo working tree; that machine has none), with a size/age cap and rotation so they cannot grow unbounded. **Retrieval to the Mac** (where analysis happens) is via a synced/shared folder or a manual copy — or, if the self-hosted-runner escape hatch is in use, the runner ferries logs out on each run since it already touches the Windows filesystem. Without a retrieval path the spike's findings are stranded on the machine that can't analyze them, so this is part of the spike's setup, not an afterthought. The repo's `.gitignore` entry for logs matters only once a log lands on the Mac.

### Baked-in constants for Phase 1

These are the values that become knobs later (promoted one at a time into `Settings` — see §Configuration). Phase 1 fixes them:
- List size, orientation, and sort (soonest-to-expire) — window *positions* are Settings knobs now (§Moving the overlay).
- Bar styling (fill, font, colors derived from `FillColor`).
- Center-pie size; pulse animation parameters.
- Overdue visual (count-up styling, flash).
- Poll/state tick rate.
- Target display & DPI: **primary monitor, system DPI** for Phase 1 (per-monitor DPI and monitor selection are later config; stated now to preempt WPF layered-window coordinate and click-through hit-testing bugs).
- Fallback when a timer has no usable `WarningValue` (`0`, or `≥` total): escalate at a **fraction of total duration** (e.g. the last 25%), not a fixed number of seconds — a fixed default would make a short timer permanently Imminent and scales badly across timer durations. If total duration is *also* unavailable, last-resort to an **absolute threshold** (e.g. the final 10s).

### Configuration: the knob model

Every tunable behavior is a **knob**: a typed value with a baked-in default, held in one plain `Settings` object in Core. The abstraction is deliberately thin — no framework, no reflection — but it is the single source of truth every later configuration phase builds on (per-timer overrides, the visual editor, import/export sharing strings all read/write the same store).

- **Store:** `Settings` (Core, pure, serializable) persisted to `%APPDATA%\Advanced Combat Tracker\eq2auras\settings.json` via `DataContractJsonSerializer` (never `System.Web.Extensions` — breaks the WPF markup compiler). Missing file or missing fields → defaults, so old settings files survive new knobs (forward-compatible).
- **Per-group settings:** `Settings.Panels` holds one `PanelSettings` per timer group — the group's knobs plus its four window-position values (list Left/Top, center-zone Left/Top). Positions are **nullable**: DCJS materializes missing numeric fields as `0` (a real screen corner), so `null` — never zero — means "unset, use the default layout". `Parse` normalizes the list to exactly the shipped two groups. **Legacy migration runs both directions:** an old flat file seeds Panel A from its top-level knobs; on save, the top-level knobs are written mirroring Panel A, so an older build reading a newer file stays sensible.
- **Consumption:** Core policy (engine, tracker, builder, color assignment) takes the `Settings`/`PanelSettings` instance as input — keeping policy pure and Mac-testable. Renderers read display knobs the same way.
- **Surface:** minimal WinForms controls on the plugin's ACT tab, added per knob as needs arise — alongside the existing self-update controls (token entry, "check for updates"). This is deliberately **not** the WeakAuras-style editor; that later phase edits the same `Settings`.
- **Current knobs (per group):** `ColorSource` — `Palette (default) | Greyscale | ActColor`; `EscalationStyle` — `CenterRadial (default) | HighlightInPlace`; the four window positions (set by dragging — §Moving the overlay). Everything still listed under *Baked-in constants* is a future knob awaiting promotion into `Settings`.

### Moving the overlay: unlock/move mode

The overlay windows are click-through by design, so repositioning needs an explicit mode — the WeakAuras "unlock frames" pattern. A **"Move overlay windows" checkbox on the plugin tab** unlocks every overlay window at once:

- **Unlocked:** each window clears `WS_EX_TRANSPARENT` and shows move chrome — a dashed outline, a translucent fill, and a label chip naming the window ("Panel A — list", "Panel B — escalation"). The chrome is also the hit-test surface: a transparent WPF window is mouse-invisible even without the click-through style, and the fill gives empty windows (a quiet list, an idle center zone) a visible, grabbable footprint. Dragging anywhere on the window moves it (`DragMove`).
- **Positions persist** into the group's `PanelSettings` on every drag-end and again on re-lock, so a crash while unlocked loses nothing. (Drag-end saves run on the overlay's STA thread while tab-knob saves run on ACT's UI thread — `SettingsStore.Save` serializes writers.)
- **Locked (default):** chrome hidden, click-through restored.

Positions are WPF device-independent units on the primary monitor (per the Phase 1 DPI stance). A null stored position means "use the built-in default layout": Panel A's windows where they have always been, Panel B's beside/below them, non-overlapping.

### Timer colors: session-stable palette assignment

ACT's per-timer `FillColor` is user data that overwhelmingly sits at the default blue, so it fails as a visual identity. Default color policy (`ColorSource = Palette`):

- A predefined palette of **5 distinguishable, pleasant colors** (a constant list in `OverlayTheme.Palette`, guild-approved; itself a future knob).
- **Color is keyed by normalized timer NAME — and nothing else.** The color's job is to identify *the ability as players think of it*, and the name is its stable proxy: the same ability cast by different boss variants (different `Combatant`s) or under zone-categorized trigger sets keeps one color. Keying by `(Name, Combatant)` or by category would recolor the same ability across boss versions/zones — exactly the confusion this feature exists to prevent. (If a user names zone-variant triggers differently, they get different colors — that's their expressed intent, fixable by renaming.)
- Assignment is **first-fired order**: the first time a name fires in the session it takes the next palette slot, and keeps it for the **plugin-instance lifetime** — stable across wipes, re-pulls, and boss versions. Consistency is a *repeated-attempts* feature; a plugin reload (i.e. taking an update) resetting the map is accepted (one-shot kills never needed consistency). Past 5 names the palette cycles. Explicitly per-ACT-instance; never synchronized across users. The map is also **global across timer groups** — one map per plugin instance, so a dual-flagged timer keeps one color in both panels (each group still applies its own `ColorSource` to the shared slot).
- **Display identity is unchanged** — rows/dedupe still key on `(Name, Combatant)`; two mobs casting the same ability get two rows that *share* the ability's color, which is correct under this model.
- `Greyscale` uses the same assignment mechanism over a grey ramp. `ActColor` restores the timer's own `FillColor` (with the slate-soften pass). Palette/greyscale colors are designed and render as-is.

### Explicitly out of scope for Phase 1

The configuration editor; per-timer / per-category customization; import/export sharing strings; element types beyond bar + radial; game icons/art; reading the combat log directly; group add/remove UI and non-panel routing sources (two ACT-panel-fed groups ship — §Timer groups); the Parse Meter module. All are later phases on the same core.

---

## Part III — Cross-cutting

### Development & test cycle

Development happens on **macOS** (with Claude); ACT and EQ2 run on a **separate Windows machine that stays a passive test target** — no build toolchain, no dev work, nothing installed beyond ACT and this plugin. That split is forced by one constraint and enabled by one architectural response.

**Constraint:** ACT plugins are Windows + .NET Framework only (WPF, .NET Framework, and ACT itself do not run on macOS). The build must happen on Windows — but not on the dev Mac (can't) and not on the personal Windows box (kept clean), so it happens in **cloud CI**.

**Project split** (the concrete form of the core/module architecture):
- **`eq2auras.Core`** — pure logic (state model, escalation rules, per-tick snapshot types, config). Target **.NET Standard 2.0**, so it **builds and unit-tests on the Mac** with `dotnet test`. Most TDD lives here.
- **`eq2auras.Plugin`** — the Windows-only shell (`IActPluginV1`, timer adapter, WPF overlay, self-updater). Target **.NET Framework 4.7.2**. **Compiles the Core sources into itself** (shared source — see Packaging) rather than referencing `Core.dll`, so the shipped plugin is one self-contained assembly. Compiles and runs only on Windows.

**Referencing ACT:** the plugin references `Advanced Combat Tracker.exe` itself (namespace `Advanced_Combat_Tracker`) via a relative `HintPath` with `SpecificVersion=False` and `Private=False` (not copied to output). The exe is committed to `ThirdParty/` in the **private** repo so CI can compile against it without redistribution concerns.

**Build & publish (CI):** a GitHub Actions `windows-latest` workflow, on push, runs **`msbuild`** (not `dotnet build` — WPF's XAML compile requires MSBuild) and publishes the built plugin as the asset of a rolling prerelease. The **version/tag is stamped by CI** (e.g. the run number or `git describe`), never by hand or from wall-clock in code.

**Single assembly by shared-source compilation** (see Packaging): the plugin csproj `<Compile Include>`s the Core sources, and CI ships exactly one `eq2auras.dll`. No ILRepack (with its netstandard→WPF merge hazards) and no runtime dependency resolution at all — the scan-safety property holds by construction and self-update moves one file.

**Deploy & reload (in-plugin self-update):** the intended mechanism (the `ACT_Adder` pattern) is: on startup or a "check for updates" action, a **background thread** (never `InitPlugin`'s thread — a GitHub call + download must not block ACT's UI at launch) queries the GitHub release; if newer, it downloads the DLL, strips the Windows mark-of-the-web (or ACT refuses to load it), overwrites the plugin file, and toggles `ActPluginData.cbEnabled` off→on. Needs no ACT-assigned plugin ID; works from an arbitrary URL.

**Re-enabling runs the new bytes — measured.** ACT loads plugin assemblies from bytes and **re-reads the file at enable-time**, so toggling `cbEnabled` after an overwrite runs the new build live (colour-coded-build test; see the Phase-0 resolved list above for the full evidence and the one open changed-`Core.dll` sub-case). No loader-plugin indirection and no restart-prompt is needed; the WPF payload reloads cleanly because ACT — not the plugin — performs the byte-loading.

**Token at rest:** downloading a **private** repo's release asset needs a GitHub token. Use a **fine-grained token scoped to this one repo, `contents:read` only** (never a classic repo-scoped PAT, which grants write to every private repo you own), **encrypted at rest with DPAPI** (`ProtectedData`, per-user) in the plugin's local config on the Windows box.

**The loop:** edit on Mac → `dotnet test` Core locally → push → CI builds & publishes → plugin self-updates in ACT **live, no restart** (a few minutes end to end — measured working). The fast inner loop is the local Core tests; the CI round-trip is only for the WPF/ACT shell, which can only be exercised on Windows anyway.

**Escape hatch (faster inner loop):** if the CI round-trip drags, a **self-hosted GitHub Actions runner** on the Windows box can build on push and copy the DLL straight into `%APPDATA%\Advanced Combat Tracker\Plugins` (~1–2 min, no fetch). It costs a background runner service on the personal machine, so it's opt-in, not the default. (Self-hosted runners must never run on a public repo.) This only skips the CI/network round-trip — it faces the **same DLL-lock and new-bytes-reload questions** as the self-updater (copying a file in no more runs new code live than the updater's overwrite does); it is not a shortcut around the reload problem.

**Teardown discipline** (see Platform facts) is what makes live reload safe: `DeInitPlugin()` must fully release windows, timers, event subscriptions, and log handles, or repeated hot-reloads leak.

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

**Feature-plan inputs surfaced by live data:** timers not bound to a caster report `Combatant = "none"`, so the provisional `(Name, Combatant)` key cannot distinguish two concurrent instances of the same timer; and concurrent instances share **one `TimerFrame`** whose `SpellTimers` is a list — the overlay must render **all** instances in the list, not `[0]`.

### Roadmap (later phases, same core)

1. **Timer Overlay Phase 1** — this spec.
2. **Open the knobs** — *(started, on guild feedback: the knob model with `ColorSource` and `EscalationStyle` as the first knobs, now per-group with dragged window positions — see §Configuration, §Timer groups)* — expose the remaining baked-in constants, then per-timer / per-category overrides; a richer config surface (config strings, then an in-ACT editor with live preview) reading the same `Settings`; **group management** — add/remove timer groups and richer sources (category, name match) beyond the two ACT-panel-fed groups.
3. **Sharing** — import/export configuration strings, so overlay layouts travel the way timers do today.
4. **Richer elements** — more display types (icon w/ cooldown swipe, plain text, alternate bar styles), an intermediate "approaching" visual tier, animations.
5. **Hold overdue until reset** — optionally override ACT's `RemoveValue` removal so an overdue timer is *held and escalated* until the ability actually fires (a reset), for abilities a player must not lose track of. Deferred from Phase 1 (which keeps ACT's removal) because it reintroduces the cross-removal state model and a "never resets" escape hatch.
6. **Icons** — a name→icon mapping; possibly sourcing art from EQ2's own game files (never from live game memory).
7. **Parse Meter module** — replace ACT's "mini parse" window (combatant/DPS), on the same core via an encounter adapter.

### Open decisions

No open *design* decisions remain for Phase 1 — the two that were open are resolved:

> *Resolved — multiple simultaneous Imminent timers:* they arrange in the center escalation zone (see §The center escalation zone), most-urgent-first, as a **vertical stack** for Phase 1.

> *Resolved — overdue "escape hatch":* a non-issue for Phase 1 — an overdue timer disappears when ACT removes it at `RemoveValue`, exactly as ACT behaves today (the deferred "hold until reset" aspiration is in the roadmap).

The remaining open questions are **empirical, not design** — the reload/hot-swap mechanism and the ILRepack premise — and are tracked under *Unverified items to confirm* and resolved by the spike.
