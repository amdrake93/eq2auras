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

### NEXT UP — timer groups (dual panels) + drag-to-move — SPEC'D 2026-07-02, plan pending
Brainstormed as one combined design; the spec now holds it (SPEC §Timer groups, §Moving the overlay, §Configuration). Shape: N-ready groups model — `OverlayEngine` in Core builds one tracker per `PanelSettings` entry and routes by source; two groups ship, fed by ACT's `Panel1Display`/`Panel2Display` booleans (mirror ACT: both → both, neither → nowhere; `AllowPanel2` ignored). Full duplication per group — own list window, own center zone, own knobs, own dragged/persisted positions; the only global is the name→slot color map. Unlock/move mode = tab checkbox → move chrome + `DragMove`, save on drag-end + re-lock. Escalation convergence question resolved: no convergence — per-group center zones (full duplication was the explicit direction).

## Standing items
- **Raid-scale validation** — everything so far tuned via controlled single-trigger testing on an idle log; many concurrent timers + log-flooded combat is an untested regime (ACT's log-driven clock behaves differently there). No code — run it on a raid night and collect.
- Phase-1 odds: feature enable/disable + diagnostics toggle on the tab; diagnostic log size/age rotation.
- Roadmap (spec §Roadmap): full config phase, sharing strings, richer elements, icons, Parse Meter.
