# Details! deep dive — raw research report (2026-07-09)

**What this is:** the verbatim output of the research agent that produced
`docs/details-addon-reference.md`. The reference doc is the maintained distillation; this
archive preserves the complete citation-level detail (file paths, function/table names, and
line numbers against a shallow clone of `Tercioo/Details-Damage-Meter` at retail `main`,
cloned 2026-07-09 — line numbers drift as upstream moves; re-clone and grep by name).

**Review status:** third-party re-derivation review 2026-07-09 — approved; ~10 mechanical
claims spot-checked against a fresh clone, all landed. Claims the review sampled but did not
individually verify were re-verified against the clone on 2026-07-09: `rows_max = 50`
(`classes/class_instance.lua:2403`) and `animation_speed = 33` (`functions/profiles.lua:959`).

---

# Details! Damage Meter — Architecture Deep-Dive
*(source analyzed from a shallow clone of Tercioo/Details-Damage-Meter, retail `main`. All claims verified in source; the wiki is thin — the repo's own `API General.txt`, `API Custom Displays.txt`, `API UI.txt` are the real docs.)*

---

## 1. Big-picture pipeline

Details is a strict three-stage pipeline with two decoupling points: a **dirty flag** between capture and display, and a **global refresh ticker** that pulls rather than pushes.

```
WoW combat log (CLEU event)
   │  core/parser.lua — token dispatch table, one handler per event type
   ▼
Combat object (the current segment)         ←— storage is pure Lua tables
   ├─ combatObject[1..4] = actor containers (damage/heal/energy/misc)
   ├─ actor objects (per-entity accumulators) → spell containers → spell tables
   └─ container.need_refresh = true          ←— dirty flag set on every write
   ▼  (pull, every Details.update_speed seconds, default 0.20s)
Global ticker → per-instance RefreshData() → attribute class RefreshWindow()
   ├─ sort actor container by the sub-attribute's key
   ├─ compute total / top
   └─ bind visible ranks to pre-created row frames (RefreshLine)
```

**Capture** — `core/parser.lua` (~7,900 lines).
- Entry point: `Details222.Parser.OnParserEvent()` (parser.lua:7041) reads `CombatLogGetCurrentEventInfo()` and dispatches via `token_list[token]` — a plain table mapping `"SPELL_DAMAGE" → parser.spell_dmg`, `"SPELL_HEAL" → parser.heal`, `"SPELL_AURA_APPLIED" → parser.buff`, etc. No if-chains in the hot path; one table lookup.
- Capture is **toggleable by category**: `Details.capture_types = {"damage","heal","energy","miscdata","aura","spellcast"}` (parser.lua:5048). `Details:CaptureDisable("damage")` literally sets the damage tokens in `token_list` to `nil` — disabled categories cost zero. There are alternate dispatch tables for special contexts (`OnParserEventPVP`, `OnParserEventOutOfCombat` at parser.lua:7086, which whitelists only summon/damage from group members).
- Handlers (e.g. `parser:spell_dmg`, parser.lua:757) resolve source/target to actor objects through **weak-table GUID caches** (`damage_cache`, `damage_cache_pets`, `damage_cache_petsOwners`, parser.lua:103-105), falling back to `actorContainer:GetOrCreateActor(serial, name, flags)` (classes/container_actors.lua:890) which classifies the actor (player/pet/NPC, class, group membership) from CLEU flags at creation time. Then they add straight into the actor's accumulators and its spell table (`classDamageSpellTable:Add`, classes/class_spelldamage.lua:78), and set `container.need_refresh = true` (parser.lua:984).

**Storage** — `classes/class_combat.lua`, `classes/container_actors.lua`, `classes/container_segments.lua`. Everything is plain tables + metatable "classes"; the same structures are written to SavedVariables for persistence (functions/savedata.lua:107 saves `Details.tabela_historico` wholesale).

**Display** — `classes/class_instance.lua` (windows), per-attribute display classes (`classes/class_damage.lua` etc.), row frames built in `frames/window_main.lua`. One global ticker: `Details.atualizador = C_Timer.NewTicker(updateInterval, Details.RefreshAllMainWindowsTemp)` (core/gears.lua:214) → `Details:RefreshMainWindow(-1)` (core/control.lua:1861) loops all enabled instances.

---

## 2. Segments / combat model

**A "combat" IS a "segment".** `classCombat:NovaTabela()` (class_combat.lua:1344) constructs a combat object holding:
- `combatObject[1..4]` — four actor containers (`DETAILS_COMBAT_AMOUNT_CONTAINERS = 4`): damage, heal, energy, misc. `combatObject[5]` is a placeholder container for custom displays.
- Identity/metadata: `combat_counter` (monotonic UID), start/end wall-clock dates, `start_time`/`end_time` (monotonic, for duration: `GetCombatTime()` at class_combat.lua:947), enemy name (`contra`), boss info, segment type, PvP flags, boss health, phase data, time-series chart tables (`TimeData`), death logs (`last_events_tables`), roster snapshot, cast counts.
- An actor container is `{_ActorTable = {} (array), _NameIndexTable = {} (name→index)}` (container_actors.lua:538). The combat object is callable: `combat(DETAILS_ATTRIBUTE_DAMAGE, "SomeName")` → actor (metamethod `__call` = `Details.call_combate`, class_combat.lua).

**Three data slots** (classes/container_segments.lua:39-48):
- `Details.tabela_vigente` — the **current** combat (segment id `0` / `DETAILS_SEGMENTID_CURRENT`).
- `Details.tabela_overall` — the **overall** combat (segment id `-1` / `DETAILS_SEGMENTID_OVERALL`).
- `Details.tabela_historico.tabelas` — the **segment history array**, newest at index 1; segment ids `1..N` index into it.

**Lifecycle**: `Details222.StartCombat()` (core/control.lua:353) creates a fresh combat object and swaps it in as current when the player enters combat (regen-disabled / ENCOUNTER_START / first group damage — control.lua drives this). `Details:SairDoCombate()` (control.lua:497) closes it and calls `Details222.Combat.AddCombat(combat)` (container_segments.lua:470) which:
1. `table.insert(segmentsTable, 1, combat)` — pushes at front.
2. Evicts the oldest beyond `Details.segments_amount` (user setting, **default 25**, functions/profiles.lua:922).
3. Optional trash policy: `Details.trash_auto_remove` deletes the previous trash segment when a new one lands (so only the latest trash survives).
4. Per-boss wipe cap: `segments_amount_boss_wipes` (default 10); when exceeded, evicts the wipe attempt with the **highest remaining boss health** (least progress) — `table.sort(allWipeSegmentsInThisBoss, ... GetBossHealth())` (container_segments.lua:617).
5. Segment **typing**: `DETAILS_SEGMENTTYPE_*` enum (class_combat.lua:16-45) — generic, dungeon trash/boss/overall, raid trash/boss, a whole mythic+ family, PvP arena/battleground, training dummy. Types drive eviction rules, overall inclusion, and menu labels.

**Overall is a fold, not a dual-write.** At segment close, `segmentClass:AddToOverallData(combatObject)` (container_segments.lua:165) does `overallCombat = overallCombat + combatObject` — `classCombat.__add` (class_combat.lua:1686) merges actor-by-actor via per-attribute merge functions (`Details.atributo_damage:AddToCombat`, `r_connect_shadow` for energy/misc), stitches combat time, and keeps a bounded `segments_added` log (last 40). Whether a segment qualifies is a separate policy: `Details:CanAddCombatToOverall` (user flags: boss-only, instance-only, minimum combat time...) plus reset options (`Details:SetOverallResetOptions`: reset on new boss / new M+ run / logoff / new PvP).

**Segment switching in the UI**: each window owns `instance.segmento` and a direct reference `instance.showing = <combat object>`. `Details:TrocaTabela(instance, segmentId, attributeId, subAttributeId)` (class_instance.lua:3346) resolves `-1` → overall, `0` → current, `n` → `segmentsTable[n]`, fires `DETAILS_INSTANCE_CHANGESEGMENT`, and — if the global option `Details.instances_segments_locked` is on — propagates the segment to all other windows. Sentinel args implement "rotate": `-2` next segment, `-3` next attribute, `-4` next sub-attribute (that's what clicking the toolbar buttons does). `auto_current` switches windows back to segment 0 on new combat (`Details:CheckSwitchToCurrent`).

**Windows never outlive data — badly.** Because instances hold direct combat references, Details needs: `Details:UpdateCombatObjectInUse` + `instance:ResetWindow()` when the shown segment is evicted (AddCombat scans all instances for removed combats, container_segments.lua:665-681); a **Freeze** state (`Details:Freeze`, class_instance.lua:3194 — window blanks and shows a disconnect icon) when a window points at nothing; and defensive `__destroyed` checks with user-facing "please report this bug on discord" messages sprinkled through `RefreshData` and `AddCombat`. This is scar tissue worth learning from (see §9).

---

## 3. Instances (windows) model — the part to emulate

An **instance** = one meter window. All live in the array `Details.tabela_instancias`; created by `Details:CreateNewInstance(instanceId)` / recycled via `Details:CreateDisabledInstance` (class_instance.lua:2370 — closed windows keep their config and are re-opened by `Details:RestauraJanela`). Default cap `instances_amount = 5` (profiles.lua:932), user-raisable via `Details:SetMaxInstancesAmount`.

**Each window independently selects, as four orthogonal knobs:**

| Knob | Field | Values |
|---|---|---|
| Segment | `instance.segmento` | -1 overall, 0 current, 1..N history |
| Attribute | `instance.atributo` | 1 damage, 2 heal, 3 energy, 4 misc, 5 custom |
| Sub-attribute | `instance.sub_atributo` | index within attribute (see §4); for attribute 5 it indexes the custom-display list |
| Mode | `instance.modo` | 1 SOLO, 2 GROUP, 3 ALL, 4 RAID (boot.lua:1315) |

Plus a memory: `sub_atributo_last = {1,1,1,1,1}` — when you switch attribute, the window restores the sub-attribute you last used there.

**Modes** are the actor filter + takeover mechanism: GROUP shows only `actor.grupo` (group members); ALL shows every actor including NPCs; RAID and SOLO hand the window body over to a registered plugin (e.g. "Raid Check") via `Details:MontaRaidOption` (class_instance.lua:3679) — the meter window doubles as a plugin host viewport.

**Refresh path per window**: mixin `RefreshData` (class_instance.lua:355) gets `instance.showing`, checks the dirty flag, then dispatches on `atributo` to `Details.atributo_damage/heal/energy/misc/custom:RefreshWindow(instance, combatObject, force)`. Note the dispatch is a hardcoded if-chain over the five classes — not a registry.

**Window grouping/linking** is physical **snapping**, not logical groups: `instance.snap = {side → otherInstanceId}`; `Details:agrupar_janelas` (class_instance.lua:2152) docks windows edge-to-edge; `Details:GetInstanceGroup()` (frames/window_main.lua:9109) discovers the chain by walking snap links; `Details:InstanceGroupCall / InstanceGroupEditSetting` (class_instance.lua:139-161) apply a method/setting across the snapped group (move together, resize together, share a skin change). Separately, `Details.instances_segments_locked` syncs segment selection across *all* windows, and `Details:CheckCoupleWindows` handles paired show/hide.

**Per-window settings** — the full blob is `Details.instance_defaults` (classes/include_instance.lua:101-482), ~150 keys. Categories: position/scale (via LibWindow), strata, click-through (rows/toolbar/window, combat-only), hide in/out of combat, `hide_on_context` rules (15 rule slots), auto-switch rules by player role (`switch_damager`, `switch_healer_in_combat`, ...), `SwitchTo/SwitchBack` saved switch tables (temporarily flip a window to another display and back after combat), total-bar, following mode (`following` — always show *your* row even when off-screen rank), bar grow/sort direction, menus/toolbar anatomy, and the entire skin surface (§7). Windows are **fat view-state objects**: everything about "a meter of X shown like Y" lives on the instance and is saved per-profile.

---

## 4. Attributes / sub-attributes system

Defined in `functions/attributes.lua` + `Definitions.lua` constants:

- **1 Damage** (9 subs): Damage Done, DPS, Damage Taken, Friendly Fire, Frags (killing blows), Enemies, Void Zones (avoidable-damage aura list), Damage Taken By Spell, Avoidable Damage Taken
- **2 Heal** (8): Healing Done, HPS, Overheal, Healing Taken, Enemy Healed, Damage Prevented (absorbs), Heal Absorbed, (potion sub as -10)
- **3 Energy/Resources** (6): mana / rage / energy / runic power / resources / alternate power
- **4 Misc/Utility** (8): CC Break, Ress, Interrupt, Dispel, Deaths, Defensive Cooldowns, Buff Uptime, Debuff Uptime
- **5 Custom** — open-ended list (§5)

`_detalhes.atributos_capture` maps every sub-attribute to which capture category feeds it ("damage"/"heal"/"energy"/"miscdata"/"aura").

**Key mechanic — sub-attribute = a key name on the actor object.** In `damageClass:RefreshWindow` (class_damage.lua:2216) the sub-attribute selects `keyName`: 1→`"total"`, 2→`"last_dps"`, 3→`"damage_taken"`, 4→`"friendlyfire_total"`, ... Then the generic machinery takes over:

```
sort actorContainer by actor[keyName]      (Details.SortKeyGroup / ContainerSort)
total  = Σ actor[keyName] over included actors
top    = actorTable[1][keyName]
for each visible rank i: actor:RefreshLine(instance, rows, rowIndex, i, total, ...)
```

`RefreshLine` (class_damage.lua) formats value + per-second + percent (percent vs `total` or vs `top`, `row_info.percent_type`), sets bar target %, class color, icon, and rank number. **The row renderer only ever sees: name, class/color, icon, value, formatted texts, percent.** That's the entire contract — the reason one rendering path serves 30+ metrics.

The actor object model per attribute (constructor `damageClass:NovaTabela`, class_damage.lua:432) is an accumulator record: `total`, `damage_taken`, `friendlyfire_total`, `targets{name→amount}`, `damage_from{}`, `pets{}`, activity-time bookkeeping (`start_time`, `on_hold`, `delay` — actors idle >N seconds stop accruing "activity time", so DPS can be computed per-actor active time via `actor:Tempo()` vs elapsed combat time, user-selectable `Details.time_type`), plus a **spell container** of per-spell stat records (`classDamageSpellTable`, class_spelldamage.lua:22: total, hit counts, min/max/avg split by normal/crit/glancing, resisted/blocked/absorbed, per-target totals) that powers the drill-down/breakdown window and tooltips.

Odd sub-attributes that don't map to a plain actor key (Frags, Damage Taken By Spell, Void Zones) get special-cased branches inside `RefreshWindow` that build a temp table of `{name, value, class}` triples and feed the *same* row loop — evidence that the {name,value,class,icon} row contract really is the universal interface.

**DPS smoothing**: `last_dps` is recomputed on refresh from totals ÷ time; the optional "real time DPS" module (`functions/currentdps.lua`) samples every actor's total every **0.10s** into a per-actor ring buffer covering a **5s sliding window** (`Details222.CurrentDPS.GetTimeSample`), giving a rolling DPS independent of the log tick.

---

## 5. Custom displays ("Details: Custom") — the extensibility pattern

`classes/class_custom.lua` + the contract doc `API Custom Displays.txt` (repo root; read it verbatim — it's short and complete).

A **custom object** is a saved table (in `Details.custom`, per-profile) with:

```lua
{ name, icon, author, desc, script_version,
  -- declarative mode:
  attribute = "damagedone", source = "[all]"|playerName, target = ..., spellid = ...,
  -- OR scripted mode (all strings of Lua):
  script = <search code>, tooltip = <code>, total_script = <code>, percent_script = <code> }
```

**Two tiers:**
1. **Declarative** (no script): pick attribute/source/target/spellid; `classCustom:BuildActorList` (class_custom.lua:317) walks the right combat container and filters — e.g. "damage done by X to Y with spell Z". Shipped examples: `classes/custom_damagedone.lua`, `custom_healingdone.lua`.
2. **Scripted**: the *search script* is compiled once with `loadstring`, wrapped in a sandbox env (`DetailsFramework:SetEnvironment`), cached in `Details.custom_function_cache[name]`, and invoked every refresh as `xpcall(func, errhandler, combatObject, instanceContainer, instanceObject)` (class_custom.lua:198). **Contract: fill the container, return `total, top, amount`.**

The **CustomContainer** is the bridge into the generic row machinery: `container:AddValue(actorRef, amount)` / `SetValue` / `GetValue` / `GetTotalAndHighestValue()` / `GetNumActors()` / `WipeCustomActorContainer()`. It accepts *any* table with `.name` or `.id` — real actors, or synthetic `{name="Jeff"}` rows — and internally clones name/class/icon info so rows render identically (`classCustom:GetActorTable`, class_custom.lua:867). One container per (instance × custom display); wiped automatically when the window changes display or combat (class_custom.lua:117-127).

**Formatting hooks**: `total_script` and `percent_script` receive `(value, top, total, combatObject, instanceObject)` and return the string/number to show — this is how a custom display can show "1m 30s" instead of "90". `tooltip` runs on row hover with `(actorRef, combatObject, instanceObject)` and paints lines into GameCooltip. Percent/total default to the standard value/total math when the hooks are absent.

**Distribution**: users create/edit these in the in-game "Custom Display Manager" (raw Lua textboxes); addons install them programmatically with `Details:InstallCustomObject(customTable)` — dedupe by name keeping the highest `script_version`. Windows select a custom display simply by setting `atributo = 5` and `sub_atributo = <index into Details.custom>` (`Details:GetCustomObject`, class_instance.lua:1178). **Custom displays are peers of built-in metrics in the window UI** — same segment selector, same rows, same skin.

---

## 6. Row/bar rendering + refresh loop

**Row pooling — retained, per-window, lazily grown.** `gump:CreateNewLine(instance, index)` (frames/window_main.lua:4200) builds one row: a clickable Button holding a StatusBar + bar texture, background texture, overlay texture, class icon (+hover highlight), border frame, up to **4 fontstrings** (`lineText1..4` — name, plus value/per-second/percent either concatenated or as separately-anchored columns, `use_multi_fontstrings`), an `extraStatusbar` (secondary value overlay — used for augmentation-evoker "damage granted by my buffs"), and optional 3D model overlays. Rows are created only up to what fits (`instance.rows_fit_in_window`, `rows_created`, hard `rows_max = 50`) and **never destroyed** — each refresh *re-binds* a row to whatever actor holds that rank (`thisLine.minha_tabela = actor; actor.minha_barra = thisLine`).

**Scrolling by rank window**: `instance.barraS[1]..barraS[2]` are the first/last *ranks* currently visible; mouse-wheel shifts them. Only those ranks are refreshed per tick; `Details:HideBarsNotInUse` (core/control.lua:1772) fades out the tail. So per-tick display cost is O(visible rows), independent of raid size (sorting is still O(n log n) over actors).

**Cadence**: one global ticker for *all* windows (`C_Timer.NewTicker(Details.update_speed, ...)`, gears.lua:214). Default `update_speed = 0.20s` (profiles.lua:970); "streamer mode faster updates" forces 0.016 (60 Hz); **performance profiles** per content type (RaidFinder/Raid30/Mythic/Arena..., gears.lua:252) can drop refresh to 1.0s, disable row animations, and turn off whole capture categories. The parser-side `need_refresh` dirty flag exists to skip untouched attributes (though in current code the early-return in `RefreshData` is commented out — they refresh every tick regardless; the flag still gates `RefreshAllMainWindows`).

**No row-position animation** — rows are fixed slots; an actor overtaking another simply gets bound to a higher row next tick. What animates:
- **Bar width**: each refresh sets `row.animacao_fim` (target percent); `Details:AnimarBarra` (core/windows.lua:195) attaches a per-row OnUpdate that lerps `statusbar.value` toward the target at `animation_speed = 33` %/sec, with an acceleration variant scaling speed by remaining distance (min 0.33×, max 3× — windows.lua:165). `Details:PerformAnimations` also clamps a bar so it never visually crosses the bar above it mid-animation (windows.lua:70).
- **Row show/hide**: pluggable "row animation" registry (`Details:InstallRowAnimation`, functions/rowanimation.lua) — default is a fade in/out.

**Number formatting**: a selectable formatter family `Details.ToKFunctions` (functions/util.lua:892 — raw, K/M abbreviations, western/east-asian groupings); scripts fetch the active one via `Details:GetCurrentToKFunction()`. Bar text is template-driven when custom text is enabled (`row_info.textL_custom_text = "{data1}. {data3}{data2}"`).

**Cheapness during heavy combat** (their actual tricks): token-table dispatch; weak GUID→actor caches so the hot path is one hash lookup; accumulate-in-place (no event queue, no per-hit allocation beyond first-seen actors/spells); `Details.cache_damage_group` — a pre-filtered array of group-member actors maintained at actor creation (container_actors.lua:993) so GROUP-mode refresh skips scanning NPCs; sort ties pre-broken by seeding every actor's `total` with a random epsilon `Details:GetOrderNumber()` = rand(1000..9000)/1e6 (util.lua:873 — hence `floor()` on every displayed value, a genuine wart); capture kill-switches; per-zone performance profiles.

---

## 7. Skinning / customization surface (inventory)

Everything below is **per-window**, lives on the instance, and is captured by skins (`Details.instance_defaults`, include_instance.lua:101):

- **Rows** (`row_info`): height; texture (foreground / background / overlay / mouse-over highlight, each from a shared-media registry or custom file); bar color by class vs fixed color (fg and bg independently); row alpha; per-row backdrop/border (size/color/class-color); spacing (left/right/between) and per-side row offsets; icon (class/spec icon file, size offset, grayscale, mask, or none); rank number on/off; left/right text: font face+size, class-colored or fixed, shadow/outline, custom templates with `{data1..3}` placeholders, which of value/per-second/percent to show, bracket/separator characters, percent-vs-total or percent-vs-top; name auto-sizing; 3D model overlays (pure WoW flourish); "fast ps updates" (re-render per-second text at high rate).
- **Window**: background color+alpha+texture; wallpaper (any texture, anchor, texcoord crop, alpha, overlay tint); title bar (shown/height/texture/color); full-window border and row-area border (color/size); rounded corners; scale; strata; grow direction (bars top-down or bottom-up) and sort direction; inverted (right-to-left) bars.
- **Chrome**: toolbar side, which menu icons show (mode/segment/attribute/report/reset/close), icon size/alpha/color/desaturation, auto-hide menus on mouse-leave, attribute-name text (own font/template/timer), statusbar (footer hosting micro-display plugins), scrollbar.
- **Behavior-ish appearance**: total bar (synthetic row 1 showing raid total); following bar (pin your own row); hide in/out of combat; context rules; click-through; row show/hide animation choice.
- **Skins** = named presets bundling all of the above + a statusbar art file: `Details:InstallSkin(name, skinTable)` (functions/skins.lua:8); shipped skins are just calls to it (`"Minimalistic"`, `"WoW Interface"`, ...); users export/import per-window setups (`Details:ExportSkin` serializes the instance blob); third-party skins get **cached into saved variables** so they survive the source addon being disabled (skins.lua:24-33 — nice resilience touch).

---

## 8. Plugin API (brief)

- **`Details:InstallPlugin(pluginType, localizedName, icon, pluginObject, absoluteName, minVersion, author, version, defaultSavedTable)`** (core/plugins.lua:153). Types: `"RAID"` (takes over a window's body when that window is switched to raid mode — Death Log, Raid Check), `"SOLO"` (same for solo mode), `"TOOLBAR"` (icon+menu on the window toolbar — Encounter Details, Time Attack), `"STATUSBAR"` (micro-widgets in the window footer: clock/durability/threat, core/plugins_statusbar.lua). Details manages the plugin's saved table, enable state, and version gating; plugin frames get a `RegisterEvent` shim.
- **Event bus** for consumers: `local listener = Details:CreateEventListener(); listener:RegisterEvent("COMBAT_PLAYER_ENTER", fn)` (functions/events.lua). Full event list at events.lua:25-75: combat lifecycle (`COMBAT_PLAYER_ENTER/LEAVE`, `COMBAT_ENCOUNTER_START/END`, `COMBAT_BOSS_FOUND/WIPE/DEFEATED`), data (`DETAILS_DATA_RESET`, `DETAILS_DATA_SEGMENTREMOVED`), and per-window UI events (`DETAILS_INSTANCE_CHANGESEGMENT/CHANGEATTRIBUTE/CHANGEMODE/SIZECHANGED/OPEN/CLOSE`). Internally everything interesting is announced via `Details:SendEvent`.
- **Data API**: raw object access (combat/actor methods, `combat:GetActorList(attr)`, callable combat) documented in `API General.txt`; a formal typed wrapper "API 2" in `functions/api2.lua` (`SegmentInfo`, `SegmentTotalDamage`, `UnitDamage`, `UnitDamageBySpell`, ...); plus `Details:InstallCustomObject` (§5) so plugins ship new displays.

---

## 9. Design lessons / portable patterns for an eq2auras "meter windows" module

### Port these (the load-bearing ideas)

1. **The universal row contract.** Every metric — built-in or user-scripted — reduces to: *an ordered list of `{key, label, classColor, icon, value}` + `total` + `top` + optional format/tooltip hooks*. One rendering pipeline, N data providers. This is the single most important pattern: in WPF terms, one `MeterRowViewModel` + one retained row-control pool per window, fed by pluggable `IMeterSource.Refresh(combat, window) → (rows, total, top)`.

2. **Windows as fat, dumb viewports.** A window = `{segment selector, source selector (attribute/sub), filter/mode, appearance blob}`; all data lives in combat objects that don't know windows exist. N windows over the same data are nearly free. Persist the entire window blob; "skins" are just named copies of it. Maps directly onto eq2auras' existing per-window knob model.

3. **Pull-based rendering on one global clock + dirty flags from the write path.** Capture accumulates in place at event rate; display samples at 3–5 Hz (user-tunable, with a "performance profile" concept). No per-hit UI work, ever. eq2auras already lives by "the wall clock owns the visuals" — same philosophy. ACT plays the role of the parser: subscribe to ACT's `OnCombatAction`/log events *only* to mark dirty or maintain incremental caches, and read `ActGlobals.oFormActMain.ZoneList` / `EncounterData` / `CombatantData` on the render tick.

4. **Segment = combat object; history is a ring buffer with policies.** Current(0) / Overall(−1) / History(1..N ≈ 25), typed segments (boss/trash), eviction policies (cap, trash-auto-remove, worst-wipe-eviction) kept *outside* the data objects. Overall as an explicit **merge fold** at segment close (`__add` merging actor-by-actor), with a separate "qualifies for overall" policy + reset policy. Note for eq2auras: ACT already keeps encounter history (`ZoneData`/`EncounterData`) — decide early whether segments are *ACT's encounters referenced by handle* or *our own fold structures*; Details' pain says don't do both halfway.

5. **Reference data by handle, not pointer.** Details' windows hold direct combat references and paid for it with the Freeze mechanic, `__destroyed` flags, and "report on discord" checks. Resolve segment-id → data at render time; if the segment is gone, degrade explicitly (empty state / auto-fall-back-to-current). This is literally eq2auras' "never outlive the data" rule — the meter module must obey it too.

6. **Sub-metric = key selection over one accumulator record.** One actor record with many counters (`total`, `taken`, `healed`, `deaths`, ...); a display picks a key and sorts. Adding "damage taken" is a sort key, not a new pipeline. With ACT, `CombatantData` items already carry Damage/Healed/DamageTaken/Deaths etc. — an adapter enum `MetricKey → Func<CombatantData, double>` reproduces this in a dictionary registry (improve on Details' if-chains).

7. **Custom displays as first-class peers.** The two-tier design (declarative filter first, script escape hatch second) is right. For C#: start with the declarative tier (source/target/ability filters over ACT data — covers most of Details' shipped customs) and defer arbitrary scripting; if scripting ever comes, keep Details' exact contract shape: `Search(combat, container, window) → (total, top, count)` + optional `FormatValue`/`FormatPercent`/`Tooltip`. The **CustomContainer** ("anything with a name and a value") is the piece that lets synthetic rows reuse the whole window machinery.

8. **Rendering economics**: retained row pool sized to window height, re-*bind* rows to ranks each tick (never rebuild — matches eq2auras' "retain elements, animate properties"); refresh only the visible rank window; animate bar *width* toward a target (Details: ~33%/sec with distance-based acceleration, re-target on each tick) and *fade* rows in/out — do **not** animate row reordering (Details never did, and nobody misses it); percent modes (vs total / vs top); pluggable value formatter (K/M abbreviation).

9. **Window linking**: two independent, both cheap and loved — (a) edge **snapping** so windows move/resize as a group (WPF: shared canvas coords make this easy), (b) a "segments locked" toggle syncing segment selection across windows. Details has no deeper "linking" than that.

10. **Capture kill-switches per category** (Details can turn off healing capture entirely in raid-finder). ACT parses regardless, so eq2auras' analog is cheaper: per-category *aggregation* toggles and update-rate profiles.

### WoW-specific accidents (don't port)
Pet-ownership resolution via tooltip scanning and GUID heuristics (ACT resolves pets); spell-reflect/override spellId shenanigans; CLEU token zoo and flag bitfields; ENCOUNTER_START boss detection and mythic+ segment taxonomy (ACT's zone/encounter model replaces it — though a light "boss vs trash" classification from EQ2 mob names is worth keeping as a segment *type* tag); class colors/spec icons (→ EQ2 class/archetype colors — and eq2auras already keys color identity by normalized name); 3D model bar overlays; the entire Blizzard-API "Apocalypse/Midnight" dual-source layer (that's Details adapting to Blizzard shipping a built-in damage meter API — irrelevant, though its existence validates the "swap the capture layer, keep the display machinery" separation we're doing with ACT).

### Known pain points observed in the source (avoid)
- **Monoliths**: parser.lua ~7.9k lines, class_damage.lua ~8.2k, class_instance.lua ~4.9k; attribute dispatch is copy-pasted if-chains in 3+ places (RefreshData, RefreshAllMainWindows, export paths). Use a display registry.
- **Custom displays bolted on as attribute 5**: placeholder container `combatObject[5]`, special-case branches everywhere (`instance.atributo == 5`). If customs are designed in from day one as just-another-source in the registry, this disappears.
- **Storage format = live object format**: combat tables are saved verbatim to SavedVariables, so any refactor is a migration, and destroyed/live object confusion leaks into saved data. Separate persistence DTOs.
- **Epsilon-seeded totals** (`GetOrderNumber`) to stabilize sorts → `floor()` everywhere and never-exactly-zero totals. Use a proper tie-break comparator instead.
- **Mixed PT/EN naming** (`TrocaTabela`, `barraS`, `minha_tabela`) — pure legacy friction, but a reminder that this codebase is 12+ years of accretion and the *architecture* (pipeline, row contract, window model) is what survived, not the code.

### Suggested ACT mapping (for the brainstorm)
`Details combat object ≈ ACT EncounterData` · `actor ≈ CombatantData` · `spell table ≈ DamageTypeData/AttackType breakdown` · `segments 1..N ≈ ZoneData.Items history` · `current(0) ≈ ActiveEncounter/last encounter while InCombat` · `overall(−1) ≈ ACT's "All" encounter or our own fold with eq2auras-controlled inclusion policy` · `parser dirty flags ≈ OnCombatAction handler setting a dirty bit` · `update_speed ticker ≈ existing WPF render clock at a lower divider`. The genuinely new things to build are: the window/source/segment knob model (§3), the row contract + row pool (§6), and the source registry with a declarative custom tier (§4/§5).
