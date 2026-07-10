# Backlog

Triaged feature/fix queue. Sources: guild feedback (streamed dev sessions), field testing, spec roadmap.

## From guild feedback — 2026-07-02 (voice, streamed dev session)

> **ALL THREE SHIPPED (slice 3, 2026-07-02) and guild-verified live:** session-stable palette colors (name-keyed), greyscale + ACT-color modes, escalation style radial/in-place — all live-switchable from the tab, persisted to `settings.json`. "Big kudos for the live changing."

### 1. Session-stable palette colors — spec'd (§Timer colors); palette itself SETTLED
**Palette v2, guild-approved live (2026-07-02):** sky `#56B4E9` · amber `#E69F00` · teal `#009E73` · rose `#E37DA4` · indigo `#5E6BD8` — lives in `OverlayTheme.Palette`. (In-overlay preview swatches still showing, gated by `TimerListWindow.ShowPalettePreview` — flip off once palette-colored timers ship.)
Timers should draw from a **predefined palette of N nice colors**, assigned **in the order timers first fire** within an ACT session; a recurring timer keeps its color. Stability requirement: **consistent across fights within the same ACT instance** (wipe and re-pull → same trigger, same color). Explicitly *per ACT instance*, not synchronized across users.
- Supersedes ACT `FillColor` as the default color source (most timers carry ACT's default blue anyway, which is why everything currently looks the same).
- Design sketch: bounded `key → paletteIndex` map (plugin-lifetime, first-seen order, cycles past N). Plugin reload resets assignments — acceptable v1; disk persistence only if it annoys.
- The existing slate-soften pass applies on top regardless of source.

### 1a. Greyscale mode — config
Same assignment mechanism, palette of greys. Implies the real feature is a **color-source knob**: `palette (default) | greyscale | ACT FillColor`.

### 2. Escalation style knob — config, small spec touch
Some users prefer the escalating timer to **stay in the calm list with the highlight outline** (the original brainstorm's Model C) instead of migrating to the center radial (Model A, current). Make escalation style a setting: `center-radial (default) | highlight-in-place`. Per-user setting first; per-timer override belongs to the later full-config phase.
- Engine note: urgency + highlight already exist (overflow rows use them) — in-place mode is mostly "cap = 0" plus keeping escalated rows in the list.

### → Together these pull forward roadmap phase 2 ("open the knobs"), minimally:
- A tiny persisted settings store (`%APPDATA%\Advanced Combat Tracker\eq2auras\settings.json`, DataContractJsonSerializer — NOT System.Web.Extensions) + checkboxes/dropdowns on the plugin tab.
- First knobs: color source, escalation style.

### Shipped same session (guild-verified live)
- Calm countdown text visibility fix (was accent-dark-on-dark).
- **Spark**: bright timer-colored leading edge (45% toward white, 3px) riding the bar drain. Tuning levers if revisited: width, white-blend %, glow.

### SHIPPED — slice 4 (2026-07-02, live-verified): timer groups (dual panels) + unlock/move mode
Worked straight out of the box on first live test. Two independent groups (A = ACT panel 1, B = panel 2), each a full pipeline instance — own list window, own center zone, own per-group knobs, own dragged/persisted positions; one global name→slot color map. `OverlayEngine` in Core (N-ready groups list, mirror-ACT routing); unlock/move mode via tab checkbox (chrome + `DragMove`, save on drag-end + re-lock); bidirectional settings migration preserved existing knob choices. Spec: §Timer groups, §Moving the overlay. Plan: `docs/plans/2026-07-02-slice4-timer-groups-and-move-mode.md` (third-party reviewed).

### Also shipped 2026-07-02: CI-stamped README status section
Build badge + version/date pills + quick links; machine-owned marker block restamped by every release (`[skip ci]` bot commit). First feature through the new branch-review flow end to end. Spec: SPEC §Development & test cycle. Outstanding micro-check: confirm the native Actions badge renders on github.com (fallback: static build pill in the same stamp step).

### SHIPPED — slice 6 (2026-07-02, field verification in progress): element dimensions
Settled model (SPEC §Element dimensions): **drag is for position, numbers are for size** (the WeakAuras division). Per-group numeric knobs `RowWidth`×`RowHeight` (defaults 250×26) and `RadialSize` (diameter, default 110; oval deferred; LATE card derives ~1.55×); tab numeric fields, live-apply via rebuild-once; slice 5's grip and `ListScale`/`CenterScale` retired. Plan: `docs/plans/2026-07-02-slice6-element-dimensions.md` (third-party reviewed). **First field finding, fixed same day:** the text-fit floor on row height silently rendered rows taller than configured — tab and screen disagreed. Reversed (`fix-row-height-floor`): the configured dimension always wins, oversized text clips ("never override the knob" — SPEC §Element dimensions records the reversal). **Reversal field-verified** ("perfect" — 64pt font clips to illegibility as configured; radial names truncate with ellipsis at small widths). Residual sweep items (persistence across restart, palette wrap at 16, no-grip chrome) ride along with normal play; raid-scale validation remains the standing item.
**Direction captured (discussion 2026-07-02, not in this slice):** the timer element has display *forms* (row/radial/LATE); `EscalationStyle` is secretly a preset over a `state → form @ zone` matrix — making that table real is the next conceptual step toward per-timer config (now in SPEC §Roadmap item 2).

**Reviewer plan-watch items** (3rd-party spec review, 2026-07-02 — the plan review will check): the grip retirement is a **deletion sweep** enumerated with addition-level rigor (Settings/PanelSettings scale fields + clamps + tests, both windows' grip handlers/ProposedScale/preview/CurrentScale/persist-scale params, OverlayHost scale plumbing, MoveChrome grip, `VisualStyle.Scale` and every `× Scale`); enumerate radial-derived geometry like slice 5's six text roles (pie-name MaxWidth 190 ≈ 1.73×RadialSize, center margins 10, LATE padding); text-fit floor = a real measure (`FontFamily.LineSpacing × FontSize` + named padding constant) at style-resolution time; NumericUpDown Min/Max = the shared `Settings` bounds constants, also enforced in `Normalize`; live script absorbs slice 5's leftover verification (palette swatches/wrap, per-panel font, persistence); rename `TimerRowVisual`'s private `RowWidth`/`RowHeight` constants (collide with the new knob names → `DefaultRowWidth`/`DefaultRowHeight`).

### SHIPPED (pending remaining live verify, folded into slice 6) — slice 5: customization knobs — 2026-07-02
Three knobs (SPEC §Timer colors, §Typography, §Moving the overlay): **custom palette** (global, variable 1–16 colors, swatch + ColorDialog UI, reset button, render-as-is); **per-panel font** (family + base size via native FontDialog; text roles derive proportionally from base); **per-window scale** (corner resize grip in unlock mode, geometry-only — text never scales, null = 1.0, clamp 0.5–2.5). Design decisions: scale excludes text deliberately (font owns readability); greyscale ramp stays fixed; visuals rebuild once on scale/font change (retain-elements rule holds per-tick).

**Reviewer plan-watch items** (3rd-party spec review, 2026-07-02 — the plan review will check): FontDialog point→DIP conversion (store DIPs); enumerate all SIX text roles incl. LATE-name (12 — decide its base derivation explicitly); grip drag must not trigger `DragMove` (`e.Handled = true`); font/scale changes rebuild retained visuals once (constructor-baked constants — rebuild on knob change only, never per tick; pulses restarting once is accepted); scale every geometry constant (RowWidth/Height, drain math's `RowWidth - 2`, PieDiameter, pie-name MaxWidth 190, LATE width 170, margins, XAML Widths 260/200); `ColorPolicy.Resolve` takes the palette as a parameter and the built-in constant renames to `DefaultPaletteArgb` (avoid Settings.PaletteArgb name collision); ColorDialog is alpha-less — arrives 0xFF like the built-ins, add no alpha handling. Raid-scale validation remains the standing no-code item.

## From Alex — 2026-07-05

### SHIPPED + FIELD-VERIFIED — placement reference grid (2026-07-05, redesigned same day on field verdict; v2 verified same day — "looks good", first casualty: Alex's freehand layout)
Full-screen click-through grid, auto-shown with move mode, beneath the overlay windows (SPEC §Moving the overlay). **v1 (1-logical-cm pitch) field-rejected same day:** screens aren't cm multiples — last column/row chopped, and no line ever marked screen center. **v2 (branch `grid-recursive-centers`): fixed 64×32 lattice**, cell size calculated from the screen (exact edge-to-edge fit at any resolution), three-tier brightness that tells you where you are — center cross brightest, four quarter-center lines second, all else faint; counts divisible by 4 keep center/quarters on lines. **Architecture decision (discussion 2026-07-05):** lives in the plugin's `Overlay/` folder with `ClickThrough`/`MoveChrome` — one-plugin/modular-features confirmed as the model (Parse Meter = a tab/module in the SAME dll, not a sibling plugin); source-level `<Compile Include>` remains the escape hatch if the suite ever splits. Carve-out note for the element/group arc: when that redesign reorganizes rendering, group the reusable overlay-framework files into a clearly-bounded folder.

### SHIPPED + FIELD-VERIFIED — slice 7: QoL knobs — 2026-07-05
Live script passed: grow-flip + grow-up stacking work, spacing flat and correct (0 = touching), LATE respects font/size (bold + all-caps kept deliberately — urgency by style, not size), font label matches the picked points, regression clean. Field decisions: **escalation cap (3 center slots) stays** — 4 simultaneous escalations is an extreme case, queue-into-slots handles it; **mid-drag expiry test waived** — the seam is drag-suppressed compensation with self-correcting drag-end persistence, not worth choreographing.

## From raid-night analysis — 2026-07-05 raid, analyzed 2026-07-06/08 (`spike-data/2026-07-05/`)

First real raid capture (standing item "raid-scale validation" — first pass done). Overlay logs + full game log cross-referenced; ACT engine decompiled to explain the findings (`docs/act-timer-engine.md`).

### SHIPPED (v0.1.90, 2026-07-08) — LIVE VERIFY AT SUNDAY RAID 2026-07-12 — newest-master governing rule
**Merge-gate script: ALL THREE CASES PASSED SOLO (2026-07-08)** via the native-window double-click trick (see `docs/act-timer-engine.md` §Manual triggering): (1) master recast while LATE → cleared instantly to a full bar; (2) clicks inside the 12s window → non-master, no reset, window-extension behavior confirmed by Alex deriving the engine rule live; (3) checkbox-toggle sessions with no activity → log file deleted on every unload. Logs dir wiped clean of pre-fix zero-byte artifacts.
**Remaining for the Sunday 2026-07-12 raid** (the one thing solo can't prove): non-master suppression at raid scale — Blanket's 6s tick stream staying invisible under real stacking. Capture spike JSONLs (schema self-verifies via `master` + `instances`) and ferry to `spike-data/2026-07-12/`. **If the debug-mode branch ships first: turn Debug mode ON before the raid** — the default events-only stream doesn't carry the per-poll instance data this verification needs.
**The raid bug:** timers already LATE in the escalation window did not reset to the list when the ability re-fired (observed 51×; Soul Paralysis cycled through it for 11 straight minutes). Root cause: soonest-instance-governs let an overdue corpse instance outrank a live recast for up to `|RemoveValue|` seconds, and DoT-tick (non-master) instances stacked into the display pipeline. Fix (spec + code on branch, plan `docs/plans/2026-07-08-newest-master-governs.md`): **masters only; newest `StartTime` governs; non-masters diagnostics-only** — "master timer dictates all, let ACT do the work" (the per-instance flag pre-applies `OnlyMasterTicks` config; `Modable` mods likewise pre-applied, so timer mods always work). Ride-alongs: spike JSONL gains `master` flag; frame-event records log largest-master value + instance count (fixes misattributed `warning tl=-12` records; `removed` = null value + count by construction); zero-byte log files deleted on close. Explicitly NOT needed: ACT-side trigger reconfiguration for DoTs.

**Reviewer implementation-watch items** (3rd-party spec review, 2026-07-08 — the plan/code review verifies each landed): `EscalationTracker.Tick` selection becomes masters-only → newest `StartTime` → tie-break larger `TimeLeft`, and the stale soonest-governs engine comment (lines 28-32) goes with it; `TimerReading` gains the master flag + `StartTime` (carries neither today), captured in `TimerProbe.OnPoll`; `TimerSnapshotRecord` gains `master`; `LogFrameEvent` switches to largest-master (null-safe for `removed`) + live-instance count; `JsonlLogWriter.Dispose` deletes never-written files; **non-masters still reach the readings** (adapter snapshots every instance per SPEC) — the masters-only filter lives in the display pipeline (Core), never the adapter.

**Merge-gate live script** (attach to the code when it lands): (1) recast case — custom trigger, `RemoveValue` −15, let it go LATE ~5s, re-fire >12s after the first line → LATE clears instantly to a full Calm bar; (2) tick case — re-fire within 12s → display does NOT reset, JSONL shows the second instance `master: false`; (3) empty log — session with no timer activity, unload plugin → no zero-byte `spike-*.jsonl` left behind.

### Smaller findings (queued, not in the fix branch)
- **LATE cards are uncapped + end-of-fight pileup** — pies cap at 3 center slots but the LATE stack is unbounded (4 simultaneous observed); when a fight ends every lingering timer goes LATE together for up to ~16s of dead-mob noise. Needs a design think (cap? post-combat suppression?).
- **Trigger coverage gap** — after 21:29 the raid fought 75+ min with zero configured trigger hits (different encounters). Alex's trigger-authoring call, not plugin work.
- Poll loop health confirmed at raid scale (2 sub-1.2s hiccups all night, none during combat).

### NEXT UP — grow-up row ordering fix (field finding, 2026-07-05)
Grow-up lists render soonest-at-top, but **the soonest timer should sit nearest the anchored edge** (grow-down already does this by accident: anchor = top). Fix: `RenderRows` reverses visual order under `GrowDirection.Up` + one spec sentence in §Window growth ("row order anchors: soonest-to-expire sits at the anchored edge"). Small fix branch, fix-flow with spec review pause.

### Tab UI cleanup (Alex, 2026-07-05: "needs some work soon, but fine for now")
~700px of stacked absolute-positioned controls after seven slices. Real fix = the element/group arc's config surface (per-window sections); interim candidate if it hurts sooner: two-column layout + collapsible group boxes.
Items 1–4 of the 07-05 queue in one slice (SPEC §Window growth, §Element dimensions, §Typography):
1. **Grow direction** — **per-WINDOW** (`ListGrowDirection`/`CenterGrowDirection`, Down=0 default | Up): grow-up anchors the bottom edge (persisted vertical coordinate = the anchored edge; runtime compensates Top by the height delta). Alex: deliberately per-window, never per-panel — first knob of the window-configuration trajectory; "don't code ourselves into a corner, keep the goal in mind."
2. **Row spacing** — `RowSpacing`, raw DIPs, flat, configured-wins (the `4 × height-ratio` derivation retires); 0 = touching, never overlap; clamp 0–50.
3. **LATE typography** — respects the font as-is (the 22/13 boost retires; only radial seconds keep their proportional boost). Future: a LATE-dedicated window configures its own font like any other window.
4. **Font label** — displays points (the picker's unit), storing DIPs unchanged.

**Reviewer plan-watch items** (3rd-party spec review, 2026-07-05 — the plan review will check): drag-end + re-lock persistence write the **anchored edge** (`SaveAllPositions` and the drag-end callbacks read raw `window.Top` today — under Up they persist `Top + ActualHeight`); define who wins when content resizes mid-drag (suppress height compensation while dragging, reconcile at drag-end); DCJS rules — `GrowDirection.Down = 0`, `RowSpacing` nullable (missing → null = 4, never a legal-looking 0); `LateName` is **changed behavior** (12/13 → base) — test expects `base`, live check eyeballs the whole LATE card.

### Queue (remaining)
5. **(bigger) ACT timer editor in our tab** — create/modify ACT's native timers from the eq2auras tab, slimmed and smarter. Stays inside "ACT owns the data" (config *front-end* to ACT's own timer store). **Held (Alex, 2026-07-05): after the full WeakAuras investigation and the truly-modular elements/custom-windows work — the editor needs all that custom machinery to be worth building.**

### NEXT ARC — the element/group model (Alex's pre-brainstorm seed, 2026-07-02 session close)
"I want to take this way further. I want to begin designing things as elements." Still 100% ACT timer data underneath. The seed, in his words:
- **Timer elements as first-class:** what we call a row or radial display becomes a *Timer element* with its own configuration (form: radial/row; size — we have these knobs, but panel-based, not element-based).
- **Groups = the windows/panels, user-created:** instead of driving placement from ACT's Panel A/B booleans, keep **our own assignment** of existing timers to custom panels — create N panels, assign timer elements to them.
- **Hierarchical config + inheritance:** likely lock element style within a group — the group/window carries settings applied to its children elements. "This will require some serious brainstorming with potentially mocking designs and diagrams for hierarchical data and inheritance."
- **Research prep:** get hold of decent **WeakAuras documentation** to guide the model (public wiki/docs — a web-research pass can front-load the brainstorm).
Context it builds on: SPEC §Timer groups already anticipates richer sources; the display-matrix direction (`state → form @ zone`, Roadmap item 2) slots inside this arc. Open pre-brainstorm questions: do ACT's panel booleans survive as one assignment source or retire; element-level vs group-level vs global knob layering; identity for timer→panel assignment (name-keyed, like colors?).

### Release channels — model settled (spitballed 2026-07-02, awaiting its slice)
Trunk-based, two channels, **artifact promotion** (never rebuild what ships stable — "test what you ship"):
- **Unstable = `main` HEAD** — today's `dev-latest` rolling prerelease, renamed in spirit. The guild is the beta jury post-merge; pre-merge testing stays the manual branch-artifact hatch.
- **Stable = promoted artifact.** `workflow_dispatch` (input: `v0.x.y`) reads the current `dev-latest` release, **copies its exact asset** (the field-tested bytes), tags the SHA it was built from, publishes a non-prerelease named "v0.x.y (build 0.1.NN)". No build step. Stable pointer = GitHub `releases/latest` (excludes prereleases by definition).
- **Updater channel knob:** `Channel — Unstable (0, default: preserves guild behavior on old settings) | Stable` → unstable pulls `releases/tags/dev-latest`, stable pulls `releases/latest`.
- **Recorded caveats:** promotion only reaches the *current* dev-latest (older verified builds → Actions artifact, 90-day retention — escape hatch, not the flow); promoted bytes self-report their build version, the release name carries both; stable hotfix policy = verify HEAD, promote it (branch-off-tag exists in git if ever truly needed, build nothing for it).
- Needs when picked up: spec amendment (§Development & test cycle), promote workflow, updater knob + tab dropdown.

## Standing items
- **Raid-scale validation** — everything so far tuned via controlled single-trigger testing on an idle log; many concurrent timers + log-flooded combat is an untested regime (ACT's log-driven clock behaves differently there). No code — run it on a raid night and collect.
- Phase-1 odds → IMPLEMENTED ON BRANCH, PENDING MERGE + LIVE VERIFY (branch `qol-debug-mode-log-retention`, 2026-07-08): **Debug mode** (tab checkbox, default off = lifecycle events only; on = full per-tick dump) + **rolling log retention** (14 days / 200 MB at load; debug mode on suppresses the sweep). Feature enable/disable **dropped** — ACT's own plugin checkbox is that switch (Alex). Plan: docs/plans/2026-07-09-debug-mode-log-retention.md.
  **Reviewer plan-watch items** (3rd-party spec review, 2026-07-09 — the plan/code review verifies each): `InitPlugin` order must flip — today it constructs the log writer *before* loading settings (`Eq2AurasPlugin.cs:32-33`), and the sweep needs the persisted `DebugLogging` before the log file opens (load-settings → sweep → open-writer); `TimerProbe` has no `Settings` access today — wire the per-poll flag check (ctor param or callback; checkbox + poll share ACT's UI thread, no extra sync); pin sweep edge semantics — age source (last-write time vs filename timestamp), oldest-first deletion until both bounds hold, and a lone newest file over 200 MB survives (newest always wins).
- Roadmap (spec §Roadmap): full config phase, sharing strings, richer elements, icons, Parse Meter.
