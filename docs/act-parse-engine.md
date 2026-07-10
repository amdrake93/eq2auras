# ACT Parse Engine & Mini Parse — decompiled reference

The complete combat-data pipeline of ACT: the data model the parser builds, the export/format
system the mini parse renders from, and the hook points a meter plugin taps. Read from the
shipped binary. Ground truth for the exact `Advanced Combat Tracker.exe` vendored in
`ThirdParty/` — re-derive after any ACT upgrade. Sibling to `act-timer-engine.md` (the spell
timer side).

**Sources:** everything below is `[decompiled]` — read from method bodies, not API docs. No
field validation yet; mark findings `[field]` as raid captures confirm them. The full research
report with complete file:line citations is archived at
`docs/research/2026-07-09-act-parse-decompile-report.md`.

## Re-derivation

```sh
dotnet tool install -g ilspycmd   # once
# whole-assembly decompile (per-type files, greppable) beats per-type for this subsystem:
~/.dotnet/tools/ilspycmd -o /tmp/act-decompiled -p "ThirdParty/Advanced Combat Tracker.exe"
```

Key files: `FormActMain.cs` (the engine: log reader, `AddCombatAction`, `GetTextExport`,
`SetupEQ2EnglishEnvironment`, `UpdateMiniEnc`), `EncounterData.cs`, `CombatantData.cs`,
`DamageTypeData.cs`, `AttackType.cs`, `MasterSwing.cs`, `ZoneData.cs`, `FormMiniParse.cs`.

## Data model

Five levels of nesting; everything above `MasterSwing` is an aggregation view over swing lists.
Every level carries a `public Dictionary<string, object> Tags` for arbitrary plugin data.

```
FormActMain.ZoneList : List<ZoneData>
└─ ZoneData
   ├─ .Items : List<EncounterData>          (Items[0] = live-fed zone "All" when PopulateAll)
   └─ .ActiveEncounter : EncounterData
      └─ .Items : SortedList<string, CombatantData>       key = name.ToUpper()
         └─ .Items : Dictionary<string, DamageTypeData>   key = category ("Outgoing Damage", …)
            └─ .Items : SortedList<string, AttackType>    key = ability name (+ "All")
               └─ .Items : List<MasterSwing>              the raw swings — the only real data
```

**`MasterSwing`** — the atom. `Time`, `TimeSorter` (monotonic int, total order within
1s-resolution log timestamps), `SwingType` (plain int; EQ2 uses `SwingTypeEnum`: Melee=1,
NonMelee=2, Healing=3, PowerDrain=10, PowerHealing=13, Threat=16, CureDispel=20), `Damage`
(`Dnum`), `Attacker`, `Victim`, `AttackType` (ability name), `DamageType` (e.g. "heat"),
`Critical`, `Special`, `ParentEncounter` (stamped on insert), `Tags`.

**`Dnum`** — damage wrapper with implicit `long` conversions and sentinels: `NoDamage`=0,
`Miss`=−1, Resist=−2, Parry=−3, Riposte=−4, Block=−5 (−6..−8 unmapped), `Unknown`=−9,
`Death`=−10, `ThreatPosition`=−11. Implicit `long→Dnum` clamps anything < −10 to Unknown —
**including the `ThreatPosition` static itself**: its getter (`=> -11L`) compiles through the
implicit operator, so it actually returns `Dnum(−9)`; only `new Dnum(-11)` (the constructor
doesn't clamp) yields a real −11. ACT is internally split by this — `CombatantData` compares
swings against the clamped static (= −9) while `FormActMain` switches on raw `-11L` — so
never key threat detection on `Dnum.ThreatPosition` or it silently matches Unknown.
`operator +` ignores negative values; zero participates.

**`AttackType`** — where the numbers come from. Every metric is computed **lazily with
incremental caching keyed on `Items.Count`**: each cached metric remembers the index it has
consumed and only folds in new swings on the next read. Metrics: `Damage` (positive Dnums
only), `Hits`, `CritHits`, `CritPerc`, `Swings`, `Misses`, `Blocked`, `Median` (non-incremental
full sort), `MinHit`/`MaxHit`, `StartTime`/`EndTime`, `Duration`, `DPS` (own duration),
`CharDPS` (combatant duration), `EncDPS` (encounter duration), `AverageDelay`, `Resist`.
**The model assumes swings are append-only** — mutating a swing after insert silently corrupts
cached sums unless `InvalidateCachedValues(true)` is called up the tree.

**`DamageTypeData`** — one per category per combatant; all metrics delegate to its `"All"`
AttackType. (Latent bug: `CritHits` hardcodes English `"All"` where everything else uses
`ActGlobals.Trans["attackTypeTerm-all"]`.)

**`CombatantData`** — one combatant in one encounter. Constructor builds one `DamageTypeData`
per entry in the **public static** routing tables `OutgoingDamageTypeDataObjects` /
`IncomingDamageTypeDataObjects`; `SwingTypeToDamageTypeDataLinksOutgoing/Incoming` route each
swing type into categories. EQ2 routing (from `SetupEQ2EnglishEnvironment`): out 1→
{"Auto-Attack (Out)","Outgoing Damage"}, 2→{"Skill/Ability (Out)","Outgoing Damage"},
3→"Healed (Out)", 10/13/16/20 → power drain/replenish/threat/cure buckets; in 1,2→"Incoming
Damage", etc. Every swing is inserted into multiple buckets (category "All" + category
per-ability + the `AllOut`/`AllInc` reference buckets, mirrored onto the victim's incoming side
via `AddReverseCombatAction`). **Never hardcode bucket names** — use the alias statics
(`CombatantData.DamageTypeDataOutgoingDamage`, `…OutgoingHealing`, `…IncomingDamage`, …) and
`ActGlobals.Trans`; the keys are localization-dependent.

