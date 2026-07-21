# Basic Meter Metrics + Scope Axis Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Grow the meter's metric registry from 3 to 7 scope-free metrics (adding the `damagetaken`, `totalhealing`, `healstaken`, `powerheal` totals) and add an independent Allies/Enemies **scope** axis, surfaced through a predefined-selection primary picker while the secondary stays a scope-free metric inheriting the primary's scope.

**Architecture:** Metrics stay pure selectors (`MetricDef`, unchanged shape). A new `MeterScope` enum and a `MeterSelections` list of predefined `(label, scope, metricKey)` selections model the picker vocabulary. `MeterEngine.Tick` gains a scope argument that drives the row population (Allies = today's `ShowOnlyAllies` filter; Enemies = its inverse) and resolves the header identity from the selection label. `MeterWindowConfig` persists the primary scope; the plugin popup, window, and host thread it through. Core is strict TDD; the plugin is transcribe-only (CI-compile-gated, never Mac-runtime-verified).

**Tech Stack:** C# — `eq2auras.Core` (netstandard2.0, xUnit tests, `dotnet test`), `eq2auras.Plugin` (net472/WPF, compiled in CI only). JSON persistence is `DataContractJsonSerializer` (DCJS).

## Global Constraints

- **Core is Mac-testable and TDD; the Plugin is NOT built or run on the Mac** — plugin correctness is CI-compile + Alex's on-box field test only. Never run anything named `*Harness*`.
- **DCJS skips field initializers on deserialize** → an enum knob's default must be its **0-value**. `MeterScope.Allies = 0`.
- **No `async` in the Plugin project; no `System.Web.Extensions`; no `Assembly.LoadFrom`; no second DLL.** New Core files are compiled into the plugin via the existing recursive glob `..\eq2auras.Core\**\*.cs` (`src/eq2auras.Plugin/eq2auras.Plugin.csproj:32`) and into Core via the SDK default glob — **no csproj edit needed**.
- **Timers are untouched.** No timer file changes; the shared `BarRowVisual`/substrate is not modified.
- **Forward-compat lives at read sites, not `Normalize`** (matching `MetricRegistry.ResolvePrimary`): an unknown scope value degrades to Allies at the engine, not by clamping the persisted value.
- **Code style:** self-documenting, K&R braces, Lombok-N/A (C#), early returns, no underscore-prefixed locals, spaces around operators. Match the surrounding meter files.

---

### Task 1: `MeterScope` enum, four new total metrics, three new `CombatantReading` fields

**Files:**
- Create: `src/eq2auras.Core/Meter/MeterScope.cs`
- Modify: `src/eq2auras.Core/Meter/MeterReading.cs:7-14` (add three fields to `CombatantReading`)
- Modify: `src/eq2auras.Core/Meter/MetricRegistry.cs:12-17` (append four `MetricDef`s)
- Test: `tests/eq2auras.Core.Tests/MetricRegistryTests.cs`

**Interfaces:**
- Produces: `enum MeterScope { Allies = 0, Enemies = 1 }` (namespace `Eq2Auras.Core.Meter`); `CombatantReading.DamageTaken` (`long`), `CombatantReading.HealsTaken` (`long`), `CombatantReading.PowerReplenish` (`long`); registry keys `"damagetaken"`, `"totalhealing"`, `"healstaken"`, `"powerheal"`, each `IsRate == false` with `NumberFormat.Abbreviate`.

- [ ] **Step 1: Write the failing test** — append to `MetricRegistryTests.cs`:

```csharp
[Theory]
[InlineData("damagetaken", "Damage Taken", "Damage")]
[InlineData("totalhealing", "Total Healing", "Healing")]
[InlineData("healstaken", "Healing Taken", "Healing")]
[InlineData("powerheal", "Power Replenish", "Utility")]
public void New_total_metrics_are_registered_as_abbreviated_non_rates(string key, string label, string category)
{
    var metric = MetricRegistry.Resolve(key);

    Assert.Equal(key, metric.Key);
    Assert.Equal(label, metric.Label);
    Assert.Equal(category, metric.Category);
    Assert.False(metric.IsRate);                 // a total, not a rate — never divided by duration
    Assert.Equal("1.5M", metric.Format(1_500_000));   // K/M/B abbreviation, not a plain integer
}

[Theory]
[InlineData("damagetaken", 4200L, 0L, 0L, 4200)]
[InlineData("healstaken", 0L, 900L, 0L, 900)]
[InlineData("powerheal", 0L, 0L, 700L, 700)]
public void New_metric_selectors_read_their_combatant_field(string key, long dmgTaken, long healsTaken, long powerReplenish, double expected)
{
    var reading = new CombatantReading
    {
        DamageTaken = dmgTaken,
        HealsTaken = healsTaken,
        PowerReplenish = powerReplenish,
    };

    Assert.Equal(expected, MetricRegistry.Resolve(key).Select(reading));
}

[Fact]
public void Total_healing_and_hps_share_the_healed_selector()
{
    var reading = new CombatantReading { Healed = 12_000 };

    Assert.Equal(12_000, MetricRegistry.Resolve("totalhealing").Select(reading));
    Assert.Equal(12_000, MetricRegistry.Resolve("enchps").Select(reading));
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter "FullyQualifiedName~MetricRegistryTests"`
Expected: FAIL — `CombatantReading` has no `DamageTaken`/`HealsTaken`/`PowerReplenish`; keys unresolved (fall back to DPS, wrong assertions).

- [ ] **Step 3: Create `MeterScope.cs`**

```csharp
namespace Eq2Auras.Core.Meter
{
    /// Which combatants a meter window draws its rows from — an axis independent of the
    /// metric (SPEC Part III §The metric registry — Scope). Allies is the 0-value so a
    /// scope-less (missing) config field deserializes to it under DCJS.
    public enum MeterScope
    {
        Allies = 0,
        Enemies = 1,
    }
}
```

- [ ] **Step 4: Add the three fields to `CombatantReading`** (`MeterReading.cs`, after `CureDispels` at line 12):

```csharp
        public long DamageTaken { get; set; }
        public long HealsTaken { get; set; }
        public long PowerReplenish { get; set; }   // power restored to others (ACT swing type 13)
```

- [ ] **Step 5: Append the four metrics to `MetricRegistry.All`** (`MetricRegistry.cs`, after the `cures` line):

```csharp
            new MetricDef("damagetaken", "Damage Taken", "Damage", isRate: false, r => r.DamageTaken, NumberFormat.Abbreviate),
            new MetricDef("totalhealing", "Total Healing", "Healing", isRate: false, r => r.Healed, NumberFormat.Abbreviate),
            new MetricDef("healstaken", "Healing Taken", "Healing", isRate: false, r => r.HealsTaken, NumberFormat.Abbreviate),
            new MetricDef("powerheal", "Power Replenish", "Utility", isRate: false, r => r.PowerReplenish, NumberFormat.Abbreviate),
```

- [ ] **Step 6: Run to verify it passes**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter "FullyQualifiedName~MetricRegistryTests"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/eq2auras.Core/Meter/MeterScope.cs src/eq2auras.Core/Meter/MeterReading.cs src/eq2auras.Core/Meter/MetricRegistry.cs tests/eq2auras.Core.Tests/MetricRegistryTests.cs
git commit -m "Core: add four total metrics + MeterScope enum + three combatant fields"
```

---

### Task 2: `MeterSelections` — the predefined primary-selection vocabulary

**Files:**
- Create: `src/eq2auras.Core/Meter/MeterSelections.cs`
- Test: `tests/eq2auras.Core.Tests/MeterSelectionsTests.cs`

**Interfaces:**
- Consumes: `MeterScope` (Task 1), `MetricRegistry.All` / `MetricRegistry.Resolve` (existing).
- Produces: `sealed class PrimarySelection { string Label; MeterScope Scope; string MetricKey; }` with ctor `(string label, MeterScope scope, string metricKey)`; `static class MeterSelections { IReadOnlyList<PrimarySelection> Primary; PrimarySelection Resolve(MeterScope scope, string metricKey); }`. `Resolve` returns `null` for no match.

- [ ] **Step 1: Write the failing test** — create `MeterSelectionsTests.cs`:

```csharp
using System.Linq;
using Eq2Auras.Core.Meter;
using Xunit;

public class MeterSelectionsTests
{
    [Fact]
    public void The_nine_selections_cover_every_metric_and_add_two_enemy_twins()
    {
        Assert.Equal(9, MeterSelections.Primary.Count);

        // Every registry metric appears at least once as an allies selection.
        foreach (var metric in MetricRegistry.All)
            Assert.Contains(MeterSelections.Primary, s => s.Scope == MeterScope.Allies && s.MetricKey == metric.Key);

        // The two enemy twins reuse existing metric keys.
        Assert.Contains(MeterSelections.Primary, s => s.Scope == MeterScope.Enemies && s.MetricKey == "damagetaken" && s.Label == "Enemy Damage Taken");
        Assert.Contains(MeterSelections.Primary, s => s.Scope == MeterScope.Enemies && s.MetricKey == "totalhealing" && s.Label == "Enemy Healing Done");
    }

    [Theory]
    [InlineData(MeterScope.Allies, "damagetaken", "Damage Taken")]
    [InlineData(MeterScope.Enemies, "damagetaken", "Enemy Damage Taken")]
    [InlineData(MeterScope.Allies, "encdps", "DPS")]
    public void Resolve_returns_the_selection_matching_scope_and_metric(MeterScope scope, string key, string expectedLabel)
    {
        Assert.Equal(expectedLabel, MeterSelections.Resolve(scope, key).Label);
    }

    [Fact]
    public void Resolve_returns_null_when_no_selection_matches()
    {
        Assert.Null(MeterSelections.Resolve(MeterScope.Enemies, "cures"));   // no enemy-cures selection defined
        Assert.Null(MeterSelections.Resolve(MeterScope.Allies, "nonsense"));
    }

    [Fact]
    public void Every_selection_metric_resolves_to_a_real_registry_metric()
    {
        foreach (var selection in MeterSelections.Primary)
            Assert.Contains(MetricRegistry.All, m => m.Key == selection.MetricKey);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter "FullyQualifiedName~MeterSelectionsTests"`
Expected: FAIL — `MeterSelections`/`PrimarySelection` do not exist.

- [ ] **Step 3: Create `MeterSelections.cs`**

```csharp
using System.Collections.Generic;
using System.Linq;

namespace Eq2Auras.Core.Meter
{
    /// One entry of the primary picker's vocabulary (SPEC Part III §The metric registry —
    /// Predefined primary selections): a curated pairing of a scope and a metric under one
    /// label, chosen together in a single click. The secondary picker uses scope-free
    /// MetricDefs directly, so there is no secondary-selection type.
    public sealed class PrimarySelection
    {
        public string Label { get; }
        public MeterScope Scope { get; }
        public string MetricKey { get; }

        public PrimarySelection(string label, MeterScope scope, string metricKey)
        {
            Label = label;
            Scope = scope;
            MetricKey = metricKey;
        }
    }

    public static class MeterSelections
    {
        public static readonly IReadOnlyList<PrimarySelection> Primary = new List<PrimarySelection>
        {
            new PrimarySelection("DPS", MeterScope.Allies, "encdps"),
            new PrimarySelection("Damage Taken", MeterScope.Allies, "damagetaken"),
            new PrimarySelection("Enemy Damage Taken", MeterScope.Enemies, "damagetaken"),
            new PrimarySelection("HPS", MeterScope.Allies, "enchps"),
            new PrimarySelection("Total Healing", MeterScope.Allies, "totalhealing"),
            new PrimarySelection("Enemy Healing Done", MeterScope.Enemies, "totalhealing"),
            new PrimarySelection("Healing Taken", MeterScope.Allies, "healstaken"),
            new PrimarySelection("Cures", MeterScope.Allies, "cures"),
            new PrimarySelection("Power Replenish", MeterScope.Allies, "powerheal"),
        };

        /// The selection a window's (scope, metric) state names — its label is the header
        /// identity (SPEC §Header). Null when no selection matches (forward-compat: the
        /// engine falls back to the bare metric label).
        public static PrimarySelection Resolve(MeterScope scope, string metricKey)
            => Primary.FirstOrDefault(s => s.Scope == scope && s.MetricKey == metricKey);
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter "FullyQualifiedName~MeterSelectionsTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/eq2auras.Core/Meter/MeterSelections.cs tests/eq2auras.Core.Tests/MeterSelectionsTests.cs
git commit -m "Core: add MeterSelections — nine predefined primary selections"
```

---

### Task 3: `MeterEngine.Tick` — scope-driven population + selection-label identity

**Files:**
- Modify: `src/eq2auras.Core/Meter/MeterEngine.cs:13-97`
- Test: `tests/eq2auras.Core.Tests/MeterEngineTests.cs`

**Interfaces:**
- Consumes: `MeterScope` (Task 1), `MeterSelections.Resolve` (Task 2).
- Produces: `MeterFrame Tick(EncounterReading encounter, List<CombatantReading> combatants, string metricKey, string secondaryKey = null, MeterScope scope = MeterScope.Allies)`. Population: Allies = `ShowOnlyAllies` filter (unchanged); Enemies = show only `!IsAlly` (Unknown dropped). `frame.MetricLabel` = the matching selection's label, or the metric's label when no selection matches.

- [ ] **Step 1: Write the failing tests** — append to `MeterEngineTests.cs`. First extend the helper to set the new fields (replace the existing `Ally` helper's body signature by adding optional params — keep existing call sites working):

```csharp
    private static CombatantReading Combatant(string name, bool isAlly, long damageTaken = 0, long healed = 0, long powerReplenish = 0)
        => new() { Name = name, DamageTaken = damageTaken, Healed = healed, PowerReplenish = powerReplenish, IsAlly = isAlly };

    [Fact]
    public void Enemy_scope_shows_only_non_allies()
    {
        var combatants = new List<CombatantReading>
        {
            Combatant("Me", isAlly: true, damageTaken: 100),
            Combatant("Boss", isAlly: false, damageTaken: 5000),
            Combatant("Add", isAlly: false, damageTaken: 3000),
        };

        var frame = new MeterEngine().Tick(Live(100), combatants, "damagetaken", null, MeterScope.Enemies);

        Assert.Equal(new[] { "Boss", "Add" }, frame.Rows.Select(r => r.Name));   // allies excluded, sorted desc
        Assert.Equal(8000, frame.Rows.Sum(r => r.Value));
    }

    [Fact]
    public void Allies_scope_default_still_shows_only_allies()
    {
        var combatants = new List<CombatantReading>
        {
            Combatant("Me", isAlly: true, damageTaken: 100),
            Combatant("Boss", isAlly: false, damageTaken: 5000),
        };

        var frame = new MeterEngine().Tick(Live(100), combatants, "damagetaken");   // scope defaults to Allies

        Assert.Equal(new[] { "Me" }, frame.Rows.Select(r => r.Name));
    }

    [Fact]
    public void Pre_engage_shows_everyone_under_either_scope()
    {
        var combatants = new List<CombatantReading>   // no allies classified yet
        {
            Combatant("Groupmate", isAlly: false, damageTaken: 10),
            Combatant("Mob", isAlly: false, damageTaken: 20),
        };

        var allies = new MeterEngine().Tick(Live(100), combatants, "damagetaken", null, MeterScope.Allies);
        var enemies = new MeterEngine().Tick(Live(100), combatants, "damagetaken", null, MeterScope.Enemies);

        Assert.Equal(2, allies.Rows.Count);   // Allies escape hatch: no allies -> show all
        Assert.Equal(2, enemies.Rows.Count);  // Enemies: all are non-allies -> show all
    }

    [Fact]
    public void Enemy_scope_with_no_enemies_yields_no_rows()
    {
        var combatants = new List<CombatantReading> { Combatant("Me", isAlly: true, damageTaken: 100) };

        var frame = new MeterEngine().Tick(Live(100), combatants, "damagetaken", null, MeterScope.Enemies);

        Assert.Empty(frame.Rows);
    }

    [Fact]
    public void Header_identity_is_the_selection_label_not_the_bare_metric()
    {
        var combatants = new List<CombatantReading> { Combatant("Boss", isAlly: false, damageTaken: 5000) };

        var frame = new MeterEngine().Tick(Live(100), combatants, "damagetaken", null, MeterScope.Enemies);

        Assert.Equal("Enemy Damage Taken", frame.MetricLabel);
    }

    [Fact]
    public void Secondary_is_computed_over_the_scoped_population()
    {
        var combatants = new List<CombatantReading>
        {
            Combatant("Boss", isAlly: false, damageTaken: 5000, healed: 800),
        };

        // primary = enemy damage taken; secondary = total healing -> reads the enemy's Healed
        var frame = new MeterEngine().Tick(Live(100), combatants, "damagetaken", "totalhealing", MeterScope.Enemies);

        Assert.Single(frame.Rows[0].Secondaries);
        Assert.Equal("800", frame.Rows[0].Secondaries[0].FormattedValue);
    }

    [Fact]
    public void Unknown_scope_value_degrades_to_allies_behavior()
    {
        var combatants = new List<CombatantReading>
        {
            Combatant("Me", isAlly: true, damageTaken: 100),
            Combatant("Boss", isAlly: false, damageTaken: 5000),
        };

        var frame = new MeterEngine().Tick(Live(100), combatants, "damagetaken", null, (MeterScope)99);

        Assert.Equal(new[] { "Me" }, frame.Rows.Select(r => r.Name));   // not Enemies -> Allies filter
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter "FullyQualifiedName~MeterEngineTests"`
Expected: FAIL — `Tick` has no `scope` parameter (compile error).

- [ ] **Step 3: Add the `scope` parameter and drive the population from it.** Change the `Tick` signature (`MeterEngine.cs:13-14`):

```csharp
        public MeterFrame Tick(EncounterReading encounter, List<CombatantReading> combatants,
            string metricKey, string secondaryKey = null, MeterScope scope = MeterScope.Allies)
```

Replace the population filter block (`MeterEngine.cs:46-59`, the comment + `anyAlly` line + the two `foreach` skip lines) with a scope-aware version. The `if (combatant.Name == "Unknown") continue;` and `Compute`/row-building lines stay; only the skip logic changes:

```csharp
            // Scope selects the population (SPEC Part III §Displayed combatants): Allies mirrors
            // ACT's ShowOnlyAllies filter (hide non-allies only when the ally set is non-empty —
            // ACT's escape hatch, so pre-engage shows everyone); Enemies is the exact inverse
            // (show only non-allies). "Unknown" is always dropped. An unrecognized scope value
            // degrades to Allies (forward-compat, read-site — no persisted clamp).
            var all = combatants ?? new List<CombatantReading>();
            bool enemyScope = scope == MeterScope.Enemies;
            bool anyAlly = all.Any(c => c.IsAlly);

            var rows = new List<MeterRow>();
            double total = 0;
            foreach (var combatant in all)
            {
                if (combatant.Name == "Unknown") continue;
                if (enemyScope)
                {
                    if (combatant.IsAlly) continue;
                }
                else if (anyAlly && !combatant.IsAlly)
                {
                    continue;
                }
```

(The existing `double value = Compute(...)`, `total += value`, and `rows.Add(...)` lines follow unchanged, closing the `foreach`.)

- [ ] **Step 4: Set the header identity from the selection label.** In the primary block after `var metric = MetricRegistry.ResolvePrimary(metricKey);` and its cleared-primary guard, resolve the label once (add near the top of the non-cleared path, before `MetricLabel` is used):

```csharp
            var selection = MeterSelections.Resolve(scope, metricKey);
            string metricLabel = selection?.Label ?? metric.Label;
```

Then change the frame's `MetricLabel = metric.Label` (the `return new MeterFrame { ... }` at `MeterEngine.cs:93`) to `MetricLabel = metricLabel`.

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter "FullyQualifiedName~MeterEngineTests"`
Expected: PASS — new scope/label tests pass; all pre-existing MeterEngine tests still pass (they use the default Allies scope, and for allies selections the selection label equals the metric label, e.g. `Resolve(Allies,"encdps").Label == "DPS"`).

- [ ] **Step 6: Commit**

```bash
git add src/eq2auras.Core/Meter/MeterEngine.cs tests/eq2auras.Core.Tests/MeterEngineTests.cs
git commit -m "Core: MeterEngine.Tick gains scope — drives population + selection-label identity"
```

---

### Task 4: `MeterWindowConfig.Scope` — persistence + default

**Files:**
- Modify: `src/eq2auras.Core/Config/MeterWindowConfig.cs:14-18` (add the `Scope` member)
- Test: `tests/eq2auras.Core.Tests/MeterSettingsTests.cs`

**Interfaces:**
- Consumes: `MeterScope` (Task 1).
- Produces: `MeterWindowConfig.Scope` (`MeterScope`, `[DataMember(Name = "scope")]`, default `MeterScope.Allies`).

- [ ] **Step 1: Write the failing test** — append to `MeterSettingsTests.cs`. This file drives config through `Settings.Parse(jsonString)` / `Settings.ToJson()` (the suite's convention — no round-trip helper exists), and DCJS serializes enums numerically (`"scope":1`), exactly as `SettingsTests.cs:46` does for `"colorSource":1`. Add `using Eq2Auras.Core.Meter;` to the file's usings (it currently imports only `Eq2Auras.Core.Config`; `MeterScope` lives in `Eq2Auras.Core.Meter`).

```csharp
[Fact]
public void A_window_with_no_scope_key_defaults_to_allies()
{
    // DCJS skips the field initializer on deserialize, so a window carrying no "scope"
    // arrives at the enum's 0-value = Allies (the legacy-config case).
    var json = "{\"meter\":{\"enabled\":true,\"windows\":[{\"metricKey\":\"encdps\"}]}}";

    var parsed = Settings.Parse(json);

    Assert.Equal(MeterScope.Allies, parsed.Meter.Windows[0].Scope);
}

[Fact]
public void Enemies_scope_parses_and_reserializes_numerically()
{
    var json = "{\"meter\":{\"enabled\":true,\"windows\":[{\"metricKey\":\"damagetaken\",\"scope\":1}]}}";

    var parsed = Settings.Parse(json);
    Assert.Equal(MeterScope.Enemies, parsed.Meter.Windows[0].Scope);

    Assert.Contains("\"scope\":1", parsed.ToJson());   // DCJS house style: enum as its numeric value
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter "FullyQualifiedName~MeterSettingsTests"`
Expected: FAIL — `MeterWindowConfig` has no `Scope` member (compile error).

- [ ] **Step 3: Add the `Scope` member** (`MeterWindowConfig.cs`, after the `SecondaryKey` member at line 18):

```csharp
        [DataMember(Name = "scope")]
        public MeterScope Scope { get; set; } = MeterScope.Allies;   // the PRIMARY's scope; 0-value survives DCJS (no initializer on deserialize). Unknown values degrade to Allies at the engine read site.
```

Add `using Eq2Auras.Core.Meter;` to the file's usings if not present.

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter "FullyQualifiedName~MeterSettingsTests"`
Expected: PASS.

- [ ] **Step 5: Run the full Core suite** (regression gate before touching the plugin):

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
Expected: PASS (all green — the new tests plus every pre-existing test).

- [ ] **Step 6: Commit**

```bash
git add src/eq2auras.Core/Config/MeterWindowConfig.cs tests/eq2auras.Core.Tests/MeterSettingsTests.cs
git commit -m "Core: MeterWindowConfig persists the primary scope (default Allies)"
```

---

### Task 5: Plugin — `EncounterProbe` snapshots the three new combatant properties

**Files:**
- Modify: `src/eq2auras.Plugin/Act/EncounterProbe.cs:69-76`

**Interfaces:**
- Consumes: `CombatantReading.DamageTaken`/`HealsTaken`/`PowerReplenish` (Task 1).

> **Transcribe-only** (net472/WPF — not built on the Mac). Verification is CI compile + Alex's on-box field test.

- [ ] **Step 1: Add the three reads inside the `Items.Values` loop** — extend the `new CombatantReading { ... }` initializer (`EncounterProbe.cs:69-76`) with three lines alongside the existing `Damage`/`Healed`/`CureDispels` reads:

```csharp
                            combatants.Add(new CombatantReading
                            {
                                Name = combatant.Name,
                                Damage = combatant.Damage,
                                Healed = combatant.Healed,
                                CureDispels = combatant.CureDispels,
                                DamageTaken = combatant.DamageTaken,
                                HealsTaken = combatant.HealsTaken,
                                PowerReplenish = combatant.PowerReplenish,
                                IsAlly = allySet.Contains(combatant),
                            });
```

`combatant` is ACT's `CombatantData`; `DamageTaken`, `HealsTaken`, and `PowerReplenish` are its direct properties (`ThirdParty/ACT_English_Parser.cs:2004` reads `Data.PowerReplenish`; `docs/act-parse-engine.md:85` lists `DamageTaken`/`HealsTaken`). All three read under the existing `AfterCombatActionDataLock` already held here.

- [ ] **Step 2: Commit**

```bash
git add src/eq2auras.Plugin/Act/EncounterProbe.cs
git commit -m "Plugin: snapshot DamageTaken/HealsTaken/PowerReplenish per combatant"
```

---

### Task 6: Plugin — thread scope through the popup, window, and host

**Files:**
- Modify: `src/eq2auras.Plugin/Overlay/MeterPopup.cs` (primary grid → selections, callback carries scope)
- Modify: `src/eq2auras.Plugin/Overlay/MeterWindow.cs:48-59, 234-237` (hold `_scope`, constructor param, callback)
- Modify: `src/eq2auras.Plugin/Overlay/OverlayHost.cs:100-149, 227` (pass `config.Scope`, persist scope + metric together)

**Interfaces:**
- Consumes: `MeterScope` (Task 1), `MeterSelections.Primary`/`Resolve` (Task 2), `MetricRegistry.Resolve(...).Category` (existing), `MeterWindowConfig.Scope` (Task 4), `MeterEngine.Tick(..., scope)` (Task 3).

> **Transcribe-only** (net472/WPF — not built on the Mac). Verification is CI compile + Alex's on-box field test. All three files change together because the scope-carrying callback threads through them; they are one reviewable deliverable.

- [ ] **Step 1: `MeterPopup` — carry scope on the primary callback and build the primary grid from selections.**

Change the callback field (`MeterPopup.cs:20`) from `public Action<string> PrimaryToggled;` to:

```csharp
            public Action<MeterScope, string> PrimarySelected;    // (scope, metricKey); metricKey null clears the primary
```

Change the popup's stored primary state and constructor. Replace `_primaryKey` (`:31`) with both fields and add `_scope`:

```csharp
        private MeterScope _scope;
        private string _primaryKey;
        private string _secondaryKey;
```

Change the constructor signature (`:34`) to accept the current scope:

```csharp
        public MeterPopup(UIElement placementTarget, MeterScope scope, string primaryKey, string secondaryKey, Func<bool> canRemove, Callbacks cb)
```

and set `_scope = scope;` alongside the existing `_primaryKey`/`_secondaryKey` assignments.

Replace `BuildGrid`'s primary branch so the primary grid iterates `MeterSelections.Primary` (grouped by the metric's category) while the secondary grid keeps iterating `MetricRegistry.All`. Rewrite `BuildGrid` (`:85-105`):

```csharp
        private UIElement BuildGrid(bool isPrimary)
        {
            var grid = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(11, 2, 11, 10) };
            foreach (var family in MetricRegistry.All.Select(m => m.Category).Distinct())
            {
                var col = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
                col.Children.Add(FamilyHeader(family));
                if (isPrimary)
                {
                    foreach (var selection in MeterSelections.Primary.Where(s => MetricRegistry.Resolve(s.MetricKey).Category == family))
                    {
                        bool selected = selection.Scope == _scope && selection.MetricKey == _primaryKey;
                        var item = new MetricGridItem(selection.Label, selected);
                        _primaryItems.Add(new KeyValuePair<string, MetricGridItem>(SelectionId(selection), item));
                        var captured = selection;
                        item.Toggled += () => OnPrimaryToggle(captured);
                        col.Children.Add(item);
                    }
                }
                else
                {
                    foreach (var metric in MetricRegistry.All.Where(m => m.Category == family))
                    {
                        string key = metric.Key;
                        var item = new MetricGridItem(metric.Label, key == _secondaryKey);
                        _secondaryItems.Add(new KeyValuePair<string, MetricGridItem>(key, item));
                        item.Toggled += () => OnSecondaryToggle(key);
                        col.Children.Add(item);
                    }
                }
                grid.Children.Add(col);
            }
            return grid;
        }

        // A primary grid item's id encodes both axes so RefreshSection lights exactly the
        // (scope, metric) currently selected — an ally and an enemy entry can share a metric key.
        private static string SelectionId(PrimarySelection s) => (int)s.Scope + ":" + s.MetricKey;
```

Replace `OnToggle` (`:107-122`) with two handlers. The primary sets both axes (or clears the metric); the secondary is unchanged:

```csharp
        private void OnPrimaryToggle(PrimarySelection selection)
        {
            bool wasSelected = selection.Scope == _scope && selection.MetricKey == _primaryKey;
            _scope = selection.Scope;
            _primaryKey = wasSelected ? null : selection.MetricKey;   // click the lit one to clear
            foreach (var pair in _primaryItems)
                pair.Value.Selected = !wasSelected && pair.Key == SelectionId(selection);
            _cb.PrimarySelected(_scope, _primaryKey);
        }

        private void OnSecondaryToggle(string key)
        {
            _secondaryKey = _secondaryKey == key ? null : key;
            RefreshSection(_secondaryItems, _secondaryKey);
            _cb.SecondaryToggled(_secondaryKey);
        }
```

Add `using Eq2Auras.Core.Meter;` (already present — the file uses `MetricRegistry`). Keep `RefreshSection` (used by the secondary). Remove the now-unused `_primaryKey`-only `RefreshSection` call path for the primary (handled inline above).

- [ ] **Step 2: `MeterWindow` — hold `_scope`, take it in the constructor, pass it to the popup, persist it with the metric.**

Add the field (`MeterWindow.cs:48`, beside `_metricKey`):

```csharp
        private MeterScope _scope;
```

Add a constructor parameter (`:52`) — insert `MeterScope scope` right before `string metricKey`:

```csharp
        public MeterWindow(double left, double top, VisualStyle style, MeterScope scope, string metricKey, string secondaryKey, bool locked, double opacity, double backdropOpacity, int visibleRows,
            MeterWindowCallbacks callbacks)
```

and set `_scope = scope;` beside `_metricKey = metricKey;` (`:58`).

Update the popup construction and its primary callback (`:234-237`):

```csharp
            var popup = new MeterPopup(target, _scope, _metricKey, _secondaryKey, _cb.CanClose, new MeterPopup.Callbacks
            {
                PrimarySelected = (scope, key) => { _scope = scope; _metricKey = key; _cb.PrimaryPicked(scope, key); },
                SecondaryToggled = SetSecondary,
```

(The remaining callback wiring — Lock/NewMeter/RemoveMeter — is unchanged.)

Change the callback type: in `MeterWindowCallbacks` (find `public Action<string> MetricPicked;`) rename to:

```csharp
        public Action<MeterScope, string> PrimaryPicked;   // (scope, metricKey) persisted together
```

Add `using Eq2Auras.Core.Meter;` if not already present (the file references `MetricRegistry`/`MeterFrame`, so it is).

- [ ] **Step 3: `OverlayHost` — pass `config.Scope` to the window, the engine, and persist scope+metric together.**

In `AddMeterWindow` (`OverlayHost.cs:103-109`), pass `config.Scope` into the constructor (insert before `config.MetricKey`):

```csharp
            var window = new MeterWindow(
                config.Left ?? DefaultMeterLeft(style),
                config.Top ?? DefaultMeterTop,
                style,
                config.Scope,
                config.MetricKey,
                config.SecondaryKey,
```

Replace the `MetricPicked` callback (`:116`) with the scope-carrying one:

```csharp
                    PrimaryPicked = (scope, key) => SettingsStore.Update(_settings, () => { config.Scope = scope; config.MetricKey = key; }),
```

Pass scope to the engine (`:227`):

```csharp
                    var frame = _meterEngine.Tick(encounter, combatants, pair.Key.MetricKey, pair.Key.SecondaryKey, pair.Key.Scope);
```

`AddNewWindow` (`:139-149`) needs no change — a new `MeterWindowConfig`'s `Scope` defaults to `MeterScope.Allies`, so New meter opens at DPS/Allies (matching the seeded `MetricKey = MetricRegistry.DefaultKey`).

- [ ] **Step 4: Push the branch and verify the plugin compiles in CI** (the only build gate available off-Mac):

```bash
git add src/eq2auras.Plugin/Overlay/MeterPopup.cs src/eq2auras.Plugin/Overlay/MeterWindow.cs src/eq2auras.Plugin/Overlay/OverlayHost.cs
git commit -m "Plugin: thread scope through popup selections, window, and host"
git push -u origin meter-basic-metrics
gh run watch $(gh run list --branch meter-basic-metrics --limit 1 --json databaseId --jq '.[0].databaseId') --exit-status
```

Expected: verify-only CI green (Core tests pass + WPF plugin compiles + artifact staged). A red compile means a transcription slip — fix and re-push.

---

## Testing strategy

- **Core (Mac TDD, the fast loop):** Tasks 1–4 above — registry resolution/format for the four totals; selector-reads-field; `MeterSelections` coverage/resolution/label; `MeterEngine` population under each scope (Allies filter, Enemies inverse, pre-engage all-show under both, empty enemy set → no rows, unknown scope → Allies); selection-label identity; secondary over the scoped population; config `Scope` default + DCJS round-trip. Full Core suite green is the Task-4 regression gate.
- **Plugin (CI compile only, off-Mac):** Task 6 Step 4 — verify-only branch CI proves the WPF project compiles with the new signatures. No runtime verification is possible on the Mac.
- **Live verification on the box (merge-gate script — Alex, on `dev-latest` after merge):**
  1. Right-click a meter → the **primary** grid shows the new entries in the right family columns: Damage column has `DPS · Damage Taken · Enemy Damage Taken`; Healing has `HPS · Total Healing · Enemy Healing Done · Healing Taken`; Utility has `Cures · Power Replenish`. The **secondary** grid shows the seven metrics with **no** "Enemy…" entries.
  2. Pick **Damage Taken** → rows are your group with their damage taken, values abbreviated (K/M/B); header reads "Damage Taken".
  3. Pick **Enemy Damage Taken** → rows swap to the **mobs** with the damage each took; header reads "Enemy Damage Taken".
  4. Pick **Total Healing** / **Healing Taken** / **Power Replenish** → each shows live allies data, abbreviated.
  5. Pick **Enemy Healing Done** → rows are mobs with their healing done (often empty if mobs don't heal — an empty backdropped meter is correct).
  6. With **Enemy Damage Taken** primary, pick a secondary (e.g. Total Healing) → the secondary column reads each **mob's** healing (scope inherited).
  7. Reload ACT (or disable/enable) → the chosen scope + metric persist; a **New meter** opens at DPS/Allies.
  8. Regression: existing DPS/HPS/Cures windows, cleared-primary (click the lit primary → empty backdrop), lock, resize, opacity, and the **timer overlay** are all unaffected.

## Notes for the merge gate

- **Plugin is transcribe-only** — CI proves it compiles; visual/interaction correctness is Alex's on-box gate (script above). Present ready-for-review, never ready-to-merge.
- **No plan-watch items outstanding** — the spec review closed with none; the cleared-primary storage the spec references is already built on `main` (`MetricRegistry.ResolvePrimary`), not this slice's work.
- **Branch pushes run verify-only CI** (Task 6 Step 4); merging `main` is the release to `dev-latest`, on Alex's call only.
