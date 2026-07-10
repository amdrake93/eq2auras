# ACT parse engine — raw research report (2026-07-09)

**What this is:** the verbatim output of the decompilation research agent that produced
`docs/act-parse-engine.md`. The reference doc is the maintained distillation; this archive
preserves the complete citation-level detail (file:line references against the full-assembly
ilspycmd decompile — reproduce it with the recipe at the top of the reference doc; line
numbers are stable for the exact binary vendored in `ThirdParty/`).

**Review status:** third-party re-derivation review 2026-07-09 — approved; ~20 claims
spot-checked against the binary, all landed except one. **Known errors, corrected in the
reference doc but left as-is below:** the `Dnum` claim "values −2..−8 are
resist/parry/riposte/block variants" is wrong — the mapping is Resist=−2, Parry=−3,
Riposte=−4, Block=−5, with −6..−8 unmapped; and `operator +` ignores *negative* values (zero
participates), not "non-positive". Claims the review sampled but did not individually verify
were re-verified against the decompile on 2026-07-09: `nudMiniUpdateInterval` bounds min 1 /
max 60 (`Options_MiniParse.cs:239–240`) and the GDI `MeasureString` column alignment
(`FormActMain.cs:17010–17033`).

---

# ACT Combat Data Model & Mini Parse — Decompilation Report

Source: full ILSpy decompile of `/Users/Alex/repos/eq2auras/ThirdParty/Advanced Combat Tracker.exe` (namespace `Advanced_Combat_Tracker`), decompiled to scratchpad. Every claim below was verified against decompiled method bodies, not API docs.

---

## 1. Core combat data model

The hierarchy nests five levels deep. All of it lives in-process; nothing is a database view.

```
FormActMain.ZoneList : List<ZoneData>                 (public get/set property)
└─ ZoneData
   ├─ .Items : List<EncounterData>                    (Items[0] = merged "All" encounter when PopulateAll)
   └─ .ActiveEncounter : EncounterData                (public get/set — the live encounter)
      └─ EncounterData.Items : SortedList<string, CombatantData>   (key = combatant name .ToUpper())
         └─ CombatantData.Items : Dictionary<string, DamageTypeData>  (key = damage-type label, e.g. "Outgoing Damage")
            └─ DamageTypeData.Items : SortedList<string, AttackType>  (key = attack/ability name, incl. "All")
               └─ AttackType.Items : List<MasterSwing>                (the raw swings; the only real data)
```

Everything above `MasterSwing` is an *aggregation view* over swing lists. Each level also carries a `public Dictionary<string, object> Tags` for arbitrary plugin data (present on ZoneData, EncounterData, CombatantData, DamageTypeData, AttackType, MasterSwing).

### MasterSwing — the atom

```csharp
public MasterSwing(int SwingType, bool Critical, string Special, Dnum damage, DateTime Time,
                   int TimeSorter, string theAttackType, string Attacker, string theDamageType, string Victim)
```
Exposed read-only: `Time`, `TimeSorter`, `SwingType`, `Damage` (Dnum), `Attacker`, `Victim`, `AttackType` (ability name), `DamageType` (e.g. "heat", "slashing"), `Critical` (get/set), `Special`, `ParentEncounter` (get/set, stamped by `EncounterData.AddCombatAction`), `Tags`.

`Dnum` is a damage-number wrapper with implicit `long` conversions and sentinel statics: `Dnum.NoDamage` = 0, `Dnum.Miss` = -1, `Dnum.Unknown` = -9, `Dnum.Death` = -10, `Dnum.ThreatPosition` = -11 (values -2..-8 are resist/parry/riposte/block variants rendered via localization). `Dnum.Number` is the raw long; `DamageString` carries the display text. Implicit `long→Dnum` clamps anything < -10 to Unknown. Note `operator +` ignores negative (non-damage) values.

`SwingTypeEnum`: `Melee = 1, NonMelee = 2, Healing = 3, PowerDrain = 10, PowerHealing = 13, Threat = 16, CureDispel = 20`. `MasterSwing.SwingType` is a plain `int` (games can define others).

### AttackType — incremental aggregation (this is where the numbers come from)

`AttackType` computes every metric **lazily with incremental caching keyed on `Items.Count`**: each cached metric stores the index it has consumed up to (e.g. `damageCached`) and only sums the *new* swings since last read:

```csharp
public long Damage {                      // AttackType.cs
    get {
        if (damageCached == Items.Count) return cDamage;
        long num = cDamage;
        for (int i = damageCached; i < Items.Count; i++)
            if ((long)Items[i].Damage > 0) num += Items[i].Damage;
        cDamage = num; damageCached = Items.Count; return num;
    } }
```

Metrics on `AttackType`: `Damage` (only positive Dnums), `Hits` (Damage > 0, or ≥ 0 if `ActGlobals.blockIsHit`, default true), `CritHits`, `CritPerc`, `Swings` (excludes Death rows unless the type *is* the "Killing" bucket), `Misses` (Damage == Miss), `Blocked` (Damage < -1 && != Death), `Median` (non-incremental, sorts all positive swings), `MinHit`/`MaxHit`, `StartTime`/`EndTime` (min/max swing Time, incremental), `Duration` (EndTime−StartTime, or a complex multi-segment walk when the parent encounter has multiple StartTimes — see §5), `DPS` = Damage/own Duration, `CharDPS` = Damage/CombatantData duration, `EncDPS` = Damage/`Parent.Parent.Parent.Duration` (the *encounter* duration), `AverageDelay`, `Resist` (dominant DamageType string), `GetAttackSpecials()`.