Combatant metrics (on-demand): `Damage`, `DamageTaken`, `Healed`, `HealsTaken`, `Swings`,
`Hits`, `Misses`, `Blocked`, `CritHits`, `Heals`, `CritHeals`, `CureDispels`, `ToHit`,
`DPS` (personal duration), `EncDPS`/`ExtDPS` (Damage / *encounter* Duration — the cheap one),
`EncHPS`/`ExtHPS`, `DamagePercent`/`HealedPercent` (share of ally total), `Deaths`, `Kills`,
`StartTime`/`EndTime`, `ShortEndTime` (last outgoing *damage* — heals don't extend it),
`Duration`, `GetMaxHit`/`GetMaxHeal` (linear scans), `GetCombatantType()` (0 enemy / 1 tank-ish
/ 2 healer / 3 auto-attack / 4 dps heuristic), threat helpers, `Allies` (weight graph feeding
`EncounterData.GetAllies()`).

**`EncounterData`** — `Items` (combatants, UPPERCASE keys), `Active`, `Title` (set at combat
end = `GetStrongestEnemy()`), `StartTimes`/`EndTimes` (lists — silence cutting creates
segments), `Tags`. `EndTime` with default options = max **ally** `ShortEndTime`, so trailing
heals don't stretch ENCDPS. Aggregates (`Damage`, `Healed`, `AlliedDeaths`, `DPS`,
`GetEncounterSuccessLevel()` 1 win/2 partial/3 wipe, `GetStrongestEnemy()`) loop over
`GetAllies()` per call, uncached. `GetAllies()` = breadth-first walk of the ally weight graph
seeded from `CharName` (EQ2 logs are you-relative; `ActGlobals.charName` defaults `"YOU"`);
result cached until the graph changes; `GetAllies(allowLimited: true)` reuses a ≤1s-stale
result. `SetAllies(list)` lets a plugin pin the list. **Never read `EncId`/`GetHashCode()`/
`Equals` on a live encounter** — the hash sums every swing's string hash, O(all swings).

**`ZoneData`** — when `PopulateAll` (option "Zone All listing"), `Items[0]` is a merged "All"
encounter fed **live in parallel** with the current encounter. There is no merge step at combat
end.

## ExportVariables & ColumnDefs — the customization system

Two parallel `public static` mutable systems at each level. **Plugins can add, replace, or
clear entries.**

**`ColumnDefs`** (table cells + SQL/HTML export): `EncounterData.ColumnDefs`,
`CombatantData.ColumnDefs` (+ `SortComparer`, default sort `"EncDPS"` via
`ActGlobals.eDSort`), `DamageTypeData.ColumnDefs`, `AttackType.ColumnDefs`,
`MasterSwing.ColumnDefs`. A `ColumnDef` = label, visibility, SQL type/name, cell callback, SQL
callback (+ fore/back color callbacks). After registering, call
`ActGlobals.oFormActMain.ValidateTableSetup()` to sync the options UI.

