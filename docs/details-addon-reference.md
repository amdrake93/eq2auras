# Details! Damage Meter — architecture reference

How the WoW addon Details! (the "generic meter windows" gold standard) is built, read from its
actual source (shallow clone of `Tercioo/Details-Damage-Meter`, retail `main`, analyzed
2026-07-09). This is design input for the eq2auras Parse Meter module — ACT replaces Details'
capture layer; the display architecture is what we're here to learn. The repo's own
`API General.txt` / `API Custom Displays.txt` / `API UI.txt` are its real docs; the wiki is thin.
The full research report with complete file:line citations is archived at
`docs/research/2026-07-09-details-deep-dive-report.md`.

## The pipeline

Strict three stages with two decoupling points: a **dirty flag** between capture and display,
and a **global pull-based refresh ticker** — the display samples, capture never pushes UI work.

```
combat log event
  → parser (token → handler dispatch table; disabled categories cost zero)
  → combat object (the current segment): actor containers → actor accumulators → spell tables
      writes set container.need_refresh = true
  → global ticker (default 0.20s, user-tunable; per-content "performance profiles" can drop
      to 1.0s and disable capture categories)
  → per-window RefreshData → attribute class RefreshWindow:
      sort actors by the sub-attribute's key → total/top → bind visible ranks to pooled rows
```

Capture accumulates in place at event rate (no event queue, no per-hit allocation beyond
first-seen actors/spells, weak GUID→actor caches). Display cost is O(visible rows) per tick;
only sorting is O(actors log actors).

## Segments / combat model

**A combat IS a segment.** A combat object holds four actor containers (damage / heal /
energy / misc, plus a fifth placeholder for custom displays), identity metadata (monotonic
counter, start/end times, enemy/boss info, segment type), time-series chart data, and death
logs.

Three data slots:

| Slot | Segment id | Meaning |
|---|---|---|
| current | 0 | the live combat |
| overall | −1 | fold of qualifying past segments |
| history | 1..N | ring buffer, newest first, default cap 25 |

