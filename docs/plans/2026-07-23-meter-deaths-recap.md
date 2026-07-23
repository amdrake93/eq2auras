# Deaths & Death Recap Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. **This repo executes inline** (Alex watches) — see CLAUDE.md.

**Goal:** Add a **Deaths** metric (a chronological death *timeline*) and its **Death Recap** drill-down (a reconstructed per-second health track), the meter's first *special (event) metric*, per SPEC Part III §Deaths & the Death Recap.

**Architecture:** Deaths does **not** fit `MeterEngine.Tick` (combatant ranking) or `BreakdownEngine` (ranked contribution). Two new Core engines produce the same shared `MeterFrame`/`MeterRow` DTOs the window already renders: `DeathsEngine.BuildList` (one row per death event, value = time-of-death, bar = into-fight fraction) and `DeathRecapEngine.Build` (one row per 1-second bucket, fill = reconstructed HP%). The Plugin's `EncounterProbe` produces death records poll-only (a `CombatantData.Deaths` count-delta triggers a bounded killing-blow scan into a cached store) and, while a death is drilled, deep-reads that one victim's incoming swings over the last 10 s. The shared `MeterRowVisual` is generalized (a muted detail suffix; the full `Secondaries` list as colored columns) so both surfaces reuse it; `BarRowVisual` (timer-shared) is untouched.

**Tech Stack:** C# — `eq2auras.Core` (netstandard2.0, xUnit 2.9.3, Mac-testable) + `eq2auras.Plugin` (net472/WPF, Windows-only, CI-compile-verified, transcribe-only).

## Global Constraints

