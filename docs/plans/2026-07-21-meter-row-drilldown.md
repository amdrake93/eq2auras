# Meter Row Drill-Down Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Left-clicking a combatant row swaps a meter window's body in place to that combatant's by-ability breakdown of the current primary metric (each ability's value + its percent of that combatant's own total); right-click returns to the list.

**Architecture:** Core gains a `breakdownSource` descriptor on each `MetricDef` and a surface-agnostic `BreakdownEngine` that turns a `(label, value)` list into ranked `MeterRow`s (reusing the existing row DTO + row/bar visual). The Plugin's `EncounterProbe` deep-reads **one** drilled combatant's per-ability `AttackType` aggregates under the same ACT lock, driven by a drill-request channel the `OverlayHost` publishes from its windows; the host routes each drilled window's breakdown through `BreakdownEngine` and renders it via the shared row pool. Drill state is transient (never persisted).

**Tech Stack:** C# — Core is netstandard2.0 (xUnit, strict TDD, Mac-testable); Plugin is net472/WPF (**transcribe-only** — never built on the Mac; CI compile-gated + on-box field script, per every prior meter slice).

## Global Constraints

- **No `async` in the Plugin project; no non-GAC types in fields unless compiled in** (single-assembly packaging — ACT's pre-`InitPlugin` type scan). — CLAUDE.md engine rules.
- **No new second DLL, no `System.Web.Extensions`, no `Assembly.LoadFrom`.** JSON stays `DataContractJsonSerializer`; **enum knob defaults must be the 0-value** (DCJS skips field initializers on deserialize). — CLAUDE.md.
- **All ACT reads happen briefly under `ActGlobals.oFormActMain.AfterCombatActionDataLock`**, snapshot into Core DTOs (no ACT types, no WPF types), release, render from the snapshot. Never hold an `EncounterData`/`CombatantData` reference across ticks; never read `EncId`/`GetHashCode()`. — SPEC §The one data rule; `docs/act-parse-engine.md` §Thread safety.
- **Core is strict TDD; Plugin is transcribe-only** (WPF is not buildable on the Mac — the branch-push verify CI compiles it, and Alex's on-box merge-gate script is the runtime gate). — CLAUDE.md two-machine reality.
- **Timers are untouched.** No shared-substrate re-extraction this slice — the shared `BarRowVisual`/`MeterRowVisual` are consumed as-is (one additive `CurrentName` field on `MeterRowVisual`), so the timer needs only a light sanity check. — SPEC §Testing strategy (row drill-down).
- **Reuse the `MeterRow` DTO for breakdown rows** — the drill-down is a different data source into the same rows, not a new visual (SPEC §Row drill-down). Do **not** add a parallel breakdown-row type.
- **Never `git add -A`/`.`** — `spike-data/` holds large untracked ferried logs. Stage explicit paths only. — repo memory.

---

## Plan-watch items (from the spec review 2026-07-21 — this plan must land each)

1. **`breakdownSource` bucket per metric** — pinned in **Task 1** against the vendored `ThirdParty/ACT_English_Parser.cs` and `docs/act-parse-engine.md`; the Plugin's enum→bucket map is in **Task 3**. `cures`/`powerheal` are the non-obvious ones — resolved to `"Cure/Dispel (Out)"` / `"Power Replenish (Out)"` (`ACT_English_Parser.cs:2086,2088`).
2. **Deep-read lock discipline** — the drilled combatant's `AttackType` read in **Task 3** happens inside the existing `lock (form.AfterCombatActionDataLock)` block in `EncounterProbe.OnTick`, in the same pass that snapshots the combatants; it reads at most **one** `CombatantData` per drill request, never a per-combatant fan-out.
3. **Auto-exit wiring** — **Task 6**: a drilled window whose target name is absent from the scope-filtered `listFrame.Rows` is auto-exited (`ExitDrill()` + list render) before any breakdown lookup; no stale/empty detail, no crash.

---

## File Structure

**Core (netstandard2.0) — strict TDD:**
- Create `src/eq2auras.Core/Meter/MetricBreakdownSource.cs` — the `MetricBreakdownSource` enum (`None`=0 default; the six real buckets). Responsibility: name which ACT `DamageTypeData` bucket a metric's by-ability detail reads, with **no ACT types** in Core.
- Modify `src/eq2auras.Core/Meter/MetricDef.cs` — add a `BreakdownSource` property + ctor parameter.
- Modify `src/eq2auras.Core/Meter/MetricRegistry.cs` — pass each metric's `breakdownSource`.
- Create `src/eq2auras.Core/Meter/Breakdown.cs` — the drill DTOs: `BreakdownEntry` (label + raw value, the engine's input and the probe's output element), `BreakdownReading` (combatant name + source + entries — probe→host), `DrillRequest` (combatant name + source — host→probe).
- Create `src/eq2auras.Core/Meter/BreakdownEngine.cs` — `static List<MeterRow> Build(IReadOnlyList<BreakdownEntry> entries, MetricDef metric, double durationSeconds)`. Reuses `MeterRow`; mirrors `MeterEngine`'s rate/percent/sort/bar math.
- Modify `src/eq2auras.Core/Meter/MeterEngine.cs` — extract the private `EncounterDuration` into a **public static** `DurationSeconds(EncounterReading)` so the host can compute the duration for `BreakdownEngine.Build` (single source of the duration policy).

**Plugin (net472/WPF) — transcribe-only:**
- Modify `src/eq2auras.Plugin/Act/EncounterProbe.cs` — drill-request input + per-combatant `AttackType` deep-read + the enum→bucket/value map; `_onSample` gains the breakdowns list.
- Modify `src/eq2auras.Plugin/Overlay/MeterRowVisual.cs` — expose `CurrentName` (the last-bound row's name) for click resolution.
- Modify `src/eq2auras.Plugin/Overlay/MeterWindow.cs` — drill state, `EnterDrill`/`ExitDrill`/`RenderDrill`/`DrillTarget`, header swap, per-slot left-click, context-sensitive right-click.
- Modify `src/eq2auras.Plugin/Overlay/MeterWindowCallbacks.cs` — add `Action DrillChanged`.
- Modify `src/eq2auras.Plugin/Overlay/OverlayHost.cs` — `UpdateMeterSample` gains breakdowns; per-window list-vs-drill routing + auto-exit; `CurrentDrillRequests()` + volatile snapshot; wire `DrillChanged`.
- Modify `src/eq2auras.Plugin/Eq2AurasPlugin.cs` — pass `() => _overlay.CurrentDrillRequests()` to the probe; 3-arg `_onSample` lambda.

**Tests (Core only):**
- Modify `tests/eq2auras.Core.Tests/MetricRegistryTests.cs` — each metric's `BreakdownSource`.
- Create `tests/eq2auras.Core.Tests/BreakdownEngineTests.cs` — the engine's math.

---

## Task 1: Core — the `breakdownSource` descriptor

**Files:**
- Create: `src/eq2auras.Core/Meter/MetricBreakdownSource.cs`
- Modify: `src/eq2auras.Core/Meter/MetricDef.cs`
- Modify: `src/eq2auras.Core/Meter/MetricRegistry.cs`
- Test: `tests/eq2auras.Core.Tests/MetricRegistryTests.cs`

**Interfaces:**
- Produces: `enum MetricBreakdownSource { None, OutgoingDamage, IncomingDamage, OutgoingHealing, IncomingHealing, PowerReplenish, Cures }`; `MetricDef.BreakdownSource` (get); `MetricDef` ctor gains a trailing `MetricBreakdownSource breakdownSource` parameter. Task 2 and Task 3 consume `MetricDef.BreakdownSource`.

- [ ] **Step 1: Write the failing test**

Add to `tests/eq2auras.Core.Tests/MetricRegistryTests.cs`:

```csharp
[Theory]
[InlineData("encdps", MetricBreakdownSource.OutgoingDamage)]
[InlineData("damagetaken", MetricBreakdownSource.IncomingDamage)]
[InlineData("enchps", MetricBreakdownSource.OutgoingHealing)]
[InlineData("totalhealing", MetricBreakdownSource.OutgoingHealing)]
[InlineData("healstaken", MetricBreakdownSource.IncomingHealing)]
[InlineData("powerheal", MetricBreakdownSource.PowerReplenish)]
[InlineData("cures", MetricBreakdownSource.Cures)]
public void Each_metric_names_its_by_ability_breakdown_bucket(string key, MetricBreakdownSource expected)
{
    var metric = MetricRegistry.All.Single(m => m.Key == key);
    Assert.Equal(expected, metric.BreakdownSource);
}

[Fact]
public void Damage_dealt_and_damage_taken_read_opposite_buckets()
{
    // Same Damage total, opposite direction — the descriptor cannot be derived from `select`.
    var dealt = MetricRegistry.All.Single(m => m.Key == "encdps");
    var taken = MetricRegistry.All.Single(m => m.Key == "damagetaken");
    Assert.NotEqual(dealt.BreakdownSource, taken.BreakdownSource);
}
```

(The file already `using Eq2Auras.Core.Meter;` and `using System.Linq;`? — add `using System.Linq;` if absent.)

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter "FullyQualifiedName~MetricRegistryTests"`
Expected: FAIL — `MetricBreakdownSource` / `MetricDef.BreakdownSource` do not exist (compile error).

- [ ] **Step 3: Create the enum**

`src/eq2auras.Core/Meter/MetricBreakdownSource.cs`:

```csharp
namespace Eq2Auras.Core.Meter
{
    /// Which of ACT's per-combatant DamageTypeData buckets a metric's by-ability
    /// drill-down reads (SPEC Part III §The metric registry — breakdownSource). A Core
    /// enum only: the Plugin maps each value to the concrete CombatantData.DamageTypeData…
    /// alias-static bucket + the per-AttackType value accessor. Required per-metric because
    /// the total alone does not determine it (encdps vs damagetaken read the same Damage
    /// total off opposite buckets). None = the metric has no by-ability breakdown (0-value,
    /// DCJS-safe default, though every registry metric today names a real bucket).
    public enum MetricBreakdownSource
    {
        None = 0,
        OutgoingDamage,
        IncomingDamage,
        OutgoingHealing,
        IncomingHealing,
        PowerReplenish,
        Cures,
    }
}
```

- [ ] **Step 4: Add the property to `MetricDef`**

In `src/eq2auras.Core/Meter/MetricDef.cs`, add the property and ctor parameter (append the parameter last so it is unambiguous):

```csharp
public string Key { get; }
public string Label { get; }
public string Category { get; }        // picker grouping + family color (MeterFamilyColors) — a display attribute, never a dispatch axis
public bool IsRate { get; }
public Func<CombatantReading, double> Select { get; }
public Func<double, string> Format { get; }
public MetricBreakdownSource BreakdownSource { get; }   // which ACT bucket the by-ability drill-down reads (SPEC §The metric registry)

public MetricDef(string key, string label, string category, bool isRate,
    Func<CombatantReading, double> select, Func<double, string> format,
    MetricBreakdownSource breakdownSource)
{
    Key = key;
    Label = label;
    Category = category;
    IsRate = isRate;
    Select = select;
    Format = format;
    BreakdownSource = breakdownSource;
}
```

- [ ] **Step 5: Populate the registry**

In `src/eq2auras.Core/Meter/MetricRegistry.cs`, append each metric's source (buckets pinned against `ThirdParty/ACT_English_Parser.cs:2082-2088` — the EQ2 `SetupEQ2EnglishEnvironment` alias statics — and `docs/act-parse-engine.md:73-83`):

```csharp
public static readonly IReadOnlyList<MetricDef> All = new List<MetricDef>
{
    new MetricDef("encdps", "DPS", "Damage", isRate: true, r => r.Damage, NumberFormat.Abbreviate, MetricBreakdownSource.OutgoingDamage),
    new MetricDef("enchps", "HPS", "Healing", isRate: true, r => r.Healed, NumberFormat.Abbreviate, MetricBreakdownSource.OutgoingHealing),
    new MetricDef("cures", "Cures", "Utility", isRate: false, r => r.CureDispels, NumberFormat.Integer, MetricBreakdownSource.Cures),
    new MetricDef("damagetaken", "Damage Taken", "Damage", isRate: false, r => r.DamageTaken, NumberFormat.Abbreviate, MetricBreakdownSource.IncomingDamage),
    new MetricDef("totalhealing", "Total Healing", "Healing", isRate: false, r => r.Healed, NumberFormat.Abbreviate, MetricBreakdownSource.OutgoingHealing),
    new MetricDef("healstaken", "Healing Taken", "Healing", isRate: false, r => r.HealsTaken, NumberFormat.Abbreviate, MetricBreakdownSource.IncomingHealing),
    new MetricDef("powerheal", "Power Replenish", "Utility", isRate: false, r => r.PowerReplenish, NumberFormat.Abbreviate, MetricBreakdownSource.PowerReplenish),
};
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter "FullyQualifiedName~MetricRegistryTests"`
Expected: PASS (all metric-source cases + the opposite-bucket case).

- [ ] **Step 7: Run the full Core suite (no regressions from the ctor change)**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
Expected: PASS — the only construction site is `MetricRegistry`, so nothing else broke.

- [ ] **Step 8: Commit**

```bash
git add src/eq2auras.Core/Meter/MetricBreakdownSource.cs src/eq2auras.Core/Meter/MetricDef.cs src/eq2auras.Core/Meter/MetricRegistry.cs tests/eq2auras.Core.Tests/MetricRegistryTests.cs
git commit -m "Core: MetricDef gains breakdownSource — the by-ability drill-down bucket per metric"
```

---

## Task 2: Core — the breakdown DTOs + `BreakdownEngine`

**Files:**
- Create: `src/eq2auras.Core/Meter/Breakdown.cs`
- Create: `src/eq2auras.Core/Meter/BreakdownEngine.cs`
- Modify: `src/eq2auras.Core/Meter/MeterEngine.cs`
- Test: `tests/eq2auras.Core.Tests/BreakdownEngineTests.cs`

**Interfaces:**
- Consumes: `MetricDef` + `MetricDef.BreakdownSource`, `MetricBreakdownSource` (Task 1); `MeterRow`, `MeterFamilyColors`, `EncounterReading` (existing).
- Produces:
  - `sealed class BreakdownEntry { string Label; double Value; }` (raw per-ability value)
  - `sealed class BreakdownReading { string CombatantName; MetricBreakdownSource Source; List<BreakdownEntry> Entries; }` (probe→host — Task 3/6)
  - `sealed class DrillRequest { string CombatantName; MetricBreakdownSource Source; }` (host→probe — Task 3/6)
  - `static class BreakdownEngine { static List<MeterRow> Build(IReadOnlyList<BreakdownEntry> entries, MetricDef metric, double durationSeconds); }` (Task 6)
  - `static double MeterEngine.DurationSeconds(EncounterReading encounter)` (Task 6)

- [ ] **Step 1: Write the failing tests**

Create `tests/eq2auras.Core.Tests/BreakdownEngineTests.cs`:

```csharp
using System.Collections.Generic;
using Eq2Auras.Core.Meter;
using Xunit;

public class BreakdownEngineTests
{
    private static MetricDef Dps => MetricRegistry.All[0];         // encdps — a rate
    private static MetricDef Cures =>
        System.Linq.Enumerable.Single(MetricRegistry.All, m => m.Key == "cures");   // a count/total (IsRate false)

    private static List<BreakdownEntry> Entries(params (string, double)[] xs)
    {
        var list = new List<BreakdownEntry>();
        foreach (var (label, value) in xs) list.Add(new BreakdownEntry { Label = label, Value = value });
        return list;
    }

    [Fact]
    public void Rows_sort_descending_with_a_name_tiebreak()
    {
        var rows = BreakdownEngine.Build(Entries(("Ice Comet", 100), ("Auto Attack", 100), ("Fiery Blast", 300)), Dps, durationSeconds: 1);
        Assert.Equal(new[] { "Fiery Blast", "Auto Attack", "Ice Comet" },
            rows.ConvertAll(r => r.Name).ToArray());   // 300 first; tie 100/100 -> ordinal name (Auto < Ice)
    }

    [Fact]
    public void Percent_is_share_of_the_lists_own_sum()
    {
        var rows = BreakdownEngine.Build(Entries(("A", 300), ("B", 100)), Cures, durationSeconds: 1);
        Assert.Equal(0.75, rows[0].Percent, 3);
        Assert.Equal("75%", rows[0].FormattedPercent);
        Assert.Equal("25%", rows[1].FormattedPercent);
    }

    [Fact]
    public void A_single_entry_is_one_hundred_percent_and_a_full_bar()
    {
        var rows = BreakdownEngine.Build(Entries(("Only", 42)), Cures, durationSeconds: 1);
        Assert.Single(rows);
        Assert.Equal("100%", rows[0].FormattedPercent);
        Assert.Equal(1.0, rows[0].BarFraction, 3);
    }

    [Fact]
    public void An_empty_entry_list_yields_zero_rows()
    {
        Assert.Empty(BreakdownEngine.Build(Entries(), Dps, durationSeconds: 1));
    }

    [Fact]
    public void A_zero_sum_list_gives_zero_percent_and_no_divide_by_zero()
    {
        var rows = BreakdownEngine.Build(Entries(("A", 0), ("B", 0)), Cures, durationSeconds: 1);
        Assert.Equal("0%", rows[0].FormattedPercent);
        Assert.Equal(0.0, rows[0].BarFraction, 3);
    }

    [Fact]
    public void Bar_fraction_is_relative_to_the_top_entry()
    {
        var rows = BreakdownEngine.Build(Entries(("Top", 200), ("Half", 100)), Cures, durationSeconds: 1);
        Assert.Equal(1.0, rows[0].BarFraction, 3);
        Assert.Equal(0.5, rows[1].BarFraction, 3);
    }

    [Fact]
    public void A_rate_metric_divides_each_value_by_duration_and_formats_via_the_metric()
    {
        // encdps: raw 50000 damage over 100s -> 500 DPS; format is the K/M/B abbreviation.
        var rows = BreakdownEngine.Build(Entries(("Fiery Blast", 50_000)), Dps, durationSeconds: 100);
        Assert.Equal(500, rows[0].Value);
        Assert.Equal("500", rows[0].FormattedValue);
    }

    [Fact]
    public void A_total_metric_is_the_raw_value_never_divided()
    {
        // cures: IsRate false -> the count is the value regardless of duration.
        var rows = BreakdownEngine.Build(Entries(("Cure Ward", 7)), Cures, durationSeconds: 100);
        Assert.Equal(7, rows[0].Value);
        Assert.Equal("7", rows[0].FormattedValue);
    }

    [Fact]
    public void Percent_is_duration_independent_for_a_rate_metric()
    {
        // Duration cancels in the share — the same percents at any duration.
        var rows = BreakdownEngine.Build(Entries(("A", 300), ("B", 100)), Dps, durationSeconds: 37);
        Assert.Equal("75%", rows[0].FormattedPercent);
    }

    [Fact]
    public void Every_row_takes_the_metrics_family_color()
    {
        var rows = BreakdownEngine.Build(Entries(("A", 1)), Dps, durationSeconds: 1);
        Assert.Equal(MeterFamilyColors.ArgbFor(Dps.Category), rows[0].FillArgb);
        Assert.Empty(rows[0].Secondaries);   // breakdown rows carry no secondary
    }

    [Fact]
    public void Duration_seconds_matches_the_engines_live_and_final_policy()
    {
        Assert.Equal(0, MeterEngine.DurationSeconds(new EncounterReading { Exists = false }));
        Assert.Equal(100, MeterEngine.DurationSeconds(
            new EncounterReading { Exists = true, Active = true, LiveDurationSeconds = 100, FinalDurationSeconds = 0 }));
        Assert.Equal(90, MeterEngine.DurationSeconds(
            new EncounterReading { Exists = true, Active = false, LiveDurationSeconds = 120, FinalDurationSeconds = 90 }));
        Assert.Equal(0, MeterEngine.DurationSeconds(       // degenerate pre-first-swing clamps at 0
            new EncounterReading { Exists = true, Active = true, LiveDurationSeconds = -1e15, FinalDurationSeconds = 0 }));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter "FullyQualifiedName~BreakdownEngineTests"`
Expected: FAIL — `BreakdownEntry`, `BreakdownEngine`, `MeterEngine.DurationSeconds` do not exist (compile error).

- [ ] **Step 3: Create the DTOs**

`src/eq2auras.Core/Meter/Breakdown.cs`:

```csharp
using System.Collections.Generic;

namespace Eq2Auras.Core.Meter
{
    /// One by-ability entry the drill-down deep-read produces: an ability label and its
    /// RAW value (per-ability AttackType total — the Plugin reads it, Core divides by
    /// duration for rate metrics). No ACT types (SPEC Part III §The one data rule).
    public sealed class BreakdownEntry
    {
        public string Label { get; set; }
        public double Value { get; set; }
    }

    /// The probe→host drill snapshot: one combatant's by-ability entries for one
    /// breakdown bucket, read under the ACT lock (SPEC Part III §Assembly split).
    public sealed class BreakdownReading
    {
        public string CombatantName { get; set; }
        public MetricBreakdownSource Source { get; set; }
        public List<BreakdownEntry> Entries { get; set; }
    }

    /// The host→probe drill request: which combatant + which bucket a drilled window
    /// needs deep-read this poll. Transient — never persisted (SPEC Part III §Settings).
    public sealed class DrillRequest
    {
        public string CombatantName { get; set; }
        public MetricBreakdownSource Source { get; set; }
    }
}
```

- [ ] **Step 4: Add `MeterEngine.DurationSeconds` (refactor the private helper to public static)**

In `src/eq2auras.Core/Meter/MeterEngine.cs`, replace the private `EncounterDuration` with a public static `DurationSeconds`, and update its one caller (`double duration = EncounterDuration(encounter);` → `double duration = DurationSeconds(encounter);`):

```csharp
/// Live wall clock while active, finalized log time once ended (SPEC Part III §Rates come
/// from our wall clock). Clamp defends the degenerate fresh-encounter poll where
/// StartTime == DateTime.MaxValue makes the live estimate hugely negative before the first swing.
/// Public + static so the drill-down's BreakdownEngine shares the one duration policy.
public static double DurationSeconds(EncounterReading encounter)
{
    if (encounter == null || !encounter.Exists) return 0;
    return Math.Max(0, encounter.Active ? encounter.LiveDurationSeconds : encounter.FinalDurationSeconds);
}
```

(Change the call site at the top of `Tick`: `double duration = DurationSeconds(encounter);`.)

- [ ] **Step 5: Create the engine**

`src/eq2auras.Core/Meter/BreakdownEngine.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace Eq2Auras.Core.Meter
{
    /// Turns a drilled combatant's (label, raw-value) list into ranked MeterRows — the
    /// surface-agnostic sibling of MeterEngine's row math (SPEC Part III §Row drill-down).
    /// Reuses the MeterRow DTO (same row/bar visual, no secondary column). The same duration
    /// policy as MeterEngine (rate ÷ wall-clock, total raw), so per-ability values sum to the
    /// combatant's own total; percent = share of THAT sum (duration cancels for rates).
    public static class BreakdownEngine
    {
        public static List<MeterRow> Build(IReadOnlyList<BreakdownEntry> entries, MetricDef metric, double durationSeconds)
        {
            var rows = new List<MeterRow>();
            if (entries == null || metric == null) return rows;

            double total = 0;
            foreach (var entry in entries)
            {
                double value = metric.IsRate
                    ? (durationSeconds > 0 ? entry.Value / durationSeconds : 0)
                    : entry.Value;
                total += value;
                rows.Add(new MeterRow
                {
                    Name = entry.Label ?? "",
                    Value = value,
                    Secondaries = new List<SecondaryValue>(),   // drill rows carry no secondary (SPEC §Row drill-down)
                });
            }

            rows.Sort((a, b) => a.Value != b.Value
                ? b.Value.CompareTo(a.Value)
                : string.CompareOrdinal(a.Name, b.Name));   // same deterministic tie-break as the list
            double top = rows.Count > 0 ? rows[0].Value : 0;

            foreach (var row in rows)
            {
                row.Percent = total > 0 ? row.Value / total : 0;
                row.FormattedPercent = Math.Round(row.Percent * 100) + "%";
                row.BarFraction = top > 0 ? row.Value / top : 0;
                row.FormattedValue = metric.Format(row.Value);
                row.FillArgb = MeterFamilyColors.ArgbFor(metric.Category);
            }
            return rows;
        }
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter "FullyQualifiedName~BreakdownEngineTests"`
Expected: PASS (all cases).

- [ ] **Step 7: Run the full Core suite (the `DurationSeconds` refactor touches `MeterEngine`)**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
Expected: PASS — `MeterEngine` behavior is unchanged (same helper, renamed + promoted).

- [ ] **Step 8: Commit**

```bash
git add src/eq2auras.Core/Meter/Breakdown.cs src/eq2auras.Core/Meter/BreakdownEngine.cs src/eq2auras.Core/Meter/MeterEngine.cs tests/eq2auras.Core.Tests/BreakdownEngineTests.cs
git commit -m "Core: BreakdownEngine + drill DTOs — ranked by-ability rows reusing MeterRow"
```

---

## Task 3: Plugin — `EncounterProbe` deep-reads the drilled combatant (transcribe-only)

**Files:**
- Modify: `src/eq2auras.Plugin/Act/EncounterProbe.cs`

**Interfaces:**
- Consumes: `DrillRequest`, `BreakdownReading`, `BreakdownEntry`, `MetricBreakdownSource` (Task 2); ACT `CombatantData`/`DamageTypeData`/`AttackType` (Advanced_Combat_Tracker).
- Produces: `EncounterProbe(Func<bool> enabled, Func<IReadOnlyList<DrillRequest>> drillRequests, Action<EncounterReading, List<CombatantReading>, List<BreakdownReading>> onSample)` — Task 6 (`OverlayHost.CurrentDrillRequests`) and `Eq2AurasPlugin` supply the two funcs; the host's `UpdateMeterSample` consumes the 3rd arg.

> **Transcribe-only:** WPF/ACT code is not built on the Mac. Verification is the branch-push verify CI (compiles the Plugin) + Alex's on-box merge-gate script (§Testing strategy). There are no local test steps for Plugin tasks.

- [ ] **Step 1: Add the drill-request field, extend the ctor and the sample delegate**

In `EncounterProbe.cs`, change the fields and constructor:

```csharp
private readonly Func<bool> _enabled;
private readonly Func<IReadOnlyList<DrillRequest>> _drillRequests;
private readonly Action<EncounterReading, List<CombatantReading>, List<BreakdownReading>> _onSample;
private int _tick;

public EncounterProbe(Func<bool> enabled, Func<IReadOnlyList<DrillRequest>> drillRequests,
    Action<EncounterReading, List<CombatantReading>, List<BreakdownReading>> onSample)
{
    _enabled = enabled;
    _drillRequests = drillRequests;
    _onSample = onSample;
}
```

- [ ] **Step 2: Deep-read each drilled combatant inside the existing lock**

In `OnTick`, snapshot the drill requests **before** the lock (cheap reference read), then inside the `else` branch that iterates `encounter.Items.Values` — still under `lock (form.AfterCombatActionDataLock)` — build the breakdowns. Add a `List<BreakdownReading> breakdowns = new List<BreakdownReading>();` alongside `combatants`, and after the combatant loop (still inside the lock, inside the `else`):

```csharp
var requests = _drillRequests?.Invoke();
if (requests != null && requests.Count > 0)
{
    foreach (var request in requests)
    {
        // At most one CombatantData per request — never a per-combatant fan-out
        // (plan-watch #2). GetAllies is already resolved above; Items is keyed UPPERCASE.
        if (request.Source == MetricBreakdownSource.None) continue;
        if (!encounter.Items.TryGetValue((request.CombatantName ?? "").ToUpper(), out var combatant)) continue;
        var entries = ReadBreakdown(combatant, request.Source);
        if (entries != null)
            breakdowns.Add(new BreakdownReading { CombatantName = request.CombatantName, Source = request.Source, Entries = entries });
    }
}
```

Then change the hand-off at the end of the method:

```csharp
_onSample(encounterReading, combatants, breakdowns);   // outside the lock — hold it briefly
```

Declare `breakdowns` next to `combatants` so it is in scope at the hand-off:

```csharp
EncounterReading encounterReading;
var combatants = new List<CombatantReading>();
var breakdowns = new List<BreakdownReading>();
```

- [ ] **Step 3: Add the bucket + value map (the enum→ACT translation)**

Add these private helpers to `EncounterProbe` (bucket names via the alias statics — never hardcoded strings — per `docs/act-parse-engine.md:81-83`; `at.Swings` for the cure count, `at.Damage` for the positive-Dnum sums):

```csharp
/// Enum → ACT bucket alias-static. The statics are set at the EQ2 parser's init
/// (ThirdParty/ACT_English_Parser.cs:2082-2088), so read them at call time, not at type init.
private static string BucketName(MetricBreakdownSource source)
{
    switch (source)
    {
        case MetricBreakdownSource.OutgoingDamage:  return CombatantData.DamageTypeDataOutgoingDamage;
        case MetricBreakdownSource.IncomingDamage:  return CombatantData.DamageTypeDataIncomingDamage;
        case MetricBreakdownSource.OutgoingHealing: return CombatantData.DamageTypeDataOutgoingHealing;
        case MetricBreakdownSource.IncomingHealing: return CombatantData.DamageTypeDataIncomingHealing;
        case MetricBreakdownSource.PowerReplenish:  return CombatantData.DamageTypeDataOutgoingPowerReplenish;
        case MetricBreakdownSource.Cures:           return CombatantData.DamageTypeDataOutgoingCures;
        default: return null;
    }
}

/// Per-ability value: the positive-Dnum sum for damage/heal/power buckets; the swing
/// COUNT for cures (the count metric — CombatantData.CureDispels is a count). Field-gate:
/// confirm the cures column reads sensibly on the box (the sole count breakdown).
private static double ReadValue(MetricBreakdownSource source, AttackType at)
    => source == MetricBreakdownSource.Cures ? at.Swings : at.Damage;

/// One combatant's by-ability entries for a bucket, read under the ACT lock. Skips the
/// aggregate "All" AttackType (docs/act-parse-engine.md:69-71). Returns null if the bucket
/// is absent (nothing of that kind happened) — the caller adds no reading, the window shows
/// an empty detail until data arrives.
private static List<BreakdownEntry> ReadBreakdown(CombatantData combatant, MetricBreakdownSource source)
{
    var bucketName = BucketName(source);
    if (bucketName == null) return null;
    if (!combatant.Items.TryGetValue(bucketName, out var damageType)) return null;

    string allKey = ActGlobals.ActLocalization.LocalizationStrings["attackTypeTerm-all"].DisplayedText;
    var entries = new List<BreakdownEntry>();
    foreach (var pair in damageType.Items)
    {
        if (pair.Key == allKey) continue;   // the category aggregate, not a real ability
        entries.Add(new BreakdownEntry { Label = pair.Key, Value = ReadValue(source, pair.Value) });
    }
    return entries;
}
```

- [ ] **Step 4: Verify the branch compiles (CI)**

The Plugin cannot build on the Mac. After Task 6 wires the callers, the branch push runs verify CI (Core tests + **WPF plugin compile** + artifact). This task's code is exercised there and by the on-box script. (Commit now; the compile gate runs once the callers exist — do **not** push mid-task.)

- [ ] **Step 5: Commit**

```bash
git add src/eq2auras.Plugin/Act/EncounterProbe.cs
git commit -m "Plugin: EncounterProbe deep-reads one drilled combatant's AttackTypes under the lock"
```

---

## Task 4: Plugin — `MeterWindow` drill state + rendering (transcribe-only)

**Files:**
- Modify: `src/eq2auras.Plugin/Overlay/MeterRowVisual.cs`
- Modify: `src/eq2auras.Plugin/Overlay/MeterWindow.cs`

**Interfaces:**
- Consumes: `DrillRequest`, `MetricBreakdownSource`, `MetricRegistry.ResolvePrimary`, `MeterSelections.Resolve`, `MeterRow` (Core).
- Produces (for Task 5 wiring + Task 6 host):
  - `MeterRowVisual.CurrentName` (get) — the last-bound row's name.
  - `MeterWindow.DrillTarget` (get) → `DrillRequest` or null.
  - `MeterWindow.EnterDrill(string combatantName)`, `MeterWindow.ExitDrill()`, `MeterWindow.RenderDrill(List<MeterRow> rows, string ownTotalText)`.

- [ ] **Step 1: Expose the bound name on the row visual**

In `MeterRowVisual.cs`, add a property and set it in `Update`:

```csharp
public string CurrentName { get; private set; }
```

At the top of `Update(MeterRow row)`:

```csharp
public void Update(MeterRow row)
{
    CurrentName = row.Name;
    _bar.NameText.Text = row.Name;
    // …unchanged…
}
```

- [ ] **Step 2: Add the drill state fields to `MeterWindow`**

After the existing `_metricKey`/`_secondaryKey`/`_locked` fields, add:

```csharp
private string _drilledCombatant;              // null = list mode; non-null = drilled into this combatant
private MetricBreakdownSource _drillSource;     // resolved from the metric at EnterDrill
private string _drillMetricLabel;               // the framing metric's identity label (selection label), shown in the header
private List<MeterRow> _currentRows;            // the rows the slots currently render — list OR breakdown
```

Change `RenderSlots` and `OnScroll` to read `_currentRows` instead of `_lastFrame.Rows`, so both modes scroll/render through one path. In `Render(MeterFrame frame)` set `_currentRows = frame.Rows;` right after `_lastFrame = frame;`. In `RenderSlots`, replace `var rows = _lastFrame.Rows;` with `var rows = _currentRows ?? new List<MeterRow>();`. In `OnScroll`, replace the `if (_lastFrame == null) return;` guard with `if (_currentRows == null) return;`.

- [ ] **Step 3: Add `DrillTarget`**

```csharp
/// The window's current drill request, or null in list mode — the host reads this to build
/// the probe's drill-request set and to route list-vs-drill rendering (SPEC §Row drill-down).
public DrillRequest DrillTarget => _drilledCombatant == null
    ? null
    : new DrillRequest { CombatantName = _drilledCombatant, Source = _drillSource };
```

- [ ] **Step 4: Add `EnterDrill` / `ExitDrill` / `RenderDrill` + the header swap**

```csharp
/// Enter drill mode for a combatant (left-click a row, Task 5). Resolves the framing metric,
/// swaps the header's left identity to "‹ Name — metric" (back-hint chevron; SPEC §Header while
/// drilled), hides the secondary-label cell, clears the body until the next poll's breakdown,
/// and publishes the new drill state so the host requests the deep-read.
public void EnterDrill(string combatantName)
{
    if (string.IsNullOrEmpty(combatantName)) return;
    var metric = MetricRegistry.ResolvePrimary(_metricKey);
    if (metric == null) return;   // cleared primary shows no rows — nothing to drill

    _drilledCombatant = combatantName;
    _drillSource = metric.BreakdownSource;
    var selection = MeterSelections.Resolve(_scope, _metricKey);
    _drillMetricLabel = selection?.Label ?? metric.Label;

    _metricText.Text = "‹ " + combatantName + " — " + _drillMetricLabel;
    _metricText.Visibility = Visibility.Visible;
    SetHeaderLabel(_secondaryLabelText, "");   // no secondary cell while drilled (SPEC §Header while drilled)
    SetHeaderLabel(_totalText, "");            // filled by the next RenderDrill from the combatant's own total

    _currentRows = new List<MeterRow>();
    _scrollOffset = 0;
    RenderSlots();
    _cb.DrillChanged?.Invoke();
}

/// Leave drill mode. Clears state and republishes; the host's next Render(listFrame) restores
/// the list header + rows. Called by the user (right-click, Task 5) or the host on auto-exit.
public void ExitDrill()
{
    if (_drilledCombatant == null) return;
    _drilledCombatant = null;
    _cb.DrillChanged?.Invoke();
}

/// Render the drilled combatant's by-ability rows (host, each poll). The header's left identity
/// was set at EnterDrill; here we set the combatant's own total (from the list frame's row —
/// Task 6) and swap the body. Reuses the same slot pool as the list (SPEC §Row drill-down).
public void RenderDrill(List<MeterRow> rows, string ownTotalText)
{
    SetHeaderLabel(_secondaryLabelText, "");
    SetHeaderLabel(_totalText, ownTotalText);
    _currentRows = rows ?? new List<MeterRow>();
    _scrollOffset = Math.Max(0, Math.Min(_scrollOffset, _currentRows.Count - _visibleRows));
    RenderSlots();
}
```

- [ ] **Step 5: Add the `using`**

Ensure `MeterWindow.cs` has `using System.Collections.Generic;` (it already does — `_slots` is a `List<>`). `DrillRequest`/`MetricBreakdownSource`/`MetricRegistry`/`MeterSelections` are in `Eq2Auras.Core.Meter`, already imported.

- [ ] **Step 6: Commit**

```bash
git add src/eq2auras.Plugin/Overlay/MeterRowVisual.cs src/eq2auras.Plugin/Overlay/MeterWindow.cs
git commit -m "Plugin: MeterWindow drill state — EnterDrill/ExitDrill/RenderDrill + header swap"
```

---

## Task 5: Plugin — drill interaction (row left-click + context-sensitive right-click) (transcribe-only)

**Files:**
- Modify: `src/eq2auras.Plugin/Overlay/MeterWindow.cs`

**Interfaces:**
- Consumes: `EnterDrill`/`ExitDrill`/`_drilledCombatant`, `MeterRowVisual.CurrentName`, `OpenPopup` (Task 4 + existing).
- Produces: no new external surface — wires the gestures.

- [ ] **Step 1: Left-click a row → enter drill (only in list mode)**

In `RenderSlots`, where new slots are created (`while (_slots.Count < visible) { … }`), attach a left-click handler to the slot's root when it is created:

```csharp
while (_slots.Count < visible)
{
    var slot = new MeterRowVisual(_style, _opacity);
    slot.Root.MouseLeftButtonUp += (s, e) =>
    {
        // Left-click a combatant row drills in; left-click an ability row (drill mode)
        // is reserved (no-op) for the future per-ability detail window (SPEC §Row drill-down).
        if (_drilledCombatant == null)
        {
            EnterDrill(slot.CurrentName);
            e.Handled = true;
        }
    };
    _slots.Add(slot);
    _rowsPanel.Children.Add(slot.Root);
    slot.FadeIn();
}
```

(The closure captures `slot`, so `slot.CurrentName` reads the row currently bound to that slot at click time.)

- [ ] **Step 2: Right-click is context-sensitive — replace the header-only handler with a window-level one**

Remove the header-scoped right-click wiring:

```csharp
header.MouseRightButtonUp += (s, e) => OpenPopup(header);   // DELETE this line
```

After `Content = contentGrid;` in the constructor, add a single window-level handler (so right-click anywhere on the window — header or body — works, per SPEC §Configuration "right-click anywhere"):

```csharp
// Right-click = up one layer (SPEC §Row drill-down): list mode opens the popup (anchored to
// the header, as before); drill mode returns to the list. One window-level handler so it fires
// over the header AND the body ("right-click anywhere").
contentGrid.MouseRightButtonUp += (s, e) =>
{
    if (_drilledCombatant != null) ExitDrill();
    else OpenPopup(header);
    e.Handled = true;
};
```

`contentGrid` and `header` are both in scope at that point (local `var contentGrid`, field-assigned `header`). Keep `header` reachable: it is a local `var header` in the ctor and used by `OpenPopup(header)` — the lambda captures it, so no field is needed.

- [ ] **Step 3: Verify (CI compile + on-box script)**

No local build on the Mac. The branch push (after Task 6) compiles the Plugin in CI; interaction correctness is Alex's on-box gate (§Testing strategy).

- [ ] **Step 4: Commit**

```bash
git add src/eq2auras.Plugin/Overlay/MeterWindow.cs
git commit -m "Plugin: drill gestures — row left-click enters drill; right-click is context-sensitive"
```

---

## Task 6: Plugin — host routing + drill-request channel + plugin wiring (transcribe-only)

**Files:**
- Modify: `src/eq2auras.Plugin/Overlay/MeterWindowCallbacks.cs`
- Modify: `src/eq2auras.Plugin/Overlay/OverlayHost.cs`
- Modify: `src/eq2auras.Plugin/Eq2AurasPlugin.cs`

**Interfaces:**
- Consumes: `MeterWindow.DrillTarget`/`RenderDrill`/`ExitDrill` (Task 4), `BreakdownEngine.Build`/`MeterEngine.DurationSeconds`/`MetricRegistry.ResolvePrimary` (Core), `EncounterProbe(enabled, drillRequests, onSample)` (Task 3).
- Produces: `OverlayHost.CurrentDrillRequests()`; `OverlayHost.UpdateMeterSample(EncounterReading, List<CombatantReading>, List<BreakdownReading>)`; `MeterWindowCallbacks.DrillChanged`.

- [ ] **Step 1: Add the `DrillChanged` callback**

In `MeterWindowCallbacks.cs`:

```csharp
public Action DrillChanged;   // window entered/left drill mode -> host rebuilds the drill-request snapshot
```

- [ ] **Step 2: Add the volatile drill-request snapshot + rebuild to `OverlayHost`**

Add a field near the other meter fields (`_meterWindows`):

```csharp
private volatile IReadOnlyList<DrillRequest> _drillRequests = new List<DrillRequest>();
```

Add the rebuild + the probe-facing accessor:

```csharp
/// Recompute the drill-request set from every window's current DrillTarget. Runs on the STA
/// thread (a window's DrillChanged callback fires there); the assignment is a lock-free
/// reference swap the probe reads via CurrentDrillRequests().
private void RebuildDrillRequests()
{
    var list = new List<DrillRequest>();
    foreach (var window in _meterWindows.Values)
    {
        var target = window.DrillTarget;
        if (target != null) list.Add(target);
    }
    _drillRequests = list;
}

/// Read by EncounterProbe on ACT's UI thread each poll (SPEC §Assembly split). Returns the
/// latest lock-free snapshot — the probe deep-reads each requested combatant under the lock.
public IReadOnlyList<DrillRequest> CurrentDrillRequests() => _drillRequests;
```

- [ ] **Step 3: Wire `DrillChanged` in `AddMeterWindow`**

In the `MeterWindowCallbacks { … }` initializer inside `AddMeterWindow`, add:

```csharp
DrillChanged = RebuildDrillRequests,
```

Also call `RebuildDrillRequests();` at the end of `CloseMeterWindow` (a closed window must drop out of the request set) — add it after the `SettingsStore.Update(... Windows.Remove(config))` line.

- [ ] **Step 4: Route list-vs-drill in `UpdateMeterSample`**

Replace the method with the 3-arg version that routes each window (auto-exit is plan-watch #3):

```csharp
/// Callable from any thread (the sample runs on ACT's UI thread). Fans the one shared
/// snapshot to each window; a drilled window renders its combatant's by-ability breakdown
/// instead of the list (SPEC §Row drill-down).
public void UpdateMeterSample(EncounterReading encounter, List<CombatantReading> combatants, List<BreakdownReading> breakdowns)
{
    var dispatcher = _dispatcher;
    if (dispatcher == null) return;
    dispatcher.BeginInvoke((Action)(() =>
    {
        double duration = MeterEngine.DurationSeconds(encounter);
        foreach (var pair in _meterWindows)
        {
            var config = pair.Key;
            var window = pair.Value;
            var listFrame = _meterEngine.Tick(encounter, combatants, config.MetricKey, config.SecondaryKey, config.Scope);

            var target = window.DrillTarget;
            var metric = MetricRegistry.ResolvePrimary(config.MetricKey);
            if (target == null || metric == null)
            {
                window.Render(listFrame);
                continue;
            }

            // The drilled combatant's OWN row in the scope-filtered list is its total AND the
            // auto-exit signal: gone from the list -> it left the scoped population (plan-watch #3).
            MeterRow ownRow = null;
            foreach (var row in listFrame.Rows)
                if (row.Name == target.CombatantName) { ownRow = row; break; }
            if (ownRow == null)
            {
                window.ExitDrill();
                window.Render(listFrame);
                continue;
            }

            BreakdownReading breakdown = null;
            if (breakdowns != null)
                foreach (var b in breakdowns)
                    if (b.CombatantName == target.CombatantName && b.Source == target.Source) { breakdown = b; break; }

            // Header total is the combatant's own list value (ready immediately); the body fills
            // when the breakdown arrives (one poll later than the click).
            var rows = breakdown != null
                ? BreakdownEngine.Build(breakdown.Entries, metric, duration)
                : new List<MeterRow>();
            window.RenderDrill(rows, ownRow.FormattedValue);
        }
    }));
}
```

Add `using System.Linq;`? — not needed (explicit loops above). `MetricRegistry`, `BreakdownEngine`, `MeterEngine`, `MeterRow`, `DrillRequest`, `BreakdownReading` are all in `Eq2Auras.Core.Meter`, already imported.

- [ ] **Step 5: Wire the probe's drill-request func in `Eq2AurasPlugin`**

In `Eq2AurasPlugin.cs`, change the `EncounterProbe` construction (currently the 2-arg form at `:43-45`):

```csharp
_encounterProbe = new EncounterProbe(
    () => _settings.Meter.Enabled,
    () => _overlay.CurrentDrillRequests(),
    (encounter, combatants, breakdowns) => _overlay.UpdateMeterSample(encounter, combatants, breakdowns));
```

- [ ] **Step 6: Push the branch — verify-only CI (Core tests + WPF compile + artifact)**

```bash
git add src/eq2auras.Plugin/Overlay/MeterWindowCallbacks.cs src/eq2auras.Plugin/Overlay/OverlayHost.cs src/eq2auras.Plugin/Eq2AurasPlugin.cs
git commit -m "Plugin: host routes drilled windows through BreakdownEngine; probe drill-request channel"
git push -u origin meter-row-drilldown
```

Then watch CI: `gh run watch <id> --exit-status`. Expected: Core tests green, **WPF plugin compiles**, artifact staged, publish skipped (branch, not `main`).

> **NOTE:** pushing is Alex's call in general, but the branch push is *verify-only* CI (no publish) and is the only way to compile the Plugin. If the executor should not push, stop here and report the branch is ready for Alex to push + review.

---

## Testing strategy

**Core (strict TDD, Mac loop — runs green before any Plugin work):** Tasks 1–2 cover the whole Core surface — `MetricDef.BreakdownSource` per metric (incl. `encdps` vs `damagetaken` distinct), and `BreakdownEngine` (descending sort + name tie-break, percent = value ÷ list sum, single→100%, empty→0 rows, zero-sum→0% no divide, bar vs top, rate ÷ duration vs raw total, percent duration-independence, family color, no secondary), plus `MeterEngine.DurationSeconds` matching the live/final/degenerate policy. Full suite must stay green (`dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`).

**Plugin (transcribe-only — CI compile + Alex's on-box merge-gate script):** the Plugin is not built on the Mac; the branch-push CI compiles the WPF project. Runtime correctness is Alex's on-box gate. **On-box merge-gate live script** (from SPEC §Testing strategy — row drill-down):

1. Left-click an ally on a DPS window → the body swaps to that ally's abilities, each with its value and its **percent of that ally's own total** (abilities sum to ~100%), sorted, drawn in the window's family color.
2. Header reads `‹ Name — DPS` and the right-cluster total shows that ally's **own** number; no secondary-label cell while drilled.
3. **Right-click → back to the list**; from the list, right-click still opens the popup (header and body).
4. Drill works while the window is **locked** (content interaction).
5. The breakdown **updates live** through a fight and **freezes** at combat end.
6. Starting a new fight (or Clear All) while drilled **auto-exits** to the list (no stale/empty detail, no crash).
7. Drilling under an **enemy**-scoped primary (Enemy Damage Taken) shows that enemy's abilities.
8. Drilling Healing / Damage Taken / **Cures** / Power Replenish each breaks down by the correct bucket — **Cures** especially (the sole `at.Swings` count read).
9. Reload → every window opens in **list** mode (drill state not persisted).
10. **Light timer sanity check** — timer overlay drain/spark/colors/positions unchanged (this slice re-extracts nothing; only `MeterRowVisual` gained a `CurrentName` field).

**Plan-watch verification:** #1 buckets — Task 1 (Core, cited to `ACT_English_Parser.cs:2082-2088`) + Task 3 map; the `cures`/`powerheal` accessors are on-box script step 8. #2 lock discipline — Task 3 reads inside the existing `AfterCombatActionDataLock`, ≤1 `CombatantData` per request. #3 auto-exit — Task 6, driven by absence from the scope-filtered `listFrame.Rows`.

## Self-review notes

- **Spec coverage:** by-ability breakdown (T2 engine + T3 read), percent-of-own-total (T2), reuse MeterRow/row visual (T2/T4), no secondary column (T2 `Secondaries` empty + T4 header cell hidden), right-click context-sensitive (T5), header `‹ Name — metric` + own total (T4/T6), left-click ability reserved no-op (T5 guard), live refresh + auto-exit (T6), transient drill state (no config field added anywhere), `breakdownSource` descriptor (T1), surface-agnostic engine + one-combatant deep-read (T2/T3). All covered.
- **Type consistency:** `EnterDrill`/`ExitDrill`/`RenderDrill`/`DrillTarget`/`CurrentName`/`DrillChanged`/`CurrentDrillRequests`/`UpdateMeterSample(…, breakdowns)`/`DurationSeconds`/`BreakdownEngine.Build(entries, metric, durationSeconds)` are named identically across the tasks that produce and consume them.
- **No placeholders:** every code step shows the real code; the only "verify" without a local command are the Plugin tasks, by construction (WPF unbuildable on the Mac) — routed to CI compile + the on-box script.