- **Overall is a fold, not a dual-write**: at segment close, `overall = overall + combat` via
  an actor-by-actor merge (`__add` metamethod). Whether a segment qualifies is a separate
  policy (boss-only, min combat time…), as is when overall resets (new boss, new run, logoff).
  (Contrast: ACT's zone-"All" *is* a live dual-write.)
- **Typed segments** (boss / trash / M+ / PvP…) drive eviction policy: history cap, optional
  "only keep latest trash", per-boss wipe cap that evicts the *least-progressed* wipe.
- **Scar tissue worth learning from**: windows hold direct combat-object references, so
  Details needs a "Freeze" state (window blanks with a disconnect icon), `__destroyed`
  defensive checks, and a scan-all-windows-on-eviction pass. Resolve segment-id → data at
  render time instead — this is our "never outlive the data" rule restated.

## Windows ("instances") — the part to emulate

An instance = one meter window, in a global array (default cap 5, raisable). Closed windows
keep their config and are recycled. **Each window independently selects four orthogonal
knobs:**

| Knob | Values |
|---|---|
| Segment | overall / current / history-n |
| Attribute | damage / heal / energy / misc / custom |
| Sub-attribute | metric within the attribute (window remembers the last one per attribute) |
| Mode | solo / group / all — actor filter; solo & raid modes hand the window body to a plugin |

Windows are **fat, dumb viewports**: ~150 persisted settings per window (position/scale,
click-through, hide in/out of combat, context rules, auto-switch by role, switch-to-and-back
tables, total bar, "following" bar that pins your own row, grow/sort direction, toolbar
anatomy, the entire skin surface). All data lives in combat objects that don't know windows
exist — N windows over the same data are nearly free.

**Linking** is two cheap features, nothing deeper: (a) edge **snapping** — windows dock and
then move/resize/re-skin as a group (discovered by walking snap links); (b) a global
"segments locked" toggle syncing segment selection across windows.

## Attributes / sub-attributes — one pipeline, N metrics

Taxonomy: damage (damage done, DPS, damage taken, friendly fire, killing blows, enemies,
avoidable damage…), heal (healing done, HPS, overheal, absorbs…), resources, misc (CC break,
res, interrupt, dispel, deaths, defensive cooldowns, buff/debuff uptime), custom.

**The key mechanic: a sub-attribute is a key name on the actor accumulator.** The refresh
selects `keyName` (1→`total`, 3→`damage_taken`, …), then generic machinery takes over: sort
container by `actor[keyName]`, compute total and top, bind visible ranks to rows. Adding
"damage taken" is a sort key, not a new pipeline.

**The universal row contract** — the row renderer only ever sees:
`{name, class/color, icon, value, formatted texts, percent}` (+ per-window percent mode:
vs-total or vs-top). That contract is why one rendering path serves 30+ metrics; even the odd
metrics that don't map to an actor key build temp `{name, value, class}` triples and feed the
same row loop.

Actor accumulators also carry per-spell stat records (hit counts, min/max/avg by normal/crit,
per-target totals) powering drill-down/breakdown windows and tooltips, plus activity-time
bookkeeping (idle actors stop accruing "active time", so DPS can be per-active-time or
per-elapsed, user-selectable). An optional "real time DPS" module samples every actor's total
at 0.10s into a 5s sliding ring buffer for rolling DPS.

## Custom displays — the extensibility pattern

A custom display is a saved object that is a **first-class peer of built-in metrics** in the
window UI (same segment selector, same rows, same skin). Two tiers:

1. **Declarative** (no code): pick attribute + source + target + spell-id filters — "damage
   done by X to Y with ability Z". Covers most shipped customs.
2. **Scripted**: a *search script* compiled once, sandboxed, cached, invoked per refresh as
   `search(combat, container, window) → (total, top, count)`. The script fills a
   **CustomContainer** — `AddValue(actorRef, amount)` accepts *anything with a name*: real
   actors or synthetic `{name="Jeff"}` rows, cloned into the standard row contract. Optional
   hooks: `total_script` / `percent_script` (format the displayed strings — e.g. render
   seconds as "1m 30s") and `tooltip` (row hover).

Windows select a custom display as attribute=custom + sub-attribute=index. Addons install
custom objects programmatically (dedupe by name, keep highest version).

## Rendering + refresh economics

- **Retained row pool per window**, lazily grown to what fits (hard cap 50), never destroyed.
  Each tick *re-binds* rows to ranks — never rebuilds. (Our "retain elements, animate
  properties" rule, independently evolved.)
- **Scrolling = a rank window**: only visible ranks refresh; mouse-wheel shifts the range.
- **No row-reorder animation** — rows are fixed slots; an actor overtaking simply binds to a
  higher row next tick. Nobody misses it. What animates: **bar width** lerps toward a target
  percent (~33%/sec with distance-based acceleration, clamped so a bar never visually crosses
  the one above it; re-targeted each tick), and rows **fade** in/out via a pluggable
  animation registry.
- **Number formatting**: selectable formatter family (raw, K/M abbreviations, locale
  groupings); bar text is template-driven (`{data1}. {data3}{data2}` placeholders).
- Wart to avoid: sort ties are pre-broken by seeding every total with a random epsilon,
  forcing `floor()` on every displayed value. Use a proper tie-break comparator.

## Skinning surface (inventory of per-window customization)

Rows: height, fg/bg/overlay/hover textures, color by class vs fixed (fg/bg independent),
alpha, backdrop/border, spacing/offsets, icon (class/spec, grayscale, mask, none), rank
number, left/right text (font/size/color/shadow, custom templates, which of
value/per-second/percent, separators, percent mode). Window: background, wallpaper
(anchor/crop/tint), title bar, borders, rounded corners, scale, strata, grow/sort direction,
right-to-left bars. Chrome: toolbar side + which icons, auto-hide menus, footer statusbar
hosting micro-widgets. Behavior-adjacent: total bar, following bar, hide in/out of combat,
click-through, context rules.

**Skins = named copies of the whole per-window blob** + art; users export/import window
setups; third-party skins are cached into saved config so they survive their source addon
being disabled.

## Plugin API (brief)

`InstallPlugin(type, …)` with types: RAID/SOLO (take over a window's body in that mode),
TOOLBAR (icon+menu), STATUSBAR (footer micro-widgets). An event bus for consumers
(`COMBAT_PLAYER_ENTER/LEAVE`, `ENCOUNTER_START/END`, data reset/segment-removed, per-window
change events). A typed data API wrapper besides raw object access. Plugins ship custom
displays via the install call.

## Portable patterns vs accidents vs pain points

**Port these:**
1. The universal row contract (`{key, label, color, icon, value}` + total + top + format
   hooks) — one rendering pipeline, N pluggable sources.
2. Windows as fat dumb viewports over shared data; persist the whole blob; skins are named
   copies of it.
3. Pull-based rendering on one global clock + dirty flags from the write path; capture never
   touches UI.
4. Segment ring buffer with typed segments and eviction *policies outside the data*; overall
   as an explicit fold with separate qualify/reset policies.
5. Reference data by handle, resolve at render time, degrade explicitly when gone.
6. Sub-metric = key selection over one accumulator record (registry, not if-chains).
7. Custom displays as first-class peers; declarative tier first, script tier later with the
   same `search → (total, top, count)` + CustomContainer contract.
8. Row pool + re-bind; animate bar width and row fades only.
9. Linking = snapping + segment-lock; nothing deeper needed.
10. Per-category capture/aggregation kill-switches and update-rate profiles.

**WoW accidents (skip):** pet resolution via tooltip scanning (ACT resolves pets),
CLEU token zoo, boss-detection/M+ taxonomy (ACT's zone/encounter model replaces it — a light
boss-vs-trash segment *type* tag is still worth keeping), class colors/spec icons (eq2auras
already keys color by normalized name), 3D model overlays.

**Their pain (avoid):** 8k-line monoliths with attribute dispatch copy-pasted as if-chains in
3+ places (use a display registry); custom displays bolted on late as "attribute 5" special
cases everywhere (design customs in as just-another-source from day one); live object format
used directly as storage format (separate persistence DTOs); epsilon-seeded sort keys.

## ACT ↔ Details mapping (for the brainstorm)

| Details | ACT / eq2auras |
|---|---|
| combat object (segment) | `EncounterData` |
| actor | `CombatantData` |
| spell table | `DamageTypeData` / `AttackType` breakdown |
| history 1..N | `ZoneData.Items` (mind ACT's `CullEncounters` — handle, don't pointer) |
| current (0) | `ActiveZone.ActiveEncounter` while `InCombat` |
| overall (−1) | ACT's zone-"All" (live dual-write) or our own fold with our inclusion policy |
| parser dirty flag | our `AfterCombatAction` handler setting a dirty bit / feeding accumulators |
| global 0.2s ticker | existing WPF render clock at a lower divider |

Genuinely new things to build: the window/source/segment knob model, the row contract + row
pool, and the source registry with a declarative custom tier. See `act-parse-engine.md` for
the data-side hook points.