- **No `async` in the Plugin project; no non-GAC types in fields** (ACT's pre-`InitPlugin` type scan). Core stays netstandard2.0, no ACT/WPF types.
- **Core is TDD** (`dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`); **Plugin is transcribe-only** — its correctness is the on-box merge-gate live script, not a Mac build (never build the Plugin/solution on the Mac).
- **All ACT reads happen briefly under `ActGlobals.oFormActMain.AfterCombatActionDataLock`**, snapshot into Core DTOs, release. Never hold `EncounterData`/`CombatantData` across polls. Never read `EncId`/`GetHashCode` on a live encounter.
- **`BarRowVisual` must stay byte-behaviour-identical for timers** — all row changes live in `MeterRowVisual` (meter-only) or Core.
- **Deaths is Allies-only, Damage category (red family color).** Time is whole-second resolution (EQ2 logs at 1 s; `TimeSorter` orders within a second, no sub-second time).
- **Stage explicit paths in git** — never `git add -A`/`.` (spike-data/ hazard).

## File Structure

**Core (new):**
- `src/eq2auras.Core/Meter/DeathRecord.cs` — the per-death DTO the Plugin produces (no ACT types).
- `src/eq2auras.Core/Meter/DeathsEngine.cs` — death records → `MeterFrame` (event rows).
- `src/eq2auras.Core/Meter/DeathRecap.cs` — `RecapEvent` + `RecapReading` DTOs.
- `src/eq2auras.Core/Meter/DeathRecapEngine.cs` — recap reading → per-second `MeterRow`s (reconstruction).

**Core (modified):**
- `MeterFrame.cs` — `MeterRow.Detail`, `MeterRow.DrillKey`, `SecondaryValue.Argb`.
- `NumberFormat.cs` — `Mmss`, `SignedAbbreviate`.
- `MetricDef.cs` — `IsEvent` flag.
- `MetricRegistry.cs` — the `deaths` def.
- `MetricBreakdownSource.cs` — `Deaths` marker.
- `MeterSelections.cs` — the "Deaths" selection.

**Core (tests, new):** `DeathsEngineTests.cs`, `DeathRecapEngineTests.cs`, `NumberFormatTests.cs` (extend if present), `MetricRegistryTests.cs` (extend).

**Build packaging:** the new Core `.cs` files need **no csproj change** — the Plugin `<Compile Include>`s Core via a glob (`..\eq2auras.Core\**\*.cs`, `src/eq2auras.Plugin/*.csproj:32-33`) and the Core project is SDK-style auto-glob. New files compile into both by construction.

**Plugin (modified, transcribe-only):**
- `Act/EncounterProbe.cs` — death-record production + recap deep-read + extended callback.
- `Overlay/OverlayHost.cs` — deaths-window routing + death-drill routing + auto-exit.
- `Overlay/MeterWindow.cs` — death-row drill (DrillKey), recap header, middot delimiter.
- `Overlay/MeterRowVisual.cs` — render `Detail` + full `Secondaries` list with colors.
- `Meter/Breakdown.cs` — `DrillRequest.DeathKey`.
- `Eq2AurasPlugin.cs` — extended callback wiring.

---

# Phase 1 — The Deaths list (independently field-testable)

### Task 1: Core — number formatters (`Mmss`, `SignedAbbreviate`)

**Files:**
- Modify: `src/eq2auras.Core/Meter/NumberFormat.cs`
- Modify: `src/eq2auras.Core/Meter/MeterEngine.cs` (consolidate `FormatDuration` onto `Mmss`)
- Test: `tests/eq2auras.Core.Tests/NumberFormatTests.cs` (create if absent)

**Interfaces:**
- Produces: `NumberFormat.Mmss(double seconds) → string` ("M:SS"); `NumberFormat.SignedAbbreviate(double value) → string` (`"-4.2K"`, `"+1.0K"`, `"0"`).

- [ ] **Step 1: Write the failing tests**

```csharp
using Eq2Auras.Core.Meter;
using Xunit;

public class NumberFormatTests
{
    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(42, "0:42")]
    [InlineData(80, "1:20")]
    [InlineData(113, "1:53")]
    [InlineData(3599, "59:59")]
    public void Mmss_formats_seconds_as_minutes_and_padded_seconds(double s, string expected)
        => Assert.Equal(expected, NumberFormat.Mmss(s));

    [Theory]
    [InlineData(0, "0")]
    [InlineData(-500, "-500")]
    [InlineData(1000, "+1K")]
    [InlineData(-4200, "-4.2K")]
    [InlineData(-9800, "-9.8K")]
    public void SignedAbbreviate_prefixes_sign_and_abbreviates_magnitude(double v, string expected)
        => Assert.Equal(expected, NumberFormat.SignedAbbreviate(v));
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter NumberFormatTests`
Expected: FAIL — `Mmss`/`SignedAbbreviate` not defined.

- [ ] **Step 3: Implement**

Add to `NumberFormat` (after `Integer`):

```csharp
public static string Mmss(double seconds)
{
    int t = (int)System.Math.Max(0, seconds);
    return (t / 60) + ":" + (t % 60).ToString("00", CultureInfo.InvariantCulture);
}

public static string SignedAbbreviate(double value)
{
    if (System.Math.Round(value) == 0) return "0";
    string sign = value > 0 ? "+" : "-";
    return sign + Abbreviate(System.Math.Abs(value));
}
```

Then consolidate the M:SS formatter (addresses the two-formatter nit; CLAUDE.md single-source-of-truth) — redirect `MeterEngine.FormatDuration` (`MeterEngine.cs:123-127`) so `Mmss` is the only M:SS implementation:

```csharp
internal static string FormatDuration(double seconds) => NumberFormat.Mmss(seconds);
```

Existing `MeterEngineTests` (duration `"1:40"` for 100 s) still pass — `Mmss(100)` = `"1:40"`.

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter NumberFormatTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/eq2auras.Core/Meter/NumberFormat.cs src/eq2auras.Core/Meter/MeterEngine.cs tests/eq2auras.Core.Tests/NumberFormatTests.cs
git commit -m "Deaths: add Mmss + SignedAbbreviate formatters; consolidate FormatDuration onto Mmss (Core TDD)"
```

---

### Task 2: Core — `DeathRecord` + shared-row DTO extensions + `DeathsEngine.BuildList`

**Files:**
- Create: `src/eq2auras.Core/Meter/DeathRecord.cs`
- Create: `src/eq2auras.Core/Meter/DeathsEngine.cs`
- Modify: `src/eq2auras.Core/Meter/MeterFrame.cs` (add `MeterRow.Detail`, `MeterRow.DrillKey`, `SecondaryValue.Argb`)
- Test: `tests/eq2auras.Core.Tests/DeathsEngineTests.cs`

**Interfaces:**
- Consumes: `MeterFrame`, `MeterRow`, `MeterFamilyColors.ArgbFor`, `NumberFormat.Mmss/Abbreviate`.
- Produces:
  - `DeathRecord { string Victim; int Ordinal; double TimeOfDeathSeconds; string KillingBlowAbility; double KillingBlowDamage; string DrillKey; }`
  - `DeathsEngine.BuildList(IReadOnlyList<DeathRecord> deaths, double durationSeconds) → MeterFrame`
  - `MeterRow.Detail` (string, muted suffix after Name; null on normal rows), `MeterRow.DrillKey` (string; null → drill by Name), `SecondaryValue.Argb` (int?; null → subordinate grey).

- [ ] **Step 1: Add the DTO fields (no behaviour yet)**

In `MeterFrame.cs`, add to `MeterRow`:

```csharp
public string Detail { get; set; }      // muted suffix after Name (Deaths: "(N) · killing blow + dmg"); null on normal rows
public string DrillKey { get; set; }    // per-row drill identity; null → drill by Name (SPEC §Deaths — two rows can share a victim name)
```

In `MeterFrame.cs`, add to `SecondaryValue`:

```csharp
public int? Argb { get; set; }          // optional column color (recap dmg=red/heals=green); null → subordinate grey
```

- [ ] **Step 2: Write the failing tests**

```csharp
using System.Collections.Generic;
using Eq2Auras.Core.Meter;
using Xunit;

public class DeathsEngineTests
{
    private static DeathRecord D(string victim, int ord, double t, string ability, double dmg) =>
        new DeathRecord { Victim = victim, Ordinal = ord, TimeOfDeathSeconds = t,
            KillingBlowAbility = ability, KillingBlowDamage = dmg, DrillKey = victim + "#" + ord };

    [Fact]
    public void Rows_are_death_events_in_chronological_order_with_time_as_value()
    {
        var frame = DeathsEngine.BuildList(new List<DeathRecord>
        {
            D("Aeralik", 2, 113, "Melee", 5000),      // deliberately out of order in the input
            D("Aeralik", 1, 42, "Frostbite", 3800),
            D("Biffles", 1, 80, "Cleaving Strike", 9800),
        }, durationSeconds: 120);

        Assert.Equal(new[] { "Aeralik", "Biffles", "Aeralik" },
            frame.Rows.ConvertAll(r => r.Name).ToArray());               // sorted by time asc: 42, 80, 113
        Assert.Equal("0:42", frame.Rows[0].FormattedValue);
        Assert.Equal("1:20", frame.Rows[1].FormattedValue);
        Assert.Equal("1:53", frame.Rows[2].FormattedValue);
    }

    [Fact]
    public void Every_death_is_numbered_and_the_killing_blow_is_a_muted_detail()
    {
        var frame = DeathsEngine.BuildList(new List<DeathRecord>
        {
            D("Biffles", 1, 80, "Cleaving Strike", 9800),
        }, 120);

        Assert.Equal("Biffles", frame.Rows[0].Name);
        Assert.Equal("(1) · Cleaving Strike 9.8K", frame.Rows[0].Detail);   // ordinal always shown, dmg abbreviated
        Assert.Equal("Biffles#1", frame.Rows[0].DrillKey);
    }

    [Fact]
    public void Bar_and_percent_are_the_deaths_position_in_the_fight()
    {
        var frame = DeathsEngine.BuildList(new List<DeathRecord>
        {
            D("A", 1, 60, "X", 1), // 60/120 = 0.5
        }, 120);

        Assert.Equal(0.5, frame.Rows[0].BarFraction, 3);
        Assert.Equal(0.5, frame.Rows[0].Percent, 3);
        Assert.Equal("50%", frame.Rows[0].FormattedPercent);
    }

    [Fact]
    public void Total_is_the_death_count_and_there_is_no_secondary()
    {
        var frame = DeathsEngine.BuildList(new List<DeathRecord>
        {
            D("A", 1, 10, "X", 1), D("A", 2, 20, "Y", 1), D("B", 1, 30, "Z", 1),
        }, 120);

        Assert.Equal("3", frame.TotalText);
        Assert.Equal("Deaths", frame.MetricLabel);
        Assert.Equal("", frame.SecondaryLabel);
        Assert.All(frame.Rows, r => Assert.Empty(r.Secondaries));
    }

    [Fact]
    public void A_death_with_no_killing_blow_shows_a_dash()
    {
        var frame = DeathsEngine.BuildList(new List<DeathRecord>
        {
            new DeathRecord { Victim = "A", Ordinal = 1, TimeOfDeathSeconds = 10,
                KillingBlowAbility = null, KillingBlowDamage = 0, DrillKey = "A#1" },
        }, 120);

        Assert.Equal("(1) · —", frame.Rows[0].Detail);
    }

    [Fact]
    public void Into_fight_fraction_clamps_when_duration_is_zero_or_less_than_time()
    {
        var atZero = DeathsEngine.BuildList(new List<DeathRecord> { D("A", 1, 10, "X", 1) }, 0);
        Assert.Equal(0, atZero.Rows[0].BarFraction, 3);   // no divide-by-zero

        var past = DeathsEngine.BuildList(new List<DeathRecord> { D("A", 1, 200, "X", 1) }, 120);
        Assert.Equal(1.0, past.Rows[0].BarFraction, 3);   // clamp to 1
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter DeathsEngineTests`
Expected: FAIL — `DeathRecord`/`DeathsEngine` not defined.

- [ ] **Step 4: Implement `DeathRecord`**

`src/eq2auras.Core/Meter/DeathRecord.cs`:

```csharp
namespace Eq2Auras.Core.Meter
{
    /// One death event, produced by the Plugin's poll-only capture (SPEC §Deaths & the Death Recap).
    /// No ACT types — the Plugin resolves the killing blow and stamps these fields.
    public sealed class DeathRecord
    {
        public string Victim { get; set; }
        public int Ordinal { get; set; }                 // this victim's Nth death (1-based), always shown
        public double TimeOfDeathSeconds { get; set; }   // encounter-relative time of the Death swing
        public string KillingBlowAbility { get; set; }   // last incoming damage swing's ability; null if none found
        public double KillingBlowDamage { get; set; }    // that swing's damage
        public string DrillKey { get; set; }             // stable per-death identity (Victim + "#" + Ordinal)
    }
}
```

- [ ] **Step 5: Implement `DeathsEngine`**

`src/eq2auras.Core/Meter/DeathsEngine.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace Eq2Auras.Core.Meter
{
    /// The Deaths special metric's list path (SPEC §Deaths & the Death Recap): death records →
    /// a chronological event timeline, reusing MeterFrame/MeterRow. Not MeterEngine.Tick — rows
    /// are events (value = time-of-death), not ranked combatants.
    public static class DeathsEngine
    {
        private const string Category = "Damage";   // red family color, SPEC §Deaths

        public static MeterFrame BuildList(IReadOnlyList<DeathRecord> deaths, double durationSeconds)
        {
            var rows = new List<MeterRow>();
            int fill = MeterFamilyColors.ArgbFor(Category);

            if (deaths != null)
            {
                foreach (var d in deaths)
                {
                    double frac = durationSeconds > 0
                        ? Math.Max(0, Math.Min(1, d.TimeOfDeathSeconds / durationSeconds))
                        : 0;
                    string blow = string.IsNullOrEmpty(d.KillingBlowAbility)
                        ? "—"
                        : d.KillingBlowAbility + " " + NumberFormat.Abbreviate(d.KillingBlowDamage);
                    rows.Add(new MeterRow
                    {
                        Name = d.Victim,
                        Detail = "(" + d.Ordinal + ") · " + blow,
                        DrillKey = d.DrillKey,
                        Value = d.TimeOfDeathSeconds,
                        FormattedValue = NumberFormat.Mmss(d.TimeOfDeathSeconds),
                        Percent = frac,
                        FormattedPercent = Math.Round(frac * 100) + "%",
                        BarFraction = frac,
                        FillArgb = fill,
                        Secondaries = new List<SecondaryValue>(),
                    });
                }
            }

            rows.Sort((a, b) => a.Value != b.Value
                ? a.Value.CompareTo(b.Value)                       // chronological ASC (earliest first)
                : string.CompareOrdinal(a.DrillKey, b.DrillKey));  // stable tie-break

            return new MeterFrame
            {
                Rows = rows,
                DurationText = NumberFormat.Mmss(durationSeconds),
                MetricLabel = "Deaths",
                SecondaryLabel = "",
                TotalText = rows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            };
        }
    }
}
```

> `NumberFormat.Mmss` is the single M:SS formatter (Task 1 redirected `MeterEngine.FormatDuration` onto it), used for both the header duration here and each row's time-of-death value.

- [ ] **Step 6: Run to verify pass**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter DeathsEngineTests`
Expected: PASS (6 tests).

- [ ] **Step 7: Commit**

```bash
git add src/eq2auras.Core/Meter/DeathRecord.cs src/eq2auras.Core/Meter/DeathsEngine.cs src/eq2auras.Core/Meter/MeterFrame.cs tests/eq2auras.Core.Tests/DeathsEngineTests.cs
git commit -m "Deaths: DeathRecord DTO + DeathsEngine event-timeline path + shared-row Detail/DrillKey/Argb (Core TDD)"
```

---

### Task 3: Core — register the `deaths` special metric + selection

**Files:**
- Modify: `src/eq2auras.Core/Meter/MetricDef.cs` (add `IsEvent`)
- Modify: `src/eq2auras.Core/Meter/MetricBreakdownSource.cs` (add `Deaths`)
- Modify: `src/eq2auras.Core/Meter/MetricRegistry.cs` (add the def)
- Modify: `src/eq2auras.Core/Meter/MeterSelections.cs` (add the selection)
- Test: `tests/eq2auras.Core.Tests/MetricRegistryTests.cs` (extend or create)

**Interfaces:**
- Produces: `MetricDef.IsEvent` (bool, default false); `MetricRegistry.Resolve("deaths")` / `ResolvePrimary("deaths")` → the deaths def with `IsEvent == true`; `MeterSelections.Resolve(Allies, "deaths").Label == "Deaths"`; `MetricBreakdownSource.Deaths`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Linq;
using Eq2Auras.Core.Meter;
using Xunit;

public class DeathsMetricRegistrationTests
{
    [Fact]
    public void Deaths_is_a_registered_event_metric_in_the_damage_family()
    {
        var def = MetricRegistry.ResolvePrimary("deaths");
        Assert.NotNull(def);
        Assert.True(def.IsEvent);
        Assert.Equal("Deaths", def.Label);
        Assert.Equal("Damage", def.Category);
        Assert.False(def.IsRate);
        Assert.Equal(MetricBreakdownSource.Deaths, def.BreakdownSource);
    }

    [Fact]
    public void The_seven_original_metrics_stay_scalar()
        => Assert.All(MetricRegistry.All.Where(m => m.Key != "deaths"), m => Assert.False(m.IsEvent));

    [Fact]
    public void Deaths_is_a_predefined_allies_selection()
    {
        var sel = MeterSelections.Resolve(MeterScope.Allies, "deaths");
        Assert.NotNull(sel);
        Assert.Equal("Deaths", sel.Label);
        Assert.Equal(MeterScope.Allies, sel.Scope);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter DeathsMetricRegistrationTests`
Expected: FAIL — `IsEvent`/`Deaths` selection not present.

- [ ] **Step 3: Implement**

`MetricDef.cs` — add the property + constructor param (default false so the seven existing call sites are unaffected):

```csharp
public bool IsEvent { get; }
```
Add `bool isEvent = false` as the LAST constructor parameter and assign `IsEvent = isEvent;`. (Optional params keep the seven existing `new MetricDef(...)` calls compiling unchanged.)

`MetricBreakdownSource.cs` — add `Deaths,` after `Cures,`.

`MetricRegistry.cs` — append to `All`:

```csharp
new MetricDef("deaths", "Deaths", "Damage", isRate: false, select: null, format: null, breakdownSource: MetricBreakdownSource.Deaths, isEvent: true),
```

> `select`/`format` are null for an event metric — its rows come from `DeathsEngine`, not `def.Select`. Confirm no scalar path dereferences them for `deaths`: `MeterEngine.Tick` is never called with the deaths key (the Plugin routes deaths to `DeathsEngine`, Task 5). Guard added in Task 5's routing.

`MeterSelections.cs` — append to the primary selections list:

```csharp
new PrimarySelection("Deaths", MeterScope.Allies, "deaths"),
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter DeathsMetricRegistrationTests`
Expected: PASS. Then run the full suite to confirm the optional-param change broke nothing:
Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
Expected: PASS (all green).

- [ ] **Step 5: Commit**

```bash
git add src/eq2auras.Core/Meter/MetricDef.cs src/eq2auras.Core/Meter/MetricBreakdownSource.cs src/eq2auras.Core/Meter/MetricRegistry.cs src/eq2auras.Core/Meter/MeterSelections.cs tests/eq2auras.Core.Tests/MetricRegistryTests.cs
git commit -m "Deaths: register the deaths event metric + Allies selection + MetricDef.IsEvent (Core TDD)"
```

---

### Task 4: Plugin — `EncounterProbe` produces death records (poll-only, count-delta) — TRANSCRIBE

**Files:**
- Modify: `src/eq2auras.Plugin/Act/EncounterProbe.cs`
- Modify: `src/eq2auras.Plugin/Eq2AurasPlugin.cs` (extend the sample callback)
- Modify: `src/eq2auras.Plugin/Overlay/OverlayHost.cs` (extend `UpdateMeterSample` signature; consumed in Task 5)

**Interfaces:**
- Produces: `EncounterProbe` calls `_onSample(encounterReading, combatants, breakdowns, deaths)` where `deaths` is `List<DeathRecord>`. A per-`EncounterProbe`-instance `Dictionary<string,int> _deathsSeen` (victim → ordinals already recorded) + `List<DeathRecord> _deathStore` cleared on encounter change.

*(Transcribe-only: not Mac-compiled. Verified by CI compile on branch push + the on-box script. Cites into the decompiled `CombatantData` at `/tmp/CombatantData.cs` from ACT 3.8.5.288.)*

- [ ] **Step 1: Add the death-capture fields + reset**

In `EncounterProbe`, add fields:

```csharp
private readonly List<DeathRecord> _deathStore = new List<DeathRecord>();
private readonly Dictionary<string, int> _deathsSeen = new Dictionary<string, int>();  // victim → count already recorded
private DateTime _encounterStartKey = DateTime.MinValue;   // detect encounter change to reset the store
```

- [ ] **Step 2: Capture deaths inside the existing lock block**

Inside `OnTick()`'s `lock (form.AfterCombatActionDataLock)`, after the combatant snapshot loop and before the drill-breakdown block, add:

```csharp
// Deaths capture (SPEC §Deaths — poll-only, count-delta triggers a bounded killing-blow scan).
if (encounter.StartTime != _encounterStartKey)   // new encounter → reset the store
{
    _encounterStartKey = encounter.StartTime;
    _deathStore.Clear();
    _deathsSeen.Clear();
}
foreach (var combatant in encounter.Items.Values)
{
    if (!allySet.Contains(combatant)) continue;            // Allies-only
    int deaths = combatant.Deaths;                          // boolean-cached, cheap (verified ACT 3.8.5.288)
    _deathsSeen.TryGetValue(combatant.Name, out int seen);
    if (deaths <= seen) continue;                           // no new death for this victim

    // A new death (or more) occurred; enumerate this victim's Death swings and record the un-seen ones.
    var killingBucketName = CombatantData.DamageTypeDataIncomingKilling;   // alias static (see note)
    var deathSwings = new List<MasterSwing>();
    if (combatant.Items.TryGetValue(killingBucketName, out var killingBucket))
    {
        string allKey = ActGlobals.ActLocalization.LocalizationStrings["attackTypeTerm-all"].DisplayedText;
        foreach (var at in killingBucket.Items)
            if (at.Key != allKey)
                foreach (var sw in at.Value.Items)
                    if (sw.Damage == Dnum.Death) deathSwings.Add(sw);
    }
    deathSwings.Sort((a, b) => a.TimeSorter.CompareTo(b.TimeSorter));   // chronological

    for (int ordinal = seen + 1; ordinal <= deaths && ordinal <= deathSwings.Count; ordinal++)
    {
        var deathSwing = deathSwings[ordinal - 1];
        var (ability, dmg) = FindKillingBlow(combatant, deathSwing.TimeSorter);
        _deathStore.Add(new DeathRecord
        {
            Victim = combatant.Name,
            Ordinal = ordinal,
            TimeOfDeathSeconds = (deathSwing.Time - encounter.StartTime).TotalSeconds,
            KillingBlowAbility = ability,
            KillingBlowDamage = dmg,
            DrillKey = combatant.Name + "#" + ordinal,
        });
    }
    _deathsSeen[combatant.Name] = deaths;
}
```

> **Bucket-name note (plan-watch item 3):** `CombatantData.DamageTypeDataIncomingKilling` is the alias static for the incoming "Killing" bucket. The decompiled `Deaths` getter reads `AllInc[ActGlobals.Trans["specialAttackTerm-killing"]]` (`/tmp/CombatantData.cs:263`) — confirm at transcribe time whether the alias static exists or the code must use `combatant.Items[ActGlobals.Trans["specialAttackTerm-killing"]]` directly (the parse-engine doc's "never hardcode bucket names — use the alias statics / `ActGlobals.Trans`"). Either resolves the same bucket; the on-box script confirms deaths appear.

- [ ] **Step 3: Add the killing-blow scan helper**

```csharp
// The killing blow = the victim's last INCOMING DAMAGE swing at/before the death's TimeSorter
// (SPEC §Deaths). Returns (null, 0) if none found (unsourced/absorbed) → the row shows "—".
private static (string ability, double damage) FindKillingBlow(CombatantData victim, int deathTimeSorter)
{
    if (!victim.Items.TryGetValue(CombatantData.DamageTypeDataIncomingDamage, out var incoming))
        return (null, 0);
    string allKey = ActGlobals.ActLocalization.LocalizationStrings["attackTypeTerm-all"].DisplayedText;
    MasterSwing best = null;
    foreach (var at in incoming.Items)
    {
        if (at.Key == allKey) continue;
        foreach (var sw in at.Value.Items)
        {
            if ((long)sw.Damage <= 0) continue;                 // real damage only (skip misses/avoids/death sentinel)
            if (sw.TimeSorter > deathTimeSorter) continue;      // at/before the death
            if (best == null || sw.TimeSorter > best.TimeSorter) best = sw;
        }
    }
    return best == null ? ((string)null, 0.0) : (best.AttackType, (double)(long)best.Damage);
}
```

- [ ] **Step 4: Thread `deaths` through the callback**

Change the `_onSample` field type to `Action<EncounterReading, List<CombatantReading>, List<BreakdownReading>, List<DeathRecord>>`; pass a **copy** of the store out of the lock:

```csharp
// after the lock, before _onSample:
var deaths = new List<DeathRecord>(_deathStore);
_onSample(encounterReading, combatants, breakdowns, deaths);
```

Update `OverlayHost.UpdateMeterSample` signature to accept `List<DeathRecord> deaths` (used in Task 5), and `Eq2AurasPlugin` construction of `EncounterProbe` to the 4-arg callback:

```csharp
(encounter, combatants, breakdowns, deaths) => _overlay.UpdateMeterSample(encounter, combatants, breakdowns, deaths)
```

- [ ] **Step 5: Commit**

```bash
git add src/eq2auras.Plugin/Act/EncounterProbe.cs src/eq2auras.Plugin/Overlay/OverlayHost.cs src/eq2auras.Plugin/Eq2AurasPlugin.cs
git commit -m "Deaths: EncounterProbe poll-only death-record capture (count-delta + killing-blow scan) [transcribe]"
```

---

### Task 5: Plugin — route deaths windows to `DeathsEngine`; render the death row — TRANSCRIBE

**Files:**
- Modify: `src/eq2auras.Plugin/Overlay/OverlayHost.cs` (`UpdateMeterSample` routing)
- Modify: `src/eq2auras.Plugin/Overlay/MeterRowVisual.cs` (render `Detail`)

**Interfaces:**
- Consumes: `DeathsEngine.BuildList`, `MetricDef.IsEvent`, `MeterRow.Detail`.

- [ ] **Step 1: Route deaths windows in `UpdateMeterSample`**

At the top of the per-window loop, before the scalar `_meterEngine.Tick(...)` call, branch on the event metric:

```csharp
var metricDef = MetricRegistry.ResolvePrimary(config.MetricKey);
MeterFrame listFrame;
if (metricDef != null && metricDef.IsEvent)     // Deaths — the event path, not Tick
    listFrame = DeathsEngine.BuildList(deaths, duration);
else
    listFrame = _meterEngine.Tick(encounter, combatants, config.MetricKey, config.SecondaryKey, config.Scope);
```

Leave the existing drill handling below for Phase 2; in Phase 1 a deaths window renders `listFrame` via `window.Render(listFrame)` and row-clicks are inert for deaths (Phase 2 wires the recap). The by-ability drill for scalar metrics is unchanged.

- [ ] **Step 2: Render the muted `Detail` suffix in `MeterRowVisual`**

In `MeterRowVisual.Update(MeterRow row)`, after `_bar.NameText.Text = row.Name;`, set a detail run (a subordinate-grey `TextBlock` appended after the name — add a `_detail` `TextBlock` in the constructor, `Theme.TextLabel` foreground, collapsed by default):

```csharp
if (!string.IsNullOrEmpty(row.Detail))
{
    _detail.Text = " " + row.Detail;
    _detail.Visibility = Visibility.Visible;
}
else _detail.Visibility = Visibility.Collapsed;
```

Place `_detail` immediately right of `NameText` in the leading region (same horizontal cluster), so it trims with the name. (Constructor: `_detail = new TextBlock { Foreground = Theme.TextLabel, VerticalAlignment = VerticalAlignment.Center }; ` added to the leading panel after `_bar.NameText`.)

> `CurrentName` still carries `row.Name` for the existing (scalar) drill; Phase 2 adds `CurrentDrillKey` for deaths.

- [ ] **Step 3: Commit + push for CI compile**

```bash
git add src/eq2auras.Plugin/Overlay/OverlayHost.cs src/eq2auras.Plugin/Overlay/MeterRowVisual.cs
git commit -m "Deaths: route event-metric windows to DeathsEngine + render muted killing-blow detail [transcribe]"
git push -u origin meter-deaths-recap    # verify-only CI: Core tests + WPF compile + artifact (no publish)
```

Expected CI: green (Core tests pass, WPF compiles). **Phase 1 is now field-testable** — see the merge-gate script (list-mode steps).

---

# Phase 2 — The Death Recap drill-down

### Task 6: Core — recap reconstruction (`RecapEvent`/`RecapReading` + `DeathRecapEngine`)

**Files:**
- Create: `src/eq2auras.Core/Meter/DeathRecap.cs` (`RecapEvent`, `RecapReading`)
- Create: `src/eq2auras.Core/Meter/DeathRecapEngine.cs`
- Test: `tests/eq2auras.Core.Tests/DeathRecapEngineTests.cs`

**Interfaces:**
- Consumes: `MeterRow`, `SecondaryValue` (+`Argb`), `NumberFormat.Abbreviate/SignedAbbreviate`, `MeterFamilyColors`.
- Produces:
  - `RecapEvent { double SecondsBeforeDeath; double Amount; bool IsHeal; }`
  - `RecapReading { string DrillKey; double MaxHealthEstimate; List<RecapEvent> Events; }`
  - `DeathRecapEngine.Build(RecapReading reading) → List<MeterRow>`
  - Colors: `DeathRecapEngine.DmgArgb`, `DeathRecapEngine.HealArgb` (int constants — red `#F2A0A0`, green `#2FBF8F`).

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Collections.Generic;
using Eq2Auras.Core.Meter;
using Xunit;

public class DeathRecapEngineTests
{
    private static RecapEvent Dmg(double s, double a) => new RecapEvent { SecondsBeforeDeath = s, Amount = a, IsHeal = false };
    private static RecapEvent Heal(double s, double a) => new RecapEvent { SecondsBeforeDeath = s, Amount = a, IsHeal = true };

    [Fact]
    public void One_row_per_active_second_oldest_first_death_row_at_zero_hp()
    {
        // deaths at t=0; damage 5000 in the final second, 3000 the second before.
        var rows = DeathRecapEngine.Build(new RecapReading
        {
            MaxHealthEstimate = 8000,
            Events = new List<RecapEvent> { Dmg(0.4, 5000), Dmg(1.2, 3000) },
        });

        Assert.Equal(2, rows.Count);
        Assert.Equal("-1s", rows[0].Name);        // oldest first
        Assert.Equal("0s", rows[1].Name);
        Assert.Equal(0, rows[1].Percent, 3);       // death second → 0% hp
        // HP at end of the second before death = the net damage that then killed them in second 0 = dmg[0] = 5000.
        Assert.Equal(5000.0 / 8000, rows[0].Percent, 3);   // 0.625
    }

    [Fact]
    public void Health_reconstructs_backward_and_a_heal_raises_it()
    {
        // second 0: dmg 5000 (kills). second 1: dmg 1000. second 2: heal 2000 (net +2000).
        var rows = DeathRecapEngine.Build(new RecapReading
        {
            MaxHealthEstimate = 10000,
            Events = new List<RecapEvent> { Dmg(0.5, 5000), Dmg(1.5, 1000), Heal(2.5, 2000) },
        });
        // HP_end(s) = cumulative net-damage over seconds [0..s-1]:
        //  0s: 0 ; 1s: 5000 ; 2s: 5000+1000 = 6000. The +2000 heal at second 2 does NOT change
        //  HP_end(2) (its net applies within second 2, whose END we don't display beyond its own row);
        //  it raises HP_end(3) — absent here. So displayed hp: 2s=60%, 1s=50%, 0s=0%.
        Assert.Equal(new[] { "-2s", "-1s", "0s" }, rows.ConvertAll(r => r.Name).ToArray());
        Assert.Equal(0.60, rows[0].Percent, 3);
        Assert.Equal(0.50, rows[1].Percent, 3);
        Assert.Equal(0.00, rows[2].Percent, 3);
    }

    [Fact]
    public void Health_percent_clamps_at_100_when_window_damage_exceeds_the_estimate()   // plan-watch item 2
    {
        var rows = DeathRecapEngine.Build(new RecapReading
        {
            MaxHealthEstimate = 4000,
            Events = new List<RecapEvent> { Dmg(0.5, 5000), Dmg(1.5, 3000) },  // cumulative to top = 5000 > 4000
        });
        Assert.Equal(1.0, rows[0].Percent, 3);                 // pinned at 100%
        Assert.Equal("100%", rows[0].FormattedPercent);
        Assert.Equal(NumberFormat.Abbreviate(4000), rows[0].FormattedValue);   // est-health clamped to the estimate
    }

    [Fact]
    public void Empty_seconds_are_skipped_and_dmg_heals_are_colored_secondaries()
    {
        var rows = DeathRecapEngine.Build(new RecapReading
        {
            MaxHealthEstimate = 10000,
            Events = new List<RecapEvent> { Dmg(0.5, 5000), Heal(0.7, 1000), Dmg(4.5, 2000) },
        });
        // seconds present: 0 (dmg 5000 + heal 1000) and 4 (dmg 2000); seconds 1,2,3 skipped.
        Assert.Equal(new[] { "-4s", "0s" }, rows.ConvertAll(r => r.Name).ToArray());
        var deathRow = rows[1];
        Assert.Equal(2, deathRow.Secondaries.Count);
        Assert.Equal("-5K", deathRow.Secondaries[0].FormattedValue);   // dmg, red
        Assert.Equal(DeathRecapEngine.DmgArgb, deathRow.Secondaries[0].Argb);
        Assert.Equal("+1K", deathRow.Secondaries[1].FormattedValue);   // heals, green
        Assert.Equal(DeathRecapEngine.HealArgb, deathRow.Secondaries[1].Argb);
    }

    [Fact]
    public void Bar_fraction_equals_the_health_percent()
    {
        var rows = DeathRecapEngine.Build(new RecapReading
        {
            MaxHealthEstimate = 10000,
            Events = new List<RecapEvent> { Dmg(0.5, 5000), Dmg(1.5, 5000) },
        });
        foreach (var r in rows) Assert.Equal(r.Percent, r.BarFraction, 3);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter DeathRecapEngineTests`
Expected: FAIL — types not defined.

- [ ] **Step 3: Implement the DTOs**

`src/eq2auras.Core/Meter/DeathRecap.cs`:

```csharp
using System.Collections.Generic;

namespace Eq2Auras.Core.Meter
{
    /// One incoming event in a death's recap window (SPEC §Death Recap). The Plugin flattens the
    /// victim's incoming damage/heal MasterSwings into these; Core buckets + reconstructs.
    public sealed class RecapEvent
    {
        public double SecondsBeforeDeath { get; set; }   // >= 0; 0 = the death second
        public double Amount { get; set; }               // positive magnitude
        public bool IsHeal { get; set; }                 // true = healing received; false = damage taken
    }

    public sealed class RecapReading
    {
        public string DrillKey { get; set; }             // which death (Victim#Ordinal) this recap is for
        public double MaxHealthEstimate { get; set; }    // CombatantData.GetMaxHealth() at read time
        public List<RecapEvent> Events { get; set; }
    }
}
```

- [ ] **Step 4: Implement `DeathRecapEngine`**

`src/eq2auras.Core/Meter/DeathRecapEngine.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace Eq2Auras.Core.Meter
{
    /// The Death Recap surface (SPEC §Death Recap): a victim's incoming events over the last 10 s
    /// → one row per active second, health reconstructed BACKWARD from 0-at-death as a fraction of
    /// the max-health estimate, clamped. NOT BreakdownEngine (ranked) — a chronological HP track.
    public static class DeathRecapEngine
    {
        public const int DmgArgb = unchecked((int)0xFFF2A0A0);   // red
        public const int HealArgb = unchecked((int)0xFF2FBF8F);  // green
        private const int WindowSeconds = 10;
        private static readonly int FillArgb = MeterFamilyColors.ArgbFor("Damage");

        public static List<MeterRow> Build(RecapReading reading)
        {
            var rows = new List<MeterRow>();
            if (reading?.Events == null) return rows;

            // Bucket by whole second before death (0..WindowSeconds-1), summing dmg/heals.
            var dmg = new double[WindowSeconds];
            var heal = new double[WindowSeconds];
            var present = new bool[WindowSeconds];
            foreach (var e in reading.Events)
            {
                int s = (int)Math.Floor(e.SecondsBeforeDeath);
                if (s < 0 || s >= WindowSeconds) continue;
                if (e.IsHeal) heal[s] += e.Amount; else dmg[s] += e.Amount;
                present[s] = true;
            }

            double max = reading.MaxHealthEstimate;
            // HP at END of second s = cumulative net damage over seconds [0 .. s-1].
            // (net damage of a second = dmg - heal; the second's own net affects the NEXT-older row.)
            double cumulativeNetDamage = 0;
            for (int s = 0; s < WindowSeconds; s++)
            {
                if (present[s])
                {
                    double hp = cumulativeNetDamage;                       // HP at end of second s
                    double clamped = Math.Max(0, Math.Min(max, hp));
                    double pct = max > 0 ? clamped / max : 0;
                    rows.Add(new MeterRow
                    {
                        Name = "-" + s + "s",
                        Value = clamped,
                        FormattedValue = NumberFormat.Abbreviate(clamped),
                        Percent = pct,
                        FormattedPercent = Math.Round(pct * 100) + "%",
                        BarFraction = pct,
                        FillArgb = FillArgb,
                        Secondaries = BuildSecondaries(dmg[s], heal[s]),
                    });
                }
                cumulativeNetDamage += dmg[s] - heal[s];                   // roll back one more second
            }

            rows.Reverse();          // oldest second first, death (0s) last
            if (rows.Count > 0) rows[0].Name = rows[0].Name;   // (names already correct)
            // Fix "0s" label (s==0 → "-0s"): normalize.
            foreach (var r in rows) if (r.Name == "-0s") r.Name = "0s";
            return rows;
        }

        private static List<SecondaryValue> BuildSecondaries(double dmg, double heal)
        {
            var list = new List<SecondaryValue>();
            list.Add(new SecondaryValue
            {
                Key = "dmg",
                FormattedValue = dmg > 0 ? NumberFormat.SignedAbbreviate(-dmg) : "—",
                Argb = DmgArgb,
            });
            list.Add(new SecondaryValue
            {
                Key = "heal",
                FormattedValue = heal > 0 ? NumberFormat.SignedAbbreviate(heal) : "—",
                Argb = HealArgb,
            });
            return list;
        }
    }
}
```

> The `"-0s"` normalization keeps the death row reading `0s` (not `-0s`); the tests assert `"0s"`. Verify the two-secondary order is [dmg, heal] so the visual renders red then green.

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter DeathRecapEngineTests`
Expected: PASS (5 tests). Then full suite green.

- [ ] **Step 6: Commit**

```bash
git add src/eq2auras.Core/Meter/DeathRecap.cs src/eq2auras.Core/Meter/DeathRecapEngine.cs tests/eq2auras.Core.Tests/DeathRecapEngineTests.cs
git commit -m "Deaths: DeathRecapEngine — per-second HP reconstruction backward from 0-at-death, clamped (Core TDD)"
```

---

### Task 7: Plugin — death-drill request + recap deep-read — TRANSCRIBE

**Files:**
- Modify: `src/eq2auras.Core/Meter/Breakdown.cs` (`DrillRequest.DeathKey`)
- Modify: `src/eq2auras.Plugin/Act/EncounterProbe.cs` (recap deep-read → `RecapReading`, extend callback)
- Modify: `src/eq2auras.Plugin/Eq2AurasPlugin.cs` / `OverlayHost.cs` (thread `recaps`)

**Interfaces:**
- Produces: `DrillRequest.DeathKey` (string, null for scalar drills); `EncounterProbe` emits `List<RecapReading>` alongside `breakdowns`; callback becomes `(...combatants, breakdowns, deaths, recaps)`.

- [ ] **Step 1: Extend `DrillRequest`** (Core)

In `Breakdown.cs`, add to `DrillRequest`:

```csharp
public string DeathKey { get; set; }   // set when Source == Deaths — which death (Victim#Ordinal) to recap; null otherwise
```

- [ ] **Step 2: Recap deep-read in `EncounterProbe`** (inside the lock, in the requests loop)

Replace the drill-request loop body with a branch on `MetricBreakdownSource.Deaths`:

```csharp
foreach (var request in requests)
{
    if (request.Source == MetricBreakdownSource.Deaths)
    {
        var recap = ReadRecap(encounter, request);   // may return null (death gone / not found)
        if (recap != null) recaps.Add(recap);
        continue;
    }
    if (request.Source == MetricBreakdownSource.None) continue;
    // ... existing by-ability breakdown read unchanged ...
}
```

Add the helper (whole-second window, incoming damage + healing swings):

```csharp
private static RecapReading ReadRecap(EncounterData encounter, DrillRequest request)
{
    // DeathKey = "Victim#Ordinal"
    int hash = (request.DeathKey ?? "").LastIndexOf('#');
    if (hash < 0) return null;
    string victimName = request.DeathKey.Substring(0, hash);
    if (!int.TryParse(request.DeathKey.Substring(hash + 1), out int ordinal)) return null;
    if (!encounter.Items.TryGetValue(victimName.ToUpper(), out var victim)) return null;

    string allKey = ActGlobals.ActLocalization.LocalizationStrings["attackTypeTerm-all"].DisplayedText;

    // locate the ordinal-th Death swing (chronological)
    var deathSwings = new List<MasterSwing>();
    if (victim.Items.TryGetValue(CombatantData.DamageTypeDataIncomingKilling, out var killing))
        foreach (var at in killing.Items) if (at.Key != allKey)
            foreach (var sw in at.Value.Items) if (sw.Damage == Dnum.Death) deathSwings.Add(sw);
    deathSwings.Sort((a, b) => a.TimeSorter.CompareTo(b.TimeSorter));
    if (ordinal < 1 || ordinal > deathSwings.Count) return null;   // death gone → host auto-exits
    var death = deathSwings[ordinal - 1];

    var events = new List<RecapEvent>();
    void Collect(string bucketName, bool isHeal)
    {
        if (!victim.Items.TryGetValue(bucketName, out var bucket)) return;
        foreach (var at in bucket.Items) if (at.Key != allKey)
            foreach (var sw in at.Value.Items)
            {
                double secondsBefore = (death.Time - sw.Time).TotalSeconds;
                if (sw.TimeSorter > death.TimeSorter || secondsBefore < 0 || secondsBefore >= 10) continue;
                long amt = (long)sw.Damage;
                if (amt <= 0) continue;
                events.Add(new RecapEvent { SecondsBeforeDeath = secondsBefore, Amount = amt, IsHeal = isHeal });
            }
    }
    Collect(CombatantData.DamageTypeDataIncomingDamage, isHeal: false);
    Collect(CombatantData.DamageTypeDataIncomingHealing, isHeal: true);

    return new RecapReading
    {
        DrillKey = request.DeathKey,
        MaxHealthEstimate = victim.GetMaxHealth(),   // CombatantData.GetMaxHealth() — running-min estimate
        Events = events,
    };
}
```

> Buckets pinned (plan-watch item 3): incoming damage = `DamageTypeDataIncomingDamage`, incoming healing = `DamageTypeDataIncomingHealing`, deaths = incoming Killing bucket. `GetMaxHealth()` exists on `CombatantData` (decompiled). Confirm exact alias-static names at transcribe time against the parse-engine doc.

- [ ] **Step 3: Thread `recaps` through the callback** (as Task 4 did for `deaths`)

`_onSample`/`UpdateMeterSample`/`Eq2AurasPlugin` gain the 5th arg `List<RecapReading> recaps`.

- [ ] **Step 4: Commit**

```bash
git add src/eq2auras.Core/Meter/Breakdown.cs src/eq2auras.Plugin/Act/EncounterProbe.cs src/eq2auras.Plugin/Overlay/OverlayHost.cs src/eq2auras.Plugin/Eq2AurasPlugin.cs
git commit -m "Deaths: recap deep-read (one victim's incoming dmg+heal over 10s + GetMaxHealth) + DrillRequest.DeathKey [transcribe]"
```

---

### Task 8: Plugin — drill into a death, render the recap, middot delimiter — TRANSCRIBE

**Files:**
- Modify: `src/eq2auras.Plugin/Overlay/MeterWindow.cs` (death-row drill, recap header, middot)
- Modify: `src/eq2auras.Plugin/Overlay/MeterRowVisual.cs` (`CurrentDrillKey`; render N colored secondaries)
- Modify: `src/eq2auras.Plugin/Overlay/OverlayHost.cs` (death-drill routing + auto-exit)

- [ ] **Step 1: `MeterRowVisual` — expose `CurrentDrillKey`, render N colored secondaries**

Add `public string CurrentDrillKey { get; private set; }`; in `Update`, set `CurrentDrillKey = row.DrillKey;`. Replace the single-`_secondary` block with a loop that (re)builds one right-aligned `TextBlock` per `row.Secondaries` entry, foreground = `entry.Argb.HasValue ? new SolidColorBrush(FromArgb(entry.Argb.Value)) : Theme.TextLabel`, inserted left of the value column in order. (Pool/clear the secondary TextBlocks across updates; normal rows pass 0–1 so behaviour there is unchanged.)

- [ ] **Step 2: `MeterWindow` — drill by DrillKey; compose the recap header; middot**

- The row left-click handler (`RenderSlots`, ~306): `EnterDrill(slot.CurrentDrillKey ?? slot.CurrentName)`.
- `EnterDrill(string key)`: if the window's metric `IsEvent` (deaths), the key is a `DeathKey`; set `_drillSource = MetricBreakdownSource.Deaths`, and compose the recap header **from the clicked row** (which the window is rendering) — `‹ Name (Detail-without-leading-space) … time-of-death`. Concretely, capture the clicked `MeterRow` and build:
  `_metricText.Text = "‹ " + row.Name + " " + row.Detail;` and set the right-cluster total cell to `row.FormattedValue` (the time-of-death). Publish `DrillTarget` with `{ CombatantName = victim, Source = Deaths, DeathKey = key }` (victim = key up to '#').
- **Middot delimiter (SPEC §Row drill-down):** change the *existing* by-ability line `MeterWindow.cs:350` from `" — "` to `" · "`:
  `_metricText.Text = "‹ " + combatantName + " · " + _drillMetricLabel;`

- [ ] **Step 3: `OverlayHost` — route the death drill + auto-exit**

In `UpdateMeterSample`, for a drilled deaths window: find the matching `RecapReading` by `DrillKey`; `window.RenderDrill(DeathRecapEngine.Build(recap), ownTotalText: <time-of-death>)`. Auto-exit when the drilled death is absent from `deaths` (the death left scope / new encounter): if no `DeathRecord` in `deaths` has `DrillKey == target.DeathKey`, `window.ExitDrill()` + `window.Render(listFrame)`.

- [ ] **Step 4: Commit + push for CI compile + field test**

```bash
git add src/eq2auras.Plugin/Overlay/MeterWindow.cs src/eq2auras.Plugin/Overlay/MeterRowVisual.cs src/eq2auras.Plugin/Overlay/OverlayHost.cs
git commit -m "Deaths: drill a death → per-second recap render + auto-exit; shared middot drill delimiter [transcribe]"
git push origin meter-deaths-recap    # verify-only CI
```

Expected CI: green. **Full feature now field-testable.**

---

## Testing strategy

**Core (Mac, TDD — the gate for all logic):** Tasks 1–3, 6 land `NumberFormatTests`, `DeathsEngineTests`, `DeathsMetricRegistrationTests`, `DeathRecapEngineTests` — covering the formatters, the event-timeline build (chronological order, ordinals, killing-blow detail, into-fight bar, count total, no-killing-blow dash, clamps), registry/selection registration, and the recap reconstruction (per-second bucketing, backward-from-0, the heal bump, **the >estimate clamp — plan-watch item 2**, empty-second skip, colored secondaries, bar=hp%). Run the full suite green after each task.

**Plugin (on-box merge-gate live script — the only verification of the transcribe-only Plugin):**

*List mode (after Phase 1 / Task 5):*
1. Set a meter to **Deaths** (Damage-red). In a fight where an ally dies, a row appears: `Name` white + `(1) · ‹killing blow› ‹dmg›` muted, time-of-death right-aligned, bar filling to the death's fraction of the current fight; header right shows the death **count**.
2. An ally who dies twice shows two rows, `(1)` then `(2)`, chronological (earliest nearest the anchored edge).
3. A death with no clean source shows `—` for the killing blow; name + time intact.
4. Live: bars **recede** as the fight lengthens; freeze at combat end. New encounter clears the list.

*Recap (after Phase 2 / Task 8):*
5. Left-click a death → body swaps to per-second rows `−Ns · dmg(red) · heals(green) · health · hp%`, bar = the draining HP; a heal-heavy second ticks health up; quiet seconds absent.
6. Header reads `(dur) ‹ Name (N) · ‹killing blow›+dmg … time-of-death · ⚙`.
7. **Right-click → back to the list**; from the list, right-click still opens the popup.
8. Drill works while **locked**; the recap **refreshes live** and **freezes** at end; starting a new fight (or the death leaving scope) **auto-exits** to the list; reload opens in list mode.
9. **Regression:** the by-ability drill (e.g. a DPS window) header now reads with a **middot** (`‹ Name · DPS`); timer overlay unaffected (only `MeterRowVisual`/Core changed, `BarRowVisual` untouched).

**Plan-watch items (from the spec review) — where each lands:**
1. `CombatantData.Deaths` cost — **already verified cheap** (boolean-cached, ACT 3.8.5.288; backlog). Task 4 reads it per poll on that basis.
2. Reconstruction clamp — `DeathRecapEngineTests.Health_percent_clamps_at_100_when_window_damage_exceeds_the_estimate` (Task 6).
3. Killing-blow identification + recap buckets — Task 4 `FindKillingBlow` (last incoming damage swing ≤ death `TimeSorter`) + Task 7 buckets (`DamageTypeDataIncomingDamage`/`…IncomingHealing`/incoming Killing), pinned against `docs/act-parse-engine.md` + the decompile at transcribe time.

## Self-review notes

- **Spec coverage:** list timeline (Task 2), ordinals/killing-blow/into-fight bar/count total (Task 2), Deaths selection Allies/Damage (Task 3), poll-only count-delta capture (Task 4), event-metric routing (Task 5), recap per-second reconstruction + clamp + colored dmg/heals + est-health value (Task 6), 10s deep-read + GetMaxHealth (Task 7), drill/right-click-back/auto-exit/live + middot (Task 8). No secondary on deaths (Task 2 test). Transient drill/store (Task 4 reset; no persistence added).
- **Type consistency:** `DrillKey` (MeterRow) ↔ `DeathKey` (DrillRequest) ↔ `RecapReading.DrillKey` all `"Victim#Ordinal"`. `SecondaryValue.Argb` int? consumed in Task 8. `MetricDef.IsEvent` gates routing in Task 5.
- **Open transcribe confirmations (flagged inline, on-box/decompile):** exact alias-static names for the incoming Killing bucket and `GetMaxHealth()` return type; the `_detail`/secondary-column WPF layout matching the mockups.
