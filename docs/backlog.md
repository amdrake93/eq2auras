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

### NEXT UP — slice 5: customization knobs — SPEC'D 2026-07-02, plan pending
Three knobs (SPEC §Timer colors, §Typography, §Moving the overlay): **custom palette** (global, variable 1–16 colors, swatch + ColorDialog UI, reset button, render-as-is); **per-panel font** (family + base size via native FontDialog; text roles derive proportionally from base); **per-window scale** (corner resize grip in unlock mode, geometry-only — text never scales, null = 1.0, clamp 0.5–2.5). Design decisions: scale excludes text deliberately (font owns readability); greyscale ramp stays fixed; visuals rebuild once on scale/font change (retain-elements rule holds per-tick).

**Reviewer plan-watch items** (3rd-party spec review, 2026-07-02 — the plan review will check): FontDialog point→DIP conversion (store DIPs); enumerate all SIX text roles incl. LATE-name (12 — decide its base derivation explicitly); grip drag must not trigger `DragMove` (`e.Handled = true`); font/scale changes rebuild retained visuals once (constructor-baked constants — rebuild on knob change only, never per tick; pulses restarting once is accepted); scale every geometry constant (RowWidth/Height, drain math's `RowWidth - 2`, PieDiameter, pie-name MaxWidth 190, LATE width 170, margins, XAML Widths 260/200); `ColorPolicy.Resolve` takes the palette as a parameter and the built-in constant renames to `DefaultPaletteArgb` (avoid Settings.PaletteArgb name collision); ColorDialog is alpha-less — arrives 0xFF like the built-ins, add no alpha handling. Raid-scale validation remains the standing no-code item.

### Release channels (Alex's stated direction, 2026-07-02)
An **unstable channel** (rolling prerelease from dev branches — one-button beta/dev testing before merge) and a **stable release artifact** elsewhere for other users. Needs: second rolling tag published from branch CI, a channel knob in the self-updater, spec amendment (§Development & test cycle). Until then, pre-merge field testing = download the branch CI artifact and overwrite+toggle manually.

## Standing items
- **Raid-scale validation** — everything so far tuned via controlled single-trigger testing on an idle log; many concurrent timers + log-flooded combat is an untested regime (ACT's log-driven clock behaves differently there). No code — run it on a raid night and collect.
- Phase-1 odds: feature enable/disable + diagnostics toggle on the tab; diagnostic log size/age rotation.
- Roadmap (spec §Roadmap): full config phase, sharing strings, richer elements, icons, Parse Meter.