`AttackType.AddCombatAction(MasterSwing)` just does `InvalidateCachedValues(ActGlobals.disableIncrementalCaching); Items.Add(action);` — i.e. by default the incremental counters are NOT reset per swing, only the non-incremental ones (median/duration/resist/avgdelay). **Consequence: the model assumes swings are append-only. Mutating `MasterSwing.Damage`/history after insertion silently corrupts cached sums unless `InvalidateCachedValues(true)` is called up the tree.**

### DamageTypeData — per-category bucket

One `DamageTypeData` per category label per combatant (`"Outgoing Damage"`, `"Healed (Out)"`, `"Incoming Damage"`, …). All metrics delegate to its `"All"` AttackType: `Damage`, `Swings`, `Hits`, `CritHits`, `CritPerc`, `Misses`, `Blocked`, `ToHit`, `Average`, `Median`, `MinHit`, `MaxHit`, `EncDPS`, `CharDPS`, `DPS`, `StartTime`, `EndTime`, `Duration`, `AverageDelay`. `AddCombatAction(MasterSwing action, string theAttackTypeListed)` creates/gets the named `AttackType` and appends. Note `CritHits` has a hardcoded English `"All"` lookup (`attackTypes.TryGetValue("All", ...)`) unlike the rest which use `ActGlobals.Trans["attackTypeTerm-all"]` — a latent localization bug worth knowing.

### CombatantData — a combatant in one encounter

Constructor builds one `DamageTypeData` per entry in the **static** `CombatantData.OutgoingDamageTypeDataObjects` / `IncomingDamageTypeDataObjects` dictionaries. The static routing tables (populated by `FormActMain.SetupEQ2EnglishEnvironment()`, see §2) decide which buckets each swing lands in:

