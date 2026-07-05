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

### SHIPPED — placement reference grid (2026-07-05, redesigned same day on field verdict)
Full-screen click-through grid, auto-shown with move mode, beneath the overlay windows (SPEC §Moving the overlay). **v1 (1-logical-cm pitch) field-rejected same day:** screens aren't cm multiples — last column/row chopped, and no line ever marked screen center. **v2 (branch `grid-recursive-centers`): fixed 64×32 lattice**, cell size calculated from the screen (exact edge-to-edge fit at any resolution), three-tier brightness that tells you where you are — center cross brightest, four quarter-center lines second, all else faint; counts divisible by 4 keep center/quarters on lines. **Architecture decision (discussion 2026-07-05):** lives in the plugin's `Overlay/` folder with `ClickThrough`/`MoveChrome` — one-plugin/modular-features confirmed as the model (Parse Meter = a tab/module in the SAME dll, not a sibling plugin); source-level `<Compile Include>` remains the escape hatch if the suite ever splits. Carve-out note for the element/group arc: when that redesign reorganizes rendering, group the reusable overlay-framework files into a clearly-bounded folder.

### Queue
1. **Grow direction knob** — elements grow up vs down from the window anchor (people place lists above other UI; growing down there is bad). Easy win.
2. **Row spacing knob** — configurable gap between rows (today: 4 × height-ratio constant).
3. **LATE font ratio** — *diagnosed: by design* (`base × 22/13 ≈ 1.7×`, SPEC §Typography role derivation), but field says it reads too large — tuning candidate: soften the ratio or expose it.
4. **Font-size label bug** — tab label shows DIPs (pick 16 pt → label "21"). Spec'd but wrong: the label must speak the picker's unit. Fix: display points, keep storing DIPs.
5. **(bigger) ACT timer editor in our tab** — create/modify ACT's native timers from the eq2auras tab, slimmed to what the plugin cares about and smarter. Stays inside "ACT owns the data" (we'd be a config *front-end* to ACT's own timer store, not a replacement). Needs its own brainstorm; candidate to fold into the element/group arc's config surface.

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
- Phase-1 odds: feature enable/disable + diagnostics toggle on the tab; diagnostic log size/age rotation.
- Roadmap (spec §Roadmap): full config phase, sharing strings, richer elements, icons, Parse Meter.