**`ExportVariables`** — the `{var}` format strings; **this is what the mini parse renders**.
- `CombatantData.ExportVariables : Dictionary<string, TextExportFormatter>` — callback
  `string (CombatantData Data, string ExtraFormat)`.
- `EncounterData.ExportVariables` — callback `string (EncounterData Data,
  List<CombatantData> SelectiveAllies, string ExtraFormat)` (encounter values are sums over
  the filtered ally list).

**Resolution** (`FormActMain.GetTextExport`): a `TextExportFormatOptions(PlayerFormat, Sorting,
ShowOnlyAllies, ShowAlliedInfo, AlliesFormat)` preset is applied by regex
`{(?<formatter>[^}:]+)(?::(?<extra>[^}]+))?}|(?<text>[^{]+)` (the `text` branch passes
literal text through) — `{var:extra}` passes `extra` to the formatter (e.g. `{NAME:8}`
truncates). Combatant list is snapshot-copied (bare try/catch returning `""`
on concurrent mutation — ACT's entire thread safety for exports), sorted descending by the
preset's Sorting via `CombatantData.DualComparison`, header line = AlliesFormat against
encounter variables, then one PlayerFormat line per combatant. Unknown tokens re-emit
literally. Optional column alignment measures strings with GDI and pads. Output truncated to
`MaxLines`.

Both dictionaries are populated by `FormActMain.SetupEQ2EnglishEnvironment()` (which `Clear()`s
them) during `InitACT` — **before plugins load**, so plugin registrations survive; a *parsing*
plugin re-running an environment setup would wipe them.

Built-in variable names (both levels unless noted): `name`/`NAME`/`NAME3`–`NAME15`
(combatant), `n`, `t`, `title` (encounter), `duration`/`DURATION`, `damage` (+`-m`, `-b`,
`-*` scaled variants; uppercase = k/m/b-scaled), `dps`, `encdps`, `ENCDPS`, `hits`,
`crithits`, `crithit%`, `misses`, `hitfailed`, `swings`, `tohit`, `maxhit`/`MAXHIT`,
`healed`, `healed%` (combatant), `enchps`/`ENCHPS`, `heals`, `critheals`, `critheal%`,
`cures`, `maxheal`/`maxhealward`, `damage%` (combatant), `damagetaken`, `healstaken`,
`powerdrain`, `powerheal`, `kills`, `deaths`, `threatstr`/`threatdelta` (combatant). All
funnel to two switches: `FormActMain.EncounterFormatSwitch` / `CombatantFormatSwitch`.

**Wall-clock nuance:** encounter `{duration}`/`{DURATION}` (only these) use
`ActGlobals.wallClockDuration` (default true): while active, duration =
`LastEstimatedTime − StartTime`, where `LastEstimatedTime = LastKnownTime +
estimatedTimer.Elapsed` (a Stopwatch restarted on every parsed log timestamp). **Every DPS/HPS
variable divides by log-time `Duration`** (last ally damage swing − first swing) — ACT's
ENCDPS freezes during log silence. Same lurch class as the timer clock; the overlay owns its
own wall clock.

Registration recipe (all types public):

```csharp
CombatantData.ExportVariables.Add("mymetric",
    new CombatantData.TextExportFormatter("mymetric", "My Metric", "desc",
        (CombatantData d, string extra) => d.Damage.ToString()));
ActGlobals.oFormActMain.ValidateTableSetup();   // if ColumnDefs were touched
```

Custom mini-parse presets: `FormActMain.AddTextFormat(...)` / `RemoveTextFormat(int)` append to
the shared `TextExportFormats` list and the mini/clipboard dropdowns.

## The mini parse window

`FormMiniParse` (singleton `ActGlobals.oFormMiniParse`) is a TopMost, magenta-transparency-key
WinForms form with **no data logic**: a `RichTextBox` (text mode) and a `PictureBox` (graph
mode). Click-through = `WS_EX_TRANSPARENT`. All content is pushed by
`FormActMain.UpdateMiniEnc()`: text mode = `GetTextExport(ActiveZone.ActiveEncounter,
<selected preset>)` set wholesale into the RichTextBox (only if the string changed); graph mode
= `GenEncounterGraph(...)` bitmap. Default preset: player line `{n}{NAME5} | {encdps-*}`
sorted by EncDPS, header `({duration}) {title}: {encdps-*} {maxhit-*}`.

**Cadence:** driven by `tmrTick`, a 1-second WinForms UI-thread timer. `UpdateMiniEnc()` runs
only while `InCombat`, every `nudMiniUpdateInterval` ticks (min 1, max 60, **default 5**), plus
one final call at combat end. Suppressed during imports.

**Limitations (why ours will be better):** 1s minimum cadence quantized to a shared tick
counter; whole-string RichTextBox replacement (no retained elements, flicker, no animation);
reads the live encounter **without the data lock** (survives via try/catch — a racy tick
renders empty); log-time DPS freezes during silence; per-tick cost includes a full combatant
sort + ally graph walk.

## Real-time hook points

Plugin model: `IActPluginV1.InitPlugin` runs on the UI thread; subscribe there. All events live
on `ActGlobals.oFormActMain`. Handlers are individually try/caught — a throwing handler is
never fatal, but gets logged per-assembly.

### The pipeline, end to end (live parse)

```
"Log Reader" thread — ReadLog(): poll FileStream every 10ms → per line:
  LastKnownTime = GetDateTimeFromLog(line); GlobalTimeSorter++
  ParseRawLogLine(...)  [lock(rawLogLineLock)]
    ├─ CheckIdleEndCombat()                → may EndCombat(true) on this thread
    ├─ BeforeLogLineRead   ← the EQ2 *parsing* plugin lives here; it calls, synchronously:
    │     SetEncounter(time, attacker, victim)   → fires OnCombatStart on !InCombat
    │     AddCombatAction(MasterSwing)           → fires BeforeCombatAction, then enqueues
    ├─ (record log lines, queue custom triggers)
    └─ OnLogLineRead       ← observers; line possibly rewritten by handlers above

"AfterActionQueueThread" — drains afterActionsQueue:
  lock (AfterCombatActionDataLock)
    ActiveZone.AddCombatAction(swing)      ← the ONLY mutation of the live model
    AfterCombatAction(isImport, args)      ← fires per swing, model already updated

UI thread — tmrTick (1s): idle EndCombat fallback, UpdateMiniEnc, tree refresh
```

This ACT binary contains **no built-in combat-line parser** — `SetEncounter` has zero internal
callers; the installed EQ2 parsing plugin drives everything through `BeforeLogLineRead`.
`SetupEQ2EnglishEnvironment` only installs the data environment and the EQ2 timestamp parser.
The plugin itself is vendored and documented below (§The EQ2 parsing plugin).

### Event catalog

| Event | Signature | Fires | Thread |
|---|---|---|---|
| `BeforeLogLineRead` | `(bool isImport, LogLineEventArgs)` | per raw line, first | Log Reader (import thread when importing) — serialized by `rawLogLineLock` |
| `OnLogLineRead` | same | per line, after parse/triggers | same |
| `BeforeCombatAction` | `(bool isImport, CombatActionEventArgs)` | in `AddCombatAction`, before queueing; **mutable + cancelable** (`cancelAction = true` drops the swing) | caller of `AddCombatAction` (= reader thread live) |
| `AfterCombatAction` | same | per swing, **after** the model applied it, **while holding `AfterCombatActionDataLock`** | AfterActionQueueThread |
| `OnCombatStart` | `(bool isImport, CombatToggleEventArgs)` | in `SetEncounter` on the !InCombat transition; `.encounter` is the fresh (still empty) EncounterData | caller of `SetEncounter` |
| `OnCombatEnd` | same | in `EndCombat` after finalize; **queue is drain-waited first, so totals are complete and final** | varies: UI tick, reader thread, import thread |
| also | `LogFileChanged`, `LogFileRenamed`, `ActLifecycleChanged`, `XmlSnippetAdded` … | | |

There is no `OnCombatToggle` — the pair is `OnCombatStart`/`OnCombatEnd`.

### Reading the current encounter mid-combat

`ActGlobals.oFormActMain.ActiveZone.ActiveEncounter` — **null until the session's first
`SetEncounter`, and stale (not null) after combat**: gate on `InCombat` / `encounter.Active`.
Companion state: `ZoneList`, `InCombat`, `LastKnownTime`/`LastEstimatedTime`,
`GlobalTimeSorter`. Zone "All" = `ActiveZone.Items[0]` when `PopulateAll`.

### Thread safety

- All model mutation happens on the after-action thread inside
  `lock (ActGlobals.oFormActMain.AfterCombatActionDataLock)` (public property).
- **Correct read pattern from our own thread: take that lock, read briefly, release.** Holding
  it stalls stat application (parsing continues; swings queue up).
- Reading inside an `AfterCombatAction` handler is already under the lock.
- Metric getters **mutate cache fields** — even "read-only" property access races against
  other readers; serialize all reads through the same lock.
- ACT's own UI readers don't lock (they copy + try/catch and tolerate torn reads). Don't copy
  that pattern.

## Encounter lifecycle

1. **Start** — parsing plugin calls `SetEncounter(Time, Attacker, Victim)` on a hostile line.
   On `!InCombat`: fresh `EncounterData` becomes `ActiveZone.ActiveEncounter`, appended to
   `ActiveZone.Items`, `OnCombatStart` fires. Always: `LastHostileTime` updated,
   `InCombat = true`.
2. **Silence cutting** — a hostile gap exceeding the options silence limit closes an
   EndTimes/StartTimes segment pair *without* ending combat. This is why the time fields are
   lists and per-combatant `Duration` has a heavy multi-segment walk (`EncDPS` avoids it by
   dividing by encounter duration — the cheap metric, and ACT's default sort for a reason).
3. **Accumulation** — `AddCombatAction` (**throws `InvalidOperationException` if
   `!InCombat`**) → rename/redirect corrections → `BeforeCombatAction` → spell-timer
   `NotifySpell` → queue → after-action thread stamps it into both the current and zone-"All"
   encounters and fires `AfterCombatAction`. Near-real-time: sub-millisecond queue latency
   under normal load.
4. **End** — `EndCombat(bool export)`, triggered by log-time idle (`CheckIdleEndCombat` on the
   reader thread), wall-clock idle fallback (UI tick), user/game command, zone change, or
   import boundary. Sequence: `InCombat = false` → **spin-wait until the after-action queue is
   fully drained** → finalize (Trim, `Title = GetStrongestEnemy()`) → `OnCombatEnd` → history
   DB → `CullEncounters()` → final `UpdateMiniEnc()` → optional exports.
5. **Culling** — `CullEncounters` runs at every combat end per the encounter-culling options
   (drop titleless encounters, age/count limits). **Never hold `EncounterData` references
   across fights and assume ACT still lists them** — the object survives but may be orphaned
   from `ZoneList`.

## The EQ2 parsing plugin — the swing stream's author

EQAditu's **"English Parsing Engine" v1.4.2.30** (ACT plugin id 46), distributed as C#
*source* that ACT compiles at load — vendored verbatim at `ThirdParty/ACT_English_Parser.cs`
(all line refs below are into that file). Self-updates via ACT's plugin-update mechanism, so
re-verify after it updates on the Windows box.

### Wiring (`InitPlugin`, line 260)

Subscribes `BeforeLogLineRead` (the parse driver), `BeforeCombatAction` (data corrections),
`OnLogLineRead` (selective-parsing capture of `/who`/`/consider` output), `UpdateCheckClicked`.
**It also runs its own `SetupEQ2EnglishEnvironment()` (line 1904), which `Clear()`s and
repopulates all five `ColumnDefs` dictionaries and both `ExportVariables` dictionaries at
plugin-load time** — a superset of ACT's internal copy (adds `CritTypes` columns and a
`MasterSwing` `CriticalStr` column reading `Tags["CriticalStr"]`, per-swing-type row colors).
Consequence for us: **any ExportVariables/ColumnDefs we register are wiped if this plugin
inits after ours** (plugin load order is ACT's plugin-list order), and again if the user
toggles the parser plugin off/on. Register idempotently and re-register on
`ActLifecycleChanged`/plugin-init events, or don't depend on registration surviving.

### Line recognition (lines 330–390)

14 compiled regexes tried **in order, first match wins** (`detectedType = i+1`), all prefixed
`\(\d{10}\)\[.{24}\] `, gated by a keyword quick-fail: the line must contain one of
`damage, point, ", but", killed, command, entered, hate, dispel, relieve, reduces`.
Damage amounts arrive game-shortened (`1.23M`); `ExpandDamageAmount` (1062) expands
commas and K/M/B/T/Q suffixes. Possessive splitting supports `’ ' 의 の` (EN + KR/JP glyphs).

### The 14 cases → what swings actually exist

| # | Log shape | Emission |
|---|---|---|
| 1 | "X is hit by Y for N damage" (unsourced) | NonMelee, attacker **"Unknown"**, only if `InCombat` |
| 2 | "A hits/flurries/multi attacks/… V for N damage" | **`SetEncounter`** (starts combat) → Melee (no skill) / NonMelee (skill); attacker⁄skill split by apostrophe grammar (`SplitAttackerSkill`, 1115) |
| 3 | "H heals V for N hit points" | Healing(3), DamageType `"Hitpoints"`, **requires `InCombat` — heals never start combat**; unsourced heals dropped |
| 4 | "A tries to X V, but …" | Melee/NonMelee with fail Dnum (see below) via `SetEncounter` |
| 5 | "A hits V but fails to inflict any damage" | 0-damage swing — or the **reconstructed ward/intercept value** (below) |
| 6 | "A has killed V" | NonMelee Death swing (`"Killing"`/`"Death"`, `Dnum.Death`), if `InCombat`; also `RemoveTimerMods`+`DispellTimerMods(victim)` |
| 7 | "Unknown command: 'act …'" | `ActCommands(...)` — the in-game `/act end` path |
| 8 | "A's X slashes/burns/… V draining N power" | PowerDrain(10), `SetEncounter` |
| 9 | "H absorbs N points of damage from being done to V …" | **Healing(3) with DamageType `"Absorption"`** — wards count into Healed/EncHPS; bleed-through & remaining in `Special` `"(N BT) [M left]"`; requires `InCombat` |
| 10 | "You have entered Z" | `ChangeZone` |
| 11 | "H refreshes V for N mana" | PowerHealing(13), DamageType `"Power"`, requires `InCombat` |
| 12 | "O's X increases/reduces A hate with V by N threat/positions" | Threat(16), DamageType `"Increase"`/`"Decrease"`; **can start combat** (`SetEncounter(attacker,victim)` or `(owner,victim)`) |
| 13 | "A's X dispels/relieves Y from V" | CureDispel(20), damage=1 (a count), `Special`=affliction; *dispels* can start combat, *relieves* (cures) require `InCombat` |
| 14 | "H reduces the damage from A to V by N" | **Healing(3), AttackType `"Channeler Pet"`, DamageType `"Interception"`**, requires `InCombat` |

**Emitted SwingTypes: 1, 2, 3, 10, 13, 16, 20 only** — matching ACT's EQ2 routing tables.

**Fail types (case 4, `GetFailTypeEnglish`, 1274):** only a true miss gets `Dnum.Miss` (−1).
**Everything else — parry, riposte, block, dodge, resist, reflect — arrives as
`Dnum(−9, "<why text>")`**: ACT's per-type sentinels −2..−5 are *never emitted* by this
parser. `CombatantData.Blocked` still counts them (its test is `< −1 && != Death`), but
per-type avoidance breakdown exists only in the `DamageString` text.

**Threat positions (case 12):** emitted as `new Dnum(Dnum.ThreatPosition, "N Positions")` —
which, because the static self-clamps (§Data model), constructs **−9/Unknown**. Live proof of
the Dnum trap: position-change swings are distinguishable from other Unknowns only by
DamageString/DamageType.

**Crits:** the `Critical` flag plus **`Tags["CriticalStr"]`** carrying the crit-tier text
(Legendary/Fabled/Mythical) on damage, heal, power-heal, and threat swings — the plugin's own
`CritTypes` columns aggregate it.

### Wards & intercepts — the recalc trick (`cbRecalcWardedHits`, default ON)

The game logs the absorb line *before* the hit line at the same 1s-resolution timestamp. The
plugin records the absorb (case 9/14) into `lastWard*`/`lastIntercept*` state, then when the
victim's hit arrives **in the same second** (`CheckWardedHit`, 1170), it adds the absorbed
amount back: a fully-warded "no damage" hit becomes a swing with the true value, DamageType
gaining a `warded/`/`intercepted/` prefix and DamageString like `"1,234/300/5"` (absorbed +
per-type parts). Stoneskin no-damage hits can't be recalculated (help text, 1817). Channeler
pet "focus" damage is normally suppressed in favor of the intercept-heal
(`cbIncludeInterceptFocus`, default off).

### Multi-type damage (`cbMultiDamageIsOne`, default ON)

"300 crushing and 5 poison damage" → **one** swing, `Dnum(305, "300/5")`, DamageType
`"crushing/poison"` (per-ability AttackType named by the combined verb form). Unchecked →
one swing *per* damage type (inflates swing counts / ToHit%).

### Pets, names, corrections

- Pet combatants keep full names — `petSplit` (line 328): `Fluffy <Alex's warlock>` →
  groups `petName`/`attacker`/`petClass`. The parser uses it only to drop self/own-pet hits
  ("you don't get credit for attacking yourself or your own pet"); **no reattribution to
  owner** — a meter wanting owner-merged pets applies this regex itself.
- `EnglishPersonaReplace`: YOU/YOUR/YOURSELF → `ActGlobals.charName` (the you-relative log).
- `BeforeCombatAction` corrections (1411): Ancestral Sentry intercedes synthesize a Healing
  swing (DamageType `"Intercede"`); riposte/reflect return-damage pairs get annotated via
  `DamageString2`; user-configured apostrophe-name fixes un-split mob names the grammar
  splitter breaks.

### Timer-engine tie-ins (cross-ref `act-timer-engine.md`)

The parser never calls `NotifySpell` (a call sits commented out at line 513) — spell timers
fire inside ACT's `AddCombatAction`. But it **is** the timer-mod source: a Traumatic Swipe hit
→ `ApplyTimerMod(attacker, victim, skill, 0.5F, 30)`; its dispel → `DispellTimerMods`; any
death → `RemoveTimerMods` + `DispellTimerMods`. This is the concrete origin of the "mods are
the time travel" behavior in the timer doc.

### Combat-start blind spot (meter-relevant)

Only damage-shaped lines start encounters (cases 2, 4, 8, 12, 13-dispels). **Heals, wards,
power heals, intercepts, and unsourced hits that precede the first hostile line are dropped
entirely** — a healer pre-warding before a pull loses those wards from the parse. ACT-inherited;
our meter sees exactly what ACT sees.

## Performance & correctness pitfalls

- `isImport == true` in every handler during imports — early-out; the same pipeline replays.
- Encounter-level aggregates (`Damage`, `DPS`, `AlliedDeaths`) loop allies per call. Fine at
  1–10 Hz; measurable at 60 Hz × 24-person raid. Read per-combatant properties instead.
- `EncId`/`GetHashCode`/`Equals` on live encounters: O(all swings) string hashing.
- Per-combatant `Duration` goes expensive once silence cutting splits segments; `EncDPS`
  divides by encounter duration and stays cheap.
- Combatant identity is name-based, case-insensitive; ACT's rename/redirect corrections happen
  before `AfterCombatAction` sees the swing.
- `ActGlobals.restrictToAll = true` disables per-ability buckets (memory saver, kills
  breakdowns); `disableIncrementalCaching = true` forces full recomputes. Leave both alone.

## Implications for eq2auras (pointers, not spec)

- **The meter data path writes itself:** subscribe `AfterCombatAction` (already under the data
  lock, model already updated) → fold each `MasterSwing` into our own thread-safe accumulator
  → drive WPF from our own wall clock. `OnCombatStart`/`OnCombatEnd` for reset/finalize —
  totals are guaranteed complete at `OnCombatEnd` (drain-wait).
- **Hybrid alternative** when we want ACT-computed values (ally classification, success level,
  export variables): poll cheap per-combatant properties (`Damage`, `Healed`, `DamageTaken`,
  `Deaths`, `EncDPS`) at 1–4 Hz under `AfterCombatActionDataLock` and interpolate visually —
  the mini parse's data path minus its cadence and rendering sins.
- **The wall clock owns the visuals, again:** smooth DPS =
  `damage / (LastEstimatedTime − encounter.StartTime)` (the `{duration}` logic), never the
  log-time `Duration` that ACT's own DPS variables freeze on.
- The `{var}` ExportVariables system is a ready-made *user-facing metric vocabulary* — and a
  plugin-extensible one. Worth considering as (or mapping onto) the meter's column/metric
  config surface.
- `Tags` dictionaries at every level are sanctioned plugin scratch space on ACT's own objects.
- The swing stream's exact shape (SwingTypes, wards-as-heals, fail sentinels, pet naming,
  the registration-wipe hazard) is documented in §The EQ2 parsing plugin from its vendored
  source — closing the "known unknown" this section previously carried. Remaining
  `[field]`-verification value: confirm the raid-scale stream matches (exotic log lines the
  14 regexes miss simply produce no swings), and re-verify after the parser plugin
  self-updates.