```csharp
// CombatantData.AddCombatAction (outgoing side)
List<string> list = SwingTypeToDamageTypeDataLinksOutgoing[action.SwingType];
for (...) {
    ModAlly(victim, OutgoingDamageTypeDataObjects[dtd.Type].AllyValue);   // ally-graph bookkeeping
    items[list[i]].AddCombatAction(action, ActGlobals.Trans["attackTypeTerm-all"]);
    if (!ActGlobals.restrictToAll)
        items[list[i]].AddCombatAction(action, action.AttackType);        // per-ability bucket
}
outAll.AddCombatAction(action, "All"); outAll.AddCombatAction(action, action.AttackType);
```
Every swing is therefore inserted **multiple times** (category "All" + category per-ability + outAll "All" + outAll per-ability; mirrored on the victim's incoming side via `AddReverseCombatAction`). `outAll`/`incAll` are the `"All Outgoing (Ref)"` / `"All Incoming (Ref)"` reference buckets exposed as `AllOut` / `AllInc`.

EQ2 routing (from `SetupEQ2EnglishEnvironment`, FormActMain.cs:2955+): outgoing `1→{"Auto-Attack (Out)","Outgoing Damage"}`, `2→{"Skill/Ability (Out)","Outgoing Damage"}`, `3→"Healed (Out)"`, `10→"Power Drain (Out)"`, `13→"Power Replenish (Out)"`, `20→"Cure/Dispel (Out)"`, `16→"Threat (Out)"`; incoming `1,2→"Incoming Damage"`, etc. Also `DamageSwingTypes = {1,2}`, `HealingSwingTypes = {3}`, and the well-known label aliases:
`CombatantData.DamageTypeDataOutgoingDamage = "Outgoing Damage"`, `...OutgoingHealing = "Healed (Out)"`, `...IncomingDamage = "Incoming Damage"`, `...IncomingHealing = "Healed (Inc)"`, `...OutgoingPowerReplenish/OutgoingPowerDamage/OutgoingCures`, `...NonSkillDamage = "Auto-Attack (Out)"`.

Metrics (all on-demand; those marked ⚡ are direct index into `items[...]`, cheap; ✦ are boolean-cached until next AddCombatAction):
- ⚡ `Damage`, `DamageTaken`, `Healed`, `HealsTaken`, `PowerReplenish`, `PowerDamage`, `Swings`, `Hits`, `Misses`, `Blocked`, `CritHits`, `CritDamPerc`, `CritHeals`, `CritHealPerc`, `Heals`, `CureDispels`, `ToHit`
- `DPS` = `Damage / Duration.TotalSeconds` (personal duration); `EncDPS` = `Damage / Parent.Duration.TotalSeconds`; `ExtDPS` ≡ EncDPS; `EncHPS`/`ExtHPS` = `Healed / parent.Duration`
- `DamagePercent` / `HealedPercent` (share of ally total, "--" if not an ally)
- ✦ `Deaths` (counts `Dnum.Death` in incoming "Killing" bucket, falls back to scanning AllInc "All"), ✦ `Kills` (Death swings in AllOut; non-allies only count kills of one-word victims), ✦ `StartTime`/`EndTime` (from outAll), `ShortEndTime` = `items["Outgoing Damage"].EndTime` (last *damage* action — heals don't extend it), ✦ `Duration` (multi-segment aware), ✦ `GetThreatDelta`/`GetThreatStr`
- `GetMaxHit`/`GetMaxHeal` (linear scans over the "All" outgoing bucket), `GetMaxHealth()` (running min of heal−damage), `GetCombatantType()` (0 enemy / 1 tank-ish / 2 healer / 3 auto-attack / 4 dps heuristic), `GetAttackType(name, typeLabel)`
- `Allies : SortedList<string,int>` — accumulated ally weights from `ModAlly`; feeds `EncounterData.GetAllies()`.

**Caching model:** boolean caches are invalidated by `AddCombatAction`/`AddReverseCombatAction` (they clear `durationCached/startTimeCached/endTimeCached/threatCached`, plus kills/deaths only when the swing is a Death). The metric getters are otherwise fully recomputed on demand from AttackType incremental caches. There is no timer-based refresh; reading is always current as of the last applied swing.

### EncounterData

- Static: `ExportVariables`, `ColumnDefs` (§2).
- `Items : SortedList<string, CombatantData>` (UPPERCASE keys), `Parent : ZoneData`, `Title` (set at combat end to `GetStrongestEnemy()`), `ZoneName`, `CharName`, `Active : bool`, `StartTimes`/`EndTimes : List<DateTime>` (multi-segment; getters strip MaxValue/MinValue sentinels), `LogLines : List<LogLineEntry>` (only populated if Record Log Lines option on), `HistoryRecord`, `Tags`, `DuplicateDetection` (dedupe by `MasterSwing.TimeSorter` HashSet — used on imports).
- `StartTime` = min combatant StartTime; `EndTime` = (with default `ActGlobals.longDuration == false`) `ShortEndTime` = max **ally** `CombatantData.ShortEndTime` — i.e. the encounter "ends" at the last outgoing-damage action of an ally, so trailing heals/HoTs do not stretch ENCDPS.
- `Duration` = EndTime−StartTime, or a per-segment sum when silence-cutting created multiple StartTimes.
- Aggregates loop over `GetAllies()` each call, **no caching**: `Damage`, `Healed`, `AlliedKills`, `AlliedDeaths` (one-word names only = players), `DPS = Damage/Duration`, `NumCombatants/NumAllies/NumEnemies`, `GetMaxHit/GetMaxHeal`, `GetEncounterSuccessLevel()` (1 win / 2 partial / 3 wipe), `GetStrongestEnemy()` (highest DamageTaken/Deaths among non-allies).
- `GetAllies()`: breadth-first walk of the `CombatantData.Allies` weight graph seeded from `GetCombatant(CharName)`; positive-weight closure = allies. Result cached (`alliesCached`) until any `ModAlly` with nonzero mod calls `SetAlliesUncached()`; `GetAllies(allowLimited: true)` additionally reuses a result computed in the same wall-clock second. `SetAllies(list)` lets a plugin pin the ally list manually (`alliesManual`).
- `AddCombatAction(MasterSwing)`: stamps `ParentEncounter`, creates `CombatantData` for attacker (forward) and victim (`AddReverseCombatAction`), honoring selective-parsing filters.
- `EndCombat(bool Finalize)`: `lock (ActGlobals.actionDataLock)`, sets `Active=false`, closes the EndTimes segment, and when finalizing calls `Trim()` and sets `Title = GetStrongestEnemy(...)`.
- `EncId` = `GetHashCode().ToString("x8")`, and **`GetHashCode()` sums the hash of every swing in the encounter** (CombatantData→DamageTypeData→AttackType→each MasterSwing.ToString().GetHashCode()). Touching `EncId`/`Equals` on a large live raid encounter is an O(total swings) string-hash storm — do not read it per tick.

### ZoneData (ZoneData.cs, 75 lines)

`StartTime`, `ZoneName`, `Items : List<EncounterData>`, `ActiveEncounter`, `PopulateAll`, `Tags`. If `PopulateAll` (option "Zone All listing"), `Items[0]` is a merged "All" EncounterData for the zone and `ZoneData.AddCombatAction` feeds **both** `Items[0]` and `Items[Items.Count-1]` (the current encounter), activating them and appending StartTimes as needed. So during combat the last element of `Items` *is* the ActiveEncounter, and the zone-wide "All" accumulates in parallel — there is **no merge step at combat end**; "All" is built live.

---

## 2. ColumnDefs / ExportVariables — the customization system

Two parallel static systems exist at each level. **All of them are `public static` and mutable — a plugin can add, replace, or clear entries.**

### ColumnDefs (table cells + SQL export)
- `EncounterData.ColumnDefs : Dictionary<string, EncounterData.ColumnDef>`; ctor `ColumnDef(string Label, bool DefaultVisible, string SqlDataType, string SqlDataName, StringDataCallback CellDataCallback, StringDataCallback SqlDataCallback)` where `StringDataCallback = string (EncounterData Data)`, plus public `GetCellForeColor`/`GetCellBackColor` color callbacks.
- `CombatantData.ColumnDefs` — same shape plus a `Comparison<CombatantData> SortComparer` (used by `DualComparison` and `CompareTo` via `ActGlobals.eDSort`/`eDSort2`; defaults `"EncDPS"`).
- `DamageTypeData.ColumnDefs`, `AttackType.ColumnDefs` (with `SortComparer`, sorted by `ActGlobals.mDSort`), `MasterSwing.ColumnDefs` (with `SortComparer`, `ActGlobals.aTSort`, default `"Time"`).
- Each type exposes `GetColumnByName(string)` → `ColumnDefs[name].GetCellData(this)`, and `ColCollection`/`ColHeaderCollection`/`ColTypeCollection` for SQL/HTML exporters.
- After adding ColumnDefs a plugin calls `ActGlobals.oFormActMain.ValidateTableSetup()` (public, FormActMain.cs:24066) which syncs the options-tab checkbox lists to the dictionaries (it no-ops during `InitACT` unless forced). `ValidateLists()` similarly re-syncs view dropdowns.

### ExportVariables (the `{var}` format strings — what the mini parse uses)
- `CombatantData.ExportVariables : public static Dictionary<string, CombatantData.TextExportFormatter>`; formatter ctor `TextExportFormatter(string Name, string Label, string Description, ExportStringDataCallback FormatterCallback)` with `delegate string ExportStringDataCallback(CombatantData Data, string ExtraFormat)`.
- `EncounterData.ExportVariables` — same but callback `string (EncounterData Data, List<CombatantData> SelectiveAllies, string ExtraFormat)` (it receives the filtered ally list).

**Resolution mechanics** (`FormActMain.GetTextExport(bool RtfFormat, EncounterData, TextExportFormatOptions, Font, int MaxLines)`, FormActMain.cs:16886):
```csharp
public Regex TextExportFormatterRegex { get; set; }
    = new Regex("{(?<formatter>[^}:]+)(?::(?<extra>[^}]+))?}|(?<text>[^{]+)", RegexOptions.Compiled);
```
A format is a `TextExportFormatOptions(string PlayerFormat, string Sorting, bool ShowOnlyAllies, bool ShowAlliedInfo, string AlliesFormat)`. The exporter:
1. Snapshots `Encounter.Items.Values` into a list (bare `try/catch` returning `""` on concurrent-mutation exceptions — this is ACT's entire "thread safety" for exports!), sorts with `CombatantData.DualComparison(sorting, sorting)` and reverses (descending).
2. Runs `AlliesFormat` once through the regex; `{var}` or `{var:extra}` tokens are looked up in **`EncounterData.ExportVariables`** and invoked with the encounter + ally list; unknown tokens are re-emitted literally as `{var}`.
3. For each combatant (skipping names with spaces if the filter option is on, non-allies if `ShowOnlyAllies`, and "Unknown"), runs `PlayerFormat` through the regex against **`CombatantData.ExportVariables`**.
4. Optional column alignment (`TabulateFont != null`): tokens are measured with `Graphics.MeasureString` and padded — this is the mini parse "Column alignment" checkbox.
5. Output truncated to `MaxLines` (default 256).

The `:extra` suffix is passed as `ExtraFormat` — e.g. `{NAME:8}` → `CombatantFormatSwitch` case `"NAME"` parses `Extra` as an int and truncates the name.

**Built-in variable names** — all registered inside `FormActMain.SetupEQ2EnglishEnvironment()` (FormActMain.cs:2762–3187), which `Clear()`s and repopulates all five ColumnDefs dictionaries and both ExportVariables dictionaries, then runs during `InitACT` Section 6 — *before* plugins load, so plugin registrations are never wiped (a game-parsing plugin may call its own environment setup and re-clear, though).

Encounter-level (`EncounterData.ExportVariables` keys): `n`, `t`, `title`, `duration`, `DURATION`, `damage`, `damage-m`, `damage-*`, `DAMAGE-k/-m/-b/-*`, `dps`, `dps-*`, `DPS`, `DPS-k/-m/-*`, `encdps`, `encdps-*`, `ENCDPS`, `ENCDPS-k/-m/-*`, `hits`, `crithits`, `crithit%`, `misses`, `hitfailed`, `swings`, `tohit`, `TOHIT`, `maxhit`, `MAXHIT`, `maxhit-*`, `MAXHIT-*`, `healed`, `enchps`, `enchps-*`, `ENCHPS`, `ENCHPS-k/-m/-*`, `heals`, `critheals`, `critheal%`, `cures`, `maxheal`/`MAXHEAL`/`maxhealward`/`MAXHEALWARD` (+`-*` variants), `damagetaken(-*)`, `healstaken(-*)`, `powerdrain(-*)`, `powerheal(-*)`, `kills`, `deaths`.

Combatant-level (`CombatantData.ExportVariables` keys): all of the above minus `title` plus `name`, `NAME` (`:n` extra), `NAME3`…`NAME15` (truncated), `damage%`, `healed%`, `crittypes`, `threatstr`, `threatdelta`, `damage-b`, `DAMAGE-b`.

They all funnel to two giant switches: `FormActMain.EncounterFormatSwitch(EncounterData, List<CombatantData> SelectiveAllies, string VarName, string Extra)` (line 3272 — encounter values are *sums over the ally list*, e.g. `encdps` = Σ ally Damage / `Data.Duration`) and `FormActMain.CombatantFormatSwitch(CombatantData, string VarName, string Extra)` (line 3680 — direct property reads, e.g. `"encdps"` → `Data.EncDPS.ToString("F")`).

**Wall-clock nuance:** encounter-level `{duration}`/`{DURATION}` (only these!) use `ActGlobals.wallClockDuration` (default **true**): while `Data.Active`, duration = `ActGlobals.oFormActMain.LastEstimatedTime - Data.StartTime`, where `LastEstimatedTime => LastKnownTime + estimatedTimer.Elapsed` (a Stopwatch restarted on every parsed timestamp — FormActMain.cs:2660). Every DPS/HPS variable however divides by **log-time** `Duration` (last-ally-damage-swing minus first swing). So mid-fight ENCDPS in the mini parse does *not* decay during log silence — same class of lurch the timer work already hit.

**Plugin registration recipe** (verified all types/members public):
```csharp
CombatantData.ExportVariables.Add("mymetric",
    new CombatantData.TextExportFormatter("mymetric", "My Metric", "desc",
        (CombatantData d, string extra) => d.Damage.ToString()));
// and/or table column:
CombatantData.ColumnDefs.Add("MyCol", new CombatantData.ColumnDef("MyCol", true, "BIGINT", "MyCol",
    d => d.Damage.ToString(), d => d.Damage.ToString(), (l, r) => l.Damage.CompareTo(r.Damage)));
ActGlobals.oFormActMain.ValidateTableSetup();
```
Custom presets: `FormActMain.AddTextFormat(TextExportFormatOptions)` / `RemoveTextFormat(int)` (public, line 13253) append to the shared `TextExportFormats` list *and* to the clipboard/mini dropdowns.

---

## 3. The Mini Parse window

**Renderer:** `FormMiniParse` (FormMiniParse.cs) — a borderless-capable, `TopMost`, `TransparencyKey = Magenta` WinForms `Form` holding exactly two data controls: `internal RichTextBox rtb2` (text mode; black background, yellow text hardcoded in `InitializeComponent`, colors overridden at runtime from `Options_MiniParse.fccMiniParse`) and `internal PictureBox pb1` (graph mode), toggled by a tiny red `CheckBox cbbDisplayGraph` in the corner. The singleton is `ActGlobals.oFormMiniParse` (public static). Click-through = `SetClickThrough(bool)` via `WS_EX_TRANSPARENT` window style. Closing it just hides it.

**The form has no data logic.** All content is pushed by **`FormActMain.UpdateMiniEnc()`** (public, FormActMain.cs:16789):
- Graph mode → `pb1.Image = GenEncounterGraph(ActiveZone.ActiveEncounter, pb1.Width, pb1.Height, <preset>.Sorting)`.
- Text mode → picks `TextExportFormatOptions` = `textExportFormats[opMiniParse.ddlMiniFormat.SelectedIndex]` or `defaultTextFormat`, then
  `string text = GetTextExport(ActiveZone.ActiveEncounter, exportFormatting[, rtb2.Font, 0])` and, only if changed, `ThreadInvokes.ControlSetText(this, rtb2, text)` (marshals to UI thread).

Default format (FormActMain.cs:2288):
```csharp
private TextExportFormatOptions defaultTextFormat = new TextExportFormatOptions(
    "{n}{NAME5} | {encdps-*}", "EncDPS", ShowOnlyAllies: true, ShowAlliedInfo: true,
    "({duration}) {title}: {encdps-*} {maxhit-*}");
```
i.e. header line = allies format, then one line per ally sorted by EncDPS desc.

**Refresh cadence** — driven entirely by `tmrTick`, a WinForms `Timer` with `Interval = 1000` on the **UI thread** (`tmrTick_Tick`, FormActMain.cs:8196):
```csharp
if (!importThreadAlive && ((Control)ActGlobals.oFormMiniParse).Visible
    && globalTicks % (int)opMiniParse.nudMiniUpdateInterval.Value == 0)
    UpdateMiniEnc();                                    // line 8361–8364, inside `if (InCombat)`
```
`nudMiniUpdateInterval`: min 1, max 60, **default 5** (Options_MiniParse.cs:239–244) — so the stock mini parse repaints at best once per second, default every 5 s, and **only while `InCombat`**. Outside combat the only refreshes are: one final `UpdateMiniEnc()` at the end of `EndCombat` (line 22388–2391) and on graph/text mode flip (`FormMiniParse.btnDisplayFlip_Click`). Right-clicking the window pops a menu of all `TextExportFormats`; the chosen index is stashed in `FormMiniParse.formatIndex` and applied by the next `tmrTick` (line 8240) into `opMiniParse.ddlMiniFormat`. tmrTick also recolors the window (combat = `fccMiniParse.BackColorSetting`, idle = `SystemColors.GrayText`) and swaps the title between "Mini Parse"/combat text.

**Known limitations (from the code):**
1. 1 s minimum cadence, quantized to the shared `globalTicks` counter — not smooth, not sub-second.
2. Whole-text replacement of a RichTextBox — no retained elements, no animation, flickers on update; column alignment is done by measuring space widths.
3. Data path reads `ActiveZone.ActiveEncounter` **without taking `actionDataLock`** — it relies on `try/catch` around the `Items.Values` copy (`catch { return string.Empty; }`) to survive concurrent mutation by the after-action thread. A racy tick can render an empty string.
4. Log-time DPS (see §2) — values freeze during log silence.
5. Suppressed entirely during imports (`importThreadAlive`).
6. Per-tick cost is real: `GetTextExport` sorts all combatants, and `ShowOnlyAllies` forces `GetAllies()` graph walks; ACT logs the durations (`WriteDebugLog("UpdateMiniEnc: …ms")`).

---

## 4. Real-time hook points for a plugin  ⟵ **the section that matters for eq2auras**

Plugin model: `public interface IActPluginV1 { void InitPlugin(TabPage, Label); void DeInitPlugin(); }` — `InitPlugin` runs on the UI thread; subscribe to events there. All events live on `FormActMain` (via `ActGlobals.oFormActMain`). Every invocation site wraps handlers in try/catch (`WriteExceptionLog`), and after a first handler exception flips `debugHandlers = true` which switches to per-delegate invocation with per-assembly exception counting — a throwing handler is never fatal but gets you named in the error log.

### The pipeline, end to end (live parse)

```
"Log Reader" thread (background, created in FormActMain, name "Log Reader")
  ReadLog() [20830]: poll FileStream every 10ms → ReadToEnd → split lines
    per line: LastKnownTime = GetDateTimeFromLog(line); GlobalTimeSorter++;
              ParseRawLogLine(false, LastKnownTime, line)  [21622, lock(rawLogLineLock)]
                ├─ CheckIdleEndCombat()            → may call EndCombat(true)  (reader thread!)
                ├─ BeforeLogLineRead(isImport, LogLineEventArgs)   ← parsing plugin lives here
                │     the EQ2 parsing plugin calls, synchronously on this thread:
                │       oFormActMain.SetEncounter(time, attacker, victim)  → fires OnCombatStart
                │       oFormActMain.AddCombatAction(MasterSwing)          → fires BeforeCombatAction,
                │                                                            then enqueues to afterActionsQueue
                ├─ (record LogLines, queue custom triggers, XML share scan)
                └─ OnLogLineRead(isImport, LogLineEventArgs)       ← observers; line already parsed

"AfterActionQueueThread" (background)  ThreadAfterCombatAction() [20505]
  loop: if queue empty → Sleep(1)
        lock (AfterCombatActionDataLock)          // == ActGlobals.actionDataLock
          drain afterActionsQueue → for each MasterSwing:
            ActiveZone.AddCombatAction(swing)     // ← the ONLY place the live model is mutated
            AfterCombatAction(importThreadAlive, new CombatActionEventArgs(swing))

UI thread: tmrTick (1s) → idle EndCombat, UpdateMiniEnc, tree refresh
```

**Note on the binary you ship against:** this ACT build contains no built-in combat-line parser — `SetEncounter` has zero internal callers. `SetupEQ2EnglishEnvironment()` only installs the EQ2 data-field environment and `GetDateTimeFromLog = ParseDateTimeFromEQ2Log` (line 4607). The actual `SetEncounter`/`AddCombatAction` calls come from the installed EQ2 parsing plugin via `BeforeLogLineRead`. FormActMain even logs `"BeforeLogLineRead is not handled"` at startup if nothing subscribed (line 9080–9082).

### Event catalog (exact signatures, thread, payload)

| Event (on `FormActMain`) | Delegate signature | Fires | Thread | Notes |
|---|---|---|---|---|
| `BeforeLogLineRead` | `LogLineEventDelegate(bool isImport, LogLineEventArgs logInfo)` | per raw log line, before anything else (after idle check) | Log Reader thread (or import thread when `isImport`) | **Mutable**: handler may rewrite `e.logLine` and set `e.detectedType`; this is the parsing-plugin hook. `LogLineEventArgs`: `logLine` (rw), `detectedType` (rw int), readonly `detectedTime`, `detectedZone`, `inCombat`, `originalLogLine`, `companionLogName`. |
| `OnLogLineRead` | same | per line, after parsing/trigger queueing | same | Read-only observation; `e.logLine` already possibly rewritten. |
| `BeforeCombatAction` | `CombatActionDelegate(bool isImport, CombatActionEventArgs actionInfo)` | inside `AddCombatAction`, before queueing | thread that called `AddCombatAction` (= reader thread live) | **Mutable/cancelable**: fields `swingType, critical, attacker, theAttackType, damage, time, timeSorter, victim, theDamageType, special, tags` are public and are copied back onto the MasterSwing afterward (strings interned); set `cancelAction = true` to drop the swing entirely. `combatAction` = the readonly MasterSwing. |
| `AfterCombatAction` | same | per swing, immediately **after** `ActiveZone.AddCombatAction(swing)` applied it to the model, while still holding `AfterCombatActionDataLock` | **AfterActionQueueThread** | The prime meter hook: the model already includes this swing; `actionInfo.combatAction.ParentEncounter` is set. Do NOT block — you're inside the lock that gates all stat application. |
| `OnCombatStart` | `CombatToggleEventDelegate(bool isImport, CombatToggleEventArgs encounterInfo)` | inside `SetEncounter` when `!InCombat` transitioned, *after* the new `ActiveZone.ActiveEncounter` is created & added | caller of `SetEncounter` = reader thread (live) / import thread | `CombatToggleEventArgs`: readonly `zoneDataIndex`, `encounterDataIndex`, `encounter` (the fresh EncounterData; empty at this point — the triggering swing arrives via the queue *afterwards*). |
| `OnCombatEnd` | same | inside `EndCombat(bool export)` after `EncounterData.EndCombat(Finalize:true)` (title set, times closed) | **varies**: UI thread (tmrTick idle timer / UI button), reader thread (`CheckIdleEndCombat` in ParseRawLogLine, "/act end" command via plugin→`ActCommands`), import thread | Critically, `EndCombat` first **spin-waits until `afterActionsQueue` is fully drained** (lines 22229–22244), so when `OnCombatEnd` fires the encounter totals are complete and final. |
| `OnCombatToggle` | — **does not exist** in this binary. The pair is `OnCombatStart`/`OnCombatEnd`. |
| Others available | `LogFileChanged(bool IsImport, string NewLogFileName)`, `LogFileRenamed`, `ActLifecycleChanged` (Init/Config/plugin phases), `XmlSnippetAdded`, `UrlRequest`, `BeforeClipboardSet`, `UpdateCheckClicked` | | | |

### Reading the current encounter mid-combat

Verified path: **`ActGlobals.oFormActMain.ActiveZone.ActiveEncounter`** — `ActiveZone` is `public ZoneData ActiveZone { get; set; }` (FormActMain.cs:2584), `ActiveEncounter` is `public EncounterData ActiveEncounter { get; set; }` (ZoneData.cs:16). Companion state: `public bool InCombat { get; set; }` (2582), `public List<ZoneData> ZoneList` (2580), `LastKnownTime` / `LastEstimatedTime` (log clock + Stopwatch extrapolation), `LastHostileTime`, `GlobalTimeSorter { get; set; }` (2490). Caveats:
- `ActiveEncounter` is **null** until the first `SetEncounter` of the session (ACT's own code null-checks it, e.g. line 1227) and goes *stale* (not null) after combat — check `InCombat` / `encounter.Active`.
- The zone-wide merged view is `ActiveZone.Items[0]` when `ActiveZone.PopulateAll`.

### Thread-safety of mid-combat reads

- All model mutation happens on the AfterActionQueueThread inside `lock (AfterCombatActionDataLock)` (`public object AfterCombatActionDataLock => ActGlobals.actionDataLock;`, FormActMain.cs:2702). `EncounterData.EndCombat` takes the same lock.
- **Correct pattern for a meter plugin reading from its own thread:** `lock (ActGlobals.oFormActMain.AfterCombatActionDataLock) { …read Items/metrics… }`. Holding it briefly is what ACT plugins are expected to do; holding it long stalls stat application (parsing itself continues — swings just pile up in `afterActionsQueue`).
- Alternatively, read inside your `AfterCombatAction` handler — you're already under the lock there.
- ACT's own UI readers (mini parse, HTML export) do **not** lock; they copy `Items.Values` under try/catch and tolerate the occasional failure. `SortedList` enumeration during `Add` can throw or, worse, hand back a torn snapshot — for a raid meter, take the lock.
- Metric getters mutate cache fields (`AttackType` incremental counters, `CombatantData` bool caches) — so even "read-only" property access is not thread-safe against another *reader*; serialize your reads through the same lock and you're fine (the writer thread also reads them via ACT internals under the lock).

---

## 5. Encounter lifecycle

1. **Start:** parsing plugin sees a hostile line → `SetEncounter(DateTime Time, string Attacker, string Victim)` (public, FormActMain.cs:22084). If `!InCombat`: resolves/creates the right `ZoneData` (zone changes are detected out-of-band via `ZoneChangeRegex` / `FindZoneName`, which set `CurrentZone`), constructs a fresh `EncounterData` as `ActiveZone.ActiveEncounter`, appends it to `ActiveZone.Items`, fires `OnCombatStart`, sets `lastSetEncounter`. Always: resets `idleCounter`, sets `LastHostileTime = Time`, `InCombat = true`, and `CheckIdleCutSilence(Time)`. Returns false only in full-selective mode when neither party is selected.
2. **Silence cutting:** `CheckIdleCutSilence` — if the gap since the last hostile `SetEncounter` exceeds `opMainTableGen.silenceLimit`, it closes an EndTimes/StartTimes segment pair on the active (and zone-"All") encounter *without* ending combat. This is why `StartTimes`/`EndTimes` are lists and why every `Duration` implementation has the gnarly multi-segment walk (EncounterData.Duration sums segments; CombatantData/DamageTypeData/AttackType.Duration intersect their own swing timeline with the encounter's segments — heavy, cached).
3. **Accumulation:** every `AddCombatAction` (public overloads at FormActMain.cs:20413/20418; **throws `InvalidOperationException` if `!InCombat`**) → rename/redirect corrections (`RenameCombatant`, `RedirectAbility`) → `BeforeCombatAction` (cancelable) → spell-timer `NotifySpell` → queued. The queue is drained in batches by the after-action thread which stamps the swings into `ActiveZone` (both current and "All" encounter) and fires `AfterCombatAction` per swing. So the model is *near*-real-time: per log line, with sub-millisecond queue latency under normal load; batching only matters under import-style floods.
4. **Timestamps & ordering:** the visible clock is log time (`GetDateTimeFromLog`, EQ2 layout parsed by `ParseDateTimeFromEQ2Log` — second resolution!). `GlobalTimeSorter` (monotonic int, ++ per log line) provides intra-second total ordering (`MasterSwing.CompareTime` sorts by TimeSorter then Time).
5. **End:** `EndCombat(bool export)` (public, 22218) — triggered by (a) idle: reader thread when `LastKnownTime − LastHostileTime > nudIdleLimit` (`CheckIdleEndCombat`, 20362), (b) UI tick fallback with `cbIdleTimerEnd` (wall-clock idleCounter, 8297–8303), (c) user/game command ("/act end" style → plugin → `ActCommands("end")`), (d) zone change/log switch paths, (e) import boundaries. Sequence: `InCombat = false` → **drain wait** on afterActionsQueue → `ActiveZone.Items[0].EndCombat(false)` + `ActiveEncounter.EndCombat(Finalize:true)` (Trim + Title = strongest enemy) → `OnCombatEnd` → history DB record (`HistoryRecord`) → tree label/color by `GetEncounterSuccessLevel()` → `CullEncounters()` → final `UpdateMiniEnc()` → optional clipboard/macro/HTML/ODBC exports.
6. **Culling (data retention!):** `CullEncounters` (17957) runs at every combat end honoring Options_EncCulling: remove titleless ("no ally") encounters, encounters older than N minutes, keep only N encounters, drop zone-"All" lists beyond N zones, etc. **A meter plugin must not keep references to `EncounterData` across fights and assume ACT still shows them** — they may have been culled from `ZoneList`; the object you hold stays alive but orphaned.
7. **No merge at end** — the zone "All" encounter (`Items[0]`) was fed live in parallel (see §1/ZoneData); combat end merely closes its time segment.

---

## 6. Additional load-bearing findings

- **Import vs live:** imports run on `importThread` and reuse the exact same pipeline (`ParseRawLogLine(isImport: true, …)`, same events with `isImport == importThreadAlive == true`). A meter plugin should early-out on `isImport == true` in every handler, and note UI refresh paths (mini parse, current-graph HTML) are suppressed during imports.
- **Companion logs:** `CompanionLogFile.ThreadCompanionLog` (FormActMain.cs ~600–640) tails extra log files on their own threads and interleaves them through the same `ParseRawLogLine` (`CompanionLogName` set), gated so companion time never runs ahead of the main log (`LastEstimatedTime < CurrentLogTime` spin). `GlobalTimeSorter` is shared. Consequence: `BeforeLogLineRead`/`OnLogLineRead` are **not** guaranteed single-threaded by origin — but they *are* serialized by `lock (rawLogLineLock)` in `ParseRawLogLine` (2440/21629).
- **String interning:** `AddCombatAction` interns attacker/victim/attackType/damageType/special. Combatant identity is name-based, case-insensitively (`ToUpper()` keys; `CombatantData.Equals` compares lowercased names). ACT's rename/redirect corrections happen *before* your `AfterCombatAction` sees the swing.
- **`ActGlobals` statics you'll touch:** `oFormActMain`, `oFormMiniParse`, `charName` (default `"YOU"` — EQ2 logs are you-relative; ally detection is seeded from this), `actionDataLock` (internal; use the public `AfterCombatActionDataLock` property), `mainTableShowCommas`, `blockIsHit` (true), `restrictToAll` (false; true disables per-ability AttackType buckets → massive memory saver, but kills breakdowns), `disableIncrementalCaching` (false; true = full recompute per swing), `longDuration` (false), `wallClockDuration` (true), sort keys `eDSort`/`mDSort`/`aTSort` (+`2` variants), `Trans` (localization indexer — **all structural dictionary keys like "All", "Killing", "Outgoing Damage" are localization-dependent**; always go through `ActGlobals.Trans["attackTypeTerm-all"]` and the `CombatantData.DamageTypeData*` alias statics, never hardcode).
- **Performance pitfalls for a per-frame meter:**
  - `EncounterData.Damage/Healed/AlliedDeaths/DPS` each call `GetAllies()` (cached) but then loop allies summing `CombatantData` properties — fine at 1–10 Hz, measurable at 60 Hz on 24-person raids. Prefer reading per-combatant `Damage`/`EncDPS` directly and caching the ally list yourself (subscribe to changes via swing counts).
  - Never read `EncId`/`GetHashCode`/`Equals` on live encounters (O(all swings) hashing, see §1).
  - `CombatantData.Duration`/`AttackType.Duration` become expensive when silence-cutting has produced multiple `StartTimes` (full swing sort per invalidation). `EncDPS` avoids per-combatant duration (divides by encounter duration) — it's the cheap one, which is why ACT defaults to it.
  - `GetAllies(allowLimited: true)` gives a ≤1 s-stale ally list without the graph walk — what `Kills` uses internally.
  - The wall clock vs log clock split: for smooth meter animation, compute your own display DPS as `damage / (LastEstimatedTime - encounter.StartTime)` mirroring the `{duration}` logic (FormActMain.cs:3321–3353), not from `Duration`.
- **The safest minimal meter architecture given all the above:** subscribe `AfterCombatAction` (you're on the after-action thread, under the data lock, model already updated) → fold the `MasterSwing` into your own thread-safe accumulator keyed by attacker → drive WPF rendering from your own clock. Use `OnCombatStart`/`OnCombatEnd` for reset/finalize (totals guaranteed complete at `OnCombatEnd` thanks to the drain-wait). Only fall back to polling `ActiveZone.ActiveEncounter` under `AfterCombatActionDataLock` if you need ACT-computed values (ally classification, `GetEncounterSuccessLevel`, existing export variables) rather than raw swings — or the best hybrid: poll cheap per-combatant properties (`Damage`, `Healed`, `DamageTaken`, `Deaths`, `EncDPS`) at 1–4 Hz under the lock and interpolate visually, which is exactly the mini parse's data path minus its cadence and rendering limitations.
