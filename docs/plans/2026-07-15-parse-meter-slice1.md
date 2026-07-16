# Parse Meter Slice 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
> **Repo convention override:** this repo executes plans **inline** (superpowers:executing-plans) so the owner can watch — see CLAUDE.md §Working style.

**Goal:** Ship Parse Meter slice 1 (SPEC Part III): one interactive meter window over ACT's live encounter — metric picker {DPS, HPS, Cures}, wall-clock rates, width-lerp rows — plus the two shared-substrate extractions (overlay-window base, row/bar primitive) with the timer module re-seated on them.

**Architecture:** Core (netstandard2.0, TDD on the Mac) gains a `Meter/` sibling to `Timers/`: DTOs, the flat metric registry, and `MeterEngine` (readings → one renderable frame). The plugin gains an encounter adapter (`EncounterProbe`, polling ACT's computed model under `AfterCombatActionDataLock` on a divider of the existing 100 ms tick) and a code-only `MeterWindow`, both sitting on two components extracted from the timer implementation: `OverlayWindowBase` (geometry/persistence/grow/drag + the three-axis interaction model) and `BarRowVisual` (the configurable bar-row anatomy; spark is row config). The shipped timer windows re-seat onto both extractions **behavior-identically**.

**Tech Stack:** C# / netstandard2.0 Core + net472 WPF plugin (single assembly via `<Compile Include>` — the existing glob `..\eq2auras.Core\**\*.cs` picks up the new `Meter/` folder with no csproj change), xunit on net10.0 for Core tests, DataContractJsonSerializer for settings.

## Global Constraints

- **Never build the Plugin/solution on the Mac** — only `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`. Plugin compile verification = push branch → verify-only CI (`gh run watch <id> --exit-status`). CI checkpoints are explicit steps below.
- **No `System.Web.Extensions`, no `Assembly.LoadFrom`, no second DLL, no async state machines with non-GAC hoisted field types** (SPEC §Packaging).
- **DCJS rules:** enum/bool knob defaults must be the 0-value; nullable numerics where 0 is a legal-looking value (positions); missing fields → defaults.
- **All ACT model reads happen briefly under `ActGlobals.oFormActMain.AfterCombatActionDataLock`**; never hold `EncounterData` references across polls; never read `EncId`/`GetHashCode()` on a live encounter (SPEC Part III §The one data rule).
- **Retain elements, animate properties** — never rebuild visuals per tick.
- **Behavior-preserving extractions:** Tasks 4–5 move timer code verbatim (constants, math, event order). Any observable timer change is a bug in the task, not an accepted delta.
- **Settings mutation only through `SettingsStore.Update`** (one gate for ACT-UI-thread and overlay-STA-thread writers).
- Commit style: plain descriptive messages (no ticket prefixes in this repo), e.g. `Core: meter metric registry`.

---

### Task 1: `MeterSettings` + `Settings.Meter` wiring

**Files:**
- Create: `src/eq2auras.Core/Config/MeterSettings.cs`
- Modify: `src/eq2auras.Core/Config/Settings.cs` (add member + Normalize line)
- Test: `tests/eq2auras.Core.Tests/MeterSettingsTests.cs`

**Interfaces:**
- Consumes: existing `Settings.Parse`/`ToJson`/`Normalize` pattern.
- Produces: `MeterSettings { bool Enabled; double? Left; double? Top; string MetricKey; bool Locked }`; `Settings.Meter` (never null after `Parse`). Task 8 reads/writes all five members; Task 3 consumes `MetricKey` by value.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/eq2auras.Core.Tests/MeterSettingsTests.cs
using Eq2Auras.Core.Config;
using Xunit;

public class MeterSettingsTests
{
    [Theory]
    [InlineData("")]                        // empty file
    [InlineData("{}")]                      // old file with no meter section
    [InlineData("{\"meter\":null}")]        // explicit null section
    public void Missing_meter_section_yields_defaults(string json)
    {
        var parsed = Settings.Parse(json);

        Assert.NotNull(parsed.Meter);
        Assert.False(parsed.Meter.Enabled);   // default OFF — opt-in while groundwork (SPEC Part III)
        Assert.False(parsed.Meter.Locked);
        Assert.Null(parsed.Meter.Left);       // null, never 0 — 0 is a real screen edge
        Assert.Null(parsed.Meter.Top);
        Assert.Null(parsed.Meter.MetricKey);  // null key -> registry default at resolve time
    }

    [Fact]
    public void Meter_settings_roundtrip()
    {
        var settings = new Settings();
        settings.Meter.Enabled = true;
        settings.Meter.Left = 0;              // zero is a REAL position, must survive
        settings.Meter.Top = 451.5;
        settings.Meter.MetricKey = "enchps";
        settings.Meter.Locked = true;

        var parsed = Settings.Parse(settings.ToJson());

        Assert.True(parsed.Meter.Enabled);
        Assert.Equal(0.0, parsed.Meter.Left);
        Assert.Equal(451.5, parsed.Meter.Top);
        Assert.Equal("enchps", parsed.Meter.MetricKey);
        Assert.True(parsed.Meter.Locked);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter MeterSettingsTests`
Expected: FAIL — `'Settings' does not contain a definition for 'Meter'` (compile error counts as the failing state).

- [ ] **Step 3: Implement**

```csharp
// src/eq2auras.Core/Config/MeterSettings.cs
using System.Runtime.Serialization;

namespace Eq2Auras.Core.Config
{
    /// Parse Meter module settings (SPEC Part III §Assembly split & polling — Settings).
    /// Enabled defaults false (0-value rule): the meter is opt-in while groundwork.
    /// Positions nullable on purpose: null — never zero — means "unset, default placement".
    [DataContract]
    public sealed class MeterSettings
    {
        [DataMember(Name = "enabled")]
        public bool Enabled { get; set; }

        [DataMember(Name = "left")]
        public double? Left { get; set; }

        [DataMember(Name = "top")]
        public double? Top { get; set; }

        [DataMember(Name = "metricKey")]
        public string MetricKey { get; set; }   // null/unknown -> registry default (forward-compat guard)

        [DataMember(Name = "locked")]
        public bool Locked { get; set; }
    }
}
```

In `src/eq2auras.Core/Config/Settings.cs`, add after the `panels` member (below `DefaultPanels()`):

```csharp
        [DataMember(Name = "meter")]
        public MeterSettings Meter { get; set; } = new MeterSettings();
```

and in `Normalize()`, after the `PaletteArgb` normalization lines:

```csharp
            if (Meter == null) Meter = new MeterSettings();   // DCJS skips initializers
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
Expected: PASS (all — existing suites must stay green).

- [ ] **Step 5: Commit**

```bash
git add src/eq2auras.Core/Config/MeterSettings.cs src/eq2auras.Core/Config/Settings.cs tests/eq2auras.Core.Tests/MeterSettingsTests.cs
git commit -m "Core: MeterSettings section on Settings (enabled/position/metricKey/locked, DCJS-safe defaults)"
```

---

### Task 2: Meter DTOs, number formatting, metric registry

**Files:**
- Create: `src/eq2auras.Core/Meter/MeterReading.cs` (CombatantReading + EncounterReading)
- Create: `src/eq2auras.Core/Meter/MeterFrame.cs` (MeterRow + SecondaryValue + MeterFrame)
- Create: `src/eq2auras.Core/Meter/NumberFormat.cs`
- Create: `src/eq2auras.Core/Meter/MetricDef.cs`
- Create: `src/eq2auras.Core/Meter/MetricRegistry.cs`
- Test: `tests/eq2auras.Core.Tests/MetricRegistryTests.cs`

**Interfaces:**
- Consumes: nothing existing.
- Produces (Task 3 and Task 7 rely on these exact shapes):
  - `CombatantReading { string Name; long Damage; long Healed; int CureDispels }`
  - `EncounterReading { bool Exists; bool Active; string Title; double LiveDurationSeconds; double FinalDurationSeconds }`
  - `MeterRow { string Name; double Value; string FormattedValue; double Percent; string FormattedPercent; double BarFraction; int FillArgb; List<SecondaryValue> Secondaries }`
  - `SecondaryValue { string Key; string FormattedValue }`
  - `MeterFrame { List<MeterRow> Rows; string DurationText; string Title; string MetricLabel; string TotalText }`
  - `MetricDef { string Key, Label, Category; bool IsRate; Func<CombatantReading,double> Select; Func<double,string> Format }`
  - `MetricRegistry.All : IReadOnlyList<MetricDef>`, `MetricRegistry.DefaultKey == "encdps"`, `MetricRegistry.Resolve(string) : MetricDef` (never null; null/unknown → default)
  - `NumberFormat.Abbreviate(double) : string`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/eq2auras.Core.Tests/MetricRegistryTests.cs
using Eq2Auras.Core.Meter;
using Xunit;

public class MetricRegistryTests
{
    [Theory]
    [InlineData(0, "0")]
    [InlineData(950, "950")]
    [InlineData(1_000, "1K")]
    [InlineData(1_460, "1.5K")]        // one decimal, rounded (1_450 avoided: float midpoint formats differ across runtimes)
    [InlineData(890_000, "890K")]
    [InlineData(1_400_000, "1.4M")]
    [InlineData(4_200_000_000, "4.2B")]
    public void Abbreviates_with_kmb_family(double value, string expected)
        => Assert.Equal(expected, NumberFormat.Abbreviate(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-such-metric")]      // file from a future version
    public void Unknown_or_missing_key_resolves_to_the_dps_default(string? key)
        => Assert.Equal(MetricRegistry.DefaultKey, MetricRegistry.Resolve(key).Key);

    [Fact]
    public void Ships_exactly_the_three_slice1_metrics()
    {
        Assert.Equal(new[] { "encdps", "enchps", "cures" },
            System.Linq.Enumerable.Select(MetricRegistry.All, m => m.Key));
    }

    [Fact]
    public void Selectors_read_the_matching_totals()
    {
        var reading = new CombatantReading { Name = "Zephyria", Damage = 500, Healed = 300, CureDispels = 7 };

        Assert.Equal(500, MetricRegistry.Resolve("encdps").Select(reading));
        Assert.Equal(300, MetricRegistry.Resolve("enchps").Select(reading));
        Assert.Equal(7, MetricRegistry.Resolve("cures").Select(reading));
    }

    [Fact]
    public void Rates_are_rates_and_counts_are_counts()
    {
        Assert.True(MetricRegistry.Resolve("encdps").IsRate);
        Assert.True(MetricRegistry.Resolve("enchps").IsRate);
        Assert.False(MetricRegistry.Resolve("cures").IsRate);
        Assert.Equal("7", MetricRegistry.Resolve("cures").Format(7));          // counts: plain integer
        Assert.Equal("1.4M", MetricRegistry.Resolve("encdps").Format(1_400_000));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter MetricRegistryTests`
Expected: FAIL — namespace `Eq2Auras.Core.Meter` does not exist.

- [ ] **Step 3: Implement**

```csharp
// src/eq2auras.Core/Meter/MeterReading.cs
namespace Eq2Auras.Core.Meter
{
    /// One ally's per-poll totals, snapshotted from ACT's computed model under the
    /// data lock (SPEC Part III §The one data rule): corrections already applied,
    /// no ACT types, no WPF types.
    public sealed class CombatantReading
    {
        public string Name { get; set; }
        public long Damage { get; set; }
        public long Healed { get; set; }      // includes wards — the EQ2 parser folds absorbs in
        public int CureDispels { get; set; }
    }

    /// The current segment's per-poll identity/duration. Both duration branches
    /// travel so the live-vs-final selection is Core policy, testable on the Mac
    /// (SPEC Part III §Rates come from our wall clock).
    public sealed class EncounterReading
    {
        public bool Exists { get; set; }               // false: session start / after a clear
        public bool Active { get; set; }
        public string Title { get; set; }              // strongest-enemy-so-far; may flip mid-fight
        public double LiveDurationSeconds { get; set; }    // LastEstimatedTime - StartTime (may be garbage pre-first-swing; engine clamps)
        public double FinalDurationSeconds { get; set; }   // ACT's finalized log-time Duration
    }
}
```

```csharp
// src/eq2auras.Core/Meter/MeterFrame.cs
using System.Collections.Generic;

namespace Eq2Auras.Core.Meter
{
    /// One display row (SPEC Part III §The metric registry — two-tier): the primary
    /// metric drives Value/bar/sort; Secondaries is the slice-2 growth point — the
    /// shape ships now, slice 1 renders primary-only.
    public sealed class MeterRow
    {
        public string Name { get; set; }
        public double Value { get; set; }
        public string FormattedValue { get; set; }
        public double Percent { get; set; }          // share of ALL allies' total (0..1)
        public string FormattedPercent { get; set; }
        public double BarFraction { get; set; }      // vs. rank-1's value (0..1) — rank 1 = full bar
        public int FillArgb { get; set; }
        public List<SecondaryValue> Secondaries { get; set; }
    }

    public sealed class SecondaryValue
    {
        public string Key { get; set; }
        public string FormattedValue { get; set; }
    }

    /// Everything a meter window renders for one poll: header + rows. No ACT/WPF types.
    public sealed class MeterFrame
    {
        public List<MeterRow> Rows { get; set; }
        public string DurationText { get; set; }   // "3:24"
        public string Title { get; set; }
        public string MetricLabel { get; set; }    // "DPS"
        public string TotalText { get; set; }      // all-allies total, metric-formatted
    }
}
```

```csharp
// src/eq2auras.Core/Meter/NumberFormat.cs
using System;
using System.Globalization;

namespace Eq2Auras.Core.Meter
{
    /// The K/M/B abbreviation family (SPEC Part III §The metric registry — format).
    public static class NumberFormat
    {
        public static string Abbreviate(double value)
        {
            double abs = Math.Abs(value);
            if (abs >= 1_000_000_000) return Scaled(value / 1_000_000_000) + "B";
            if (abs >= 1_000_000) return Scaled(value / 1_000_000) + "M";
            if (abs >= 1_000) return Scaled(value / 1_000) + "K";
            return Math.Round(value).ToString(CultureInfo.InvariantCulture);
        }

        public static string Integer(double value)
            => Math.Round(value).ToString(CultureInfo.InvariantCulture);

        private static string Scaled(double value)
            => value.ToString("0.#", CultureInfo.InvariantCulture);   // one decimal, trailing zero dropped
    }
}
```

```csharp
// src/eq2auras.Core/Meter/MetricDef.cs
using System;

namespace Eq2Auras.Core.Meter
{
    /// One entry of the flat metric registry (SPEC Part III §The metric registry).
    /// Select returns the raw TOTAL; the engine divides by wall-clock duration when
    /// IsRate — the duration policy lives in one place, never in selectors.
    public sealed class MetricDef
    {
        public string Key { get; }
        public string Label { get; }
        public string Category { get; }        // picker grouping only — never a dispatch axis
        public bool IsRate { get; }
        public Func<CombatantReading, double> Select { get; }
        public Func<double, string> Format { get; }

        public MetricDef(string key, string label, string category, bool isRate,
            Func<CombatantReading, double> select, Func<double, string> format)
        {
            Key = key;
            Label = label;
            Category = category;
            IsRate = isRate;
            Select = select;
            Format = format;
        }
    }
}
```

```csharp
// src/eq2auras.Core/Meter/MetricRegistry.cs
using System.Collections.Generic;
using System.Linq;

namespace Eq2Auras.Core.Meter
{
    /// The meter's entire vocabulary: ACT's ExportVariables names, our plumbing
    /// (SPEC Part III §The metric registry). Adding a metric = appending a definition.
    public static class MetricRegistry
    {
        public const string DefaultKey = "encdps";

        public static readonly IReadOnlyList<MetricDef> All = new List<MetricDef>
        {
            new MetricDef("encdps", "DPS", "Damage", isRate: true, r => r.Damage, NumberFormat.Abbreviate),
            new MetricDef("enchps", "HPS", "Healing", isRate: true, r => r.Healed, NumberFormat.Abbreviate),
            new MetricDef("cures", "Cures", "Utility", isRate: false, r => r.CureDispels, NumberFormat.Integer),
        };

        /// Null/unknown keys resolve to the DPS default — the forward-compat guard
        /// for settings files written by newer versions (SPEC Part III §Settings).
        public static MetricDef Resolve(string key)
            => All.FirstOrDefault(m => m.Key == key) ?? All.First(m => m.Key == DefaultKey);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
Expected: PASS (all).

- [ ] **Step 5: Commit**

```bash
git add src/eq2auras.Core/Meter/ tests/eq2auras.Core.Tests/MetricRegistryTests.cs
git commit -m "Core: meter DTOs, K/M/B formatter, flat metric registry (encdps/enchps/cures)"
```

---

### Task 3: `MeterEngine` — readings in, one renderable frame out

**Files:**
- Create: `src/eq2auras.Core/Meter/MeterEngine.cs`
- Test: `tests/eq2auras.Core.Tests/MeterEngineTests.cs`

**Interfaces:**
- Consumes: Task 2's DTOs + registry; existing `PaletteAssigner` (`Eq2Auras.Core.Timers`).
- Produces: `MeterEngine` (stateful — owns its own `PaletteAssigner` instance, NOT the timer module's) with `MeterFrame Tick(EncounterReading encounter, List<CombatantReading> allies, string metricKey, IReadOnlyList<int> paletteArgb)`. The frame carries **every ally, sorted — no truncation**: visibility is the window's scroll concern (SPEC Part III §The meter window — Scrolling), never the data's. Task 8 constructs one and calls `Tick` per sample.

- [ ] **Step 1: Write the failing tests**

Covers the spec's Core-TDD list *and* spec-review plan-watch item 1 (degenerate fresh-encounter poll: empty allies, garbage negative live duration from `DateTime.MaxValue` StartTime) and item 2 (freeze-at-final branch selection asserted as intended behavior).

```csharp
// tests/eq2auras.Core.Tests/MeterEngineTests.cs
using System.Collections.Generic;
using Eq2Auras.Core.Meter;
using Xunit;

public class MeterEngineTests
{
    private static readonly List<int> Palette = new() { 0x11, 0x22, 0x33 };   // 3 slots

    private static EncounterReading Live(double seconds) => new()
        { Exists = true, Active = true, Title = "Vithnok", LiveDurationSeconds = seconds, FinalDurationSeconds = 0 };

    private static CombatantReading Ally(string name, long damage = 0, long healed = 0, int cures = 0)
        => new() { Name = name, Damage = damage, Healed = healed, CureDispels = cures };

    [Fact]
    public void Rates_divide_totals_by_the_live_wall_clock_while_active()
    {
        var frame = new MeterEngine().Tick(Live(100),
            new List<CombatantReading> { Ally("A", damage: 50_000) }, "encdps", Palette);

        Assert.Equal(500, frame.Rows[0].Value);          // 50k / 100s
        Assert.Equal("500", frame.Rows[0].FormattedValue);
        Assert.Equal("DPS", frame.MetricLabel);
        Assert.Equal("1:40", frame.DurationText);
        Assert.Equal("Vithnok", frame.Title);
    }

    [Fact]
    public void Frozen_encounter_switches_to_the_finalized_duration()
    {
        // Plan-watch item 2: the branch flip IS the intended display behavior — the
        // finalized log-time duration is generally shorter, so the rate steps up.
        var frozen = new EncounterReading
            { Exists = true, Active = false, Title = "Vithnok", LiveDurationSeconds = 120, FinalDurationSeconds = 100 };

        var frame = new MeterEngine().Tick(frozen,
            new List<CombatantReading> { Ally("A", damage: 50_000) }, "encdps", Palette);

        Assert.Equal(500, frame.Rows[0].Value);          // divides by Final (100), not Live (120)
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]              // pre-first-swing: StartTime == DateTime.MaxValue makes the live estimate hugely negative
    [InlineData(-1e15)]
    public void Degenerate_durations_yield_zero_rates_never_NaN_or_infinity(double liveSeconds)
    {
        // Plan-watch item 1: the degenerate fresh-encounter poll must render, not explode.
        var frame = new MeterEngine().Tick(Live(liveSeconds),
            new List<CombatantReading> { Ally("A", damage: 50_000) }, "encdps", Palette);

        Assert.Equal(0, frame.Rows[0].Value);
        Assert.Equal("0", frame.Rows[0].FormattedValue);
        Assert.Equal("0:00", frame.DurationText);
    }

    [Fact]
    public void Empty_encounter_and_empty_ally_list_render_an_empty_frame()
    {
        var none = new MeterEngine().Tick(new EncounterReading { Exists = false },
            new List<CombatantReading>(), "encdps", Palette);

        Assert.Empty(none.Rows);
        Assert.Equal("", none.Title);
        Assert.Equal("0:00", none.DurationText);
        Assert.Equal("DPS", none.MetricLabel);   // header still names the selected metric
        Assert.Equal("0", none.TotalText);
    }

    [Fact]
    public void Counts_never_divide_and_format_as_integers()
    {
        var frame = new MeterEngine().Tick(Live(100),
            new List<CombatantReading> { Ally("A", cures: 7) }, "cures", Palette);

        Assert.Equal(7, frame.Rows[0].Value);            // NOT 0.07
        Assert.Equal("7", frame.Rows[0].FormattedValue);
        Assert.Equal("Cures", frame.MetricLabel);
    }

    [Fact]
    public void Sorts_descending_with_ordinal_name_tiebreak_and_keeps_every_ally()
    {
        var allies = new List<CombatantReading>();
        for (int i = 0; i < 12; i++) allies.Add(Ally("P" + i.ToString("00"), damage: 1000 - i * 50));
        allies.Add(Ally("Aardvark", damage: 1000));      // ties with P00 — name breaks it, deterministically

        var frame = new MeterEngine().Tick(Live(10), allies, "encdps", Palette);

        Assert.Equal(13, frame.Rows.Count);              // NO truncation — the window scrolls (SPEC Part III)
        Assert.Equal("Aardvark", frame.Rows[0].Name);    // tie -> ordinal ascending
        Assert.Equal("P00", frame.Rows[1].Name);
        Assert.True(frame.Rows[1].Value >= frame.Rows[2].Value);
    }

    [Fact]
    public void Percent_is_share_of_ALL_allies_and_bar_is_share_of_top()
    {
        var allies = new List<CombatantReading>
            { Ally("A", damage: 600), Ally("B", damage: 300), Ally("C", damage: 100) };

        var frame = new MeterEngine().Tick(Live(10), allies, "encdps", Palette);

        Assert.Equal(0.6, frame.Rows[0].Percent, 3);
        Assert.Equal("60%", frame.Rows[0].FormattedPercent);
        Assert.Equal(1.0, frame.Rows[0].BarFraction, 3);   // rank 1 = full bar
        Assert.Equal(0.5, frame.Rows[1].BarFraction, 3);   // 300/600
        Assert.Equal("100", frame.TotalText);              // (600+300+100)/10s
    }

    [Fact]
    public void Percents_and_the_header_total_cover_the_full_ally_set()
    {
        var allies = new List<CombatantReading>();
        for (int i = 0; i < 20; i++) allies.Add(Ally("P" + i.ToString("00"), damage: 100));

        var frame = new MeterEngine().Tick(Live(10), allies, "encdps", Palette);

        Assert.Equal(20, frame.Rows.Count);                // every ally in the frame
        Assert.Equal(0.05, frame.Rows[0].Percent, 3);      // 1/20 of the ALL-allies total
        Assert.Equal("200", frame.TotalText);              // all 20 counted: 2000/10s
    }

    [Fact]
    public void Ally_colors_are_first_seen_stable_and_cycle_past_the_palette()
    {
        var engine = new MeterEngine();
        var allies = new List<CombatantReading>
            { Ally("A", damage: 400), Ally("B", damage: 300), Ally("C", damage: 200), Ally("D", damage: 100) };

        var first = engine.Tick(Live(10), allies, "encdps", Palette);
        Assert.Equal(0x11, first.Rows[0].FillArgb);
        Assert.Equal(0x22, first.Rows[1].FillArgb);
        Assert.Equal(0x33, first.Rows[2].FillArgb);
        Assert.Equal(0x11, first.Rows[3].FillArgb);        // 4th name cycles onto slot 0

        // B overtakes A next tick: colors follow the NAME, not the rank.
        allies[1].Damage = 900;
        var second = engine.Tick(Live(10), allies, "encdps", Palette);
        Assert.Equal("B", second.Rows[0].Name);
        Assert.Equal(0x22, second.Rows[0].FillArgb);
    }

    [Fact]
    public void Rows_carry_the_secondaries_shape_empty_in_slice_1()
    {
        var frame = new MeterEngine().Tick(Live(10),
            new List<CombatantReading> { Ally("A", damage: 100) }, "encdps", Palette);

        Assert.NotNull(frame.Rows[0].Secondaries);
        Assert.Empty(frame.Rows[0].Secondaries);
    }

    [Fact]
    public void Null_metric_key_falls_back_to_dps()
    {
        var frame = new MeterEngine().Tick(Live(10),
            new List<CombatantReading> { Ally("A", damage: 100) }, null, Palette);

        Assert.Equal("DPS", frame.MetricLabel);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter MeterEngineTests`
Expected: FAIL — `MeterEngine` does not exist.

- [ ] **Step 3: Implement**

```csharp
// src/eq2auras.Core/Meter/MeterEngine.cs
using System;
using System.Collections.Generic;
using System.Linq;
using Eq2Auras.Core.Timers;

namespace Eq2Auras.Core.Meter
{
    /// The meter-side sibling of OverlayEngine (SPEC Part III §Assembly split):
    /// per-poll readings in, one renderable frame out. Owns its OWN PaletteAssigner —
    /// ally names and timer names are disjoint namespaces; sharing one first-fired
    /// sequence would let ally names shift the shipped timer slot assignments
    /// (SPEC Part III §The meter window — Rows).
    public sealed class MeterEngine
    {
        private readonly PaletteAssigner _palette = new PaletteAssigner();

        public MeterFrame Tick(EncounterReading encounter, List<CombatantReading> allies,
            string metricKey, IReadOnlyList<int> paletteArgb)
        {
            var metric = MetricRegistry.Resolve(metricKey);

            // Live wall clock while active, finalized log time once ended (SPEC Part III
            // §Rates come from our wall clock). Clamp defends the degenerate
            // fresh-encounter poll: StartTime == DateTime.MaxValue makes the live
            // estimate hugely negative before the first swing lands.
            double duration = 0;
            if (encounter != null && encounter.Exists)
            {
                duration = Math.Max(0, encounter.Active
                    ? encounter.LiveDurationSeconds
                    : encounter.FinalDurationSeconds);
            }

            var rows = new List<MeterRow>();
            double total = 0;
            foreach (var ally in allies ?? new List<CombatantReading>())
            {
                double raw = metric.Select(ally);
                double value = metric.IsRate
                    ? (duration > 0 ? raw / duration : 0)
                    : raw;
                total += value;
                rows.Add(new MeterRow { Name = ally.Name ?? "", Value = value });
            }

            rows.Sort((a, b) => a.Value != b.Value
                ? b.Value.CompareTo(a.Value)
                : string.CompareOrdinal(a.Name, b.Name));   // deterministic tie-break, never epsilon-seeding
            double top = rows.Count > 0 ? rows[0].Value : 0;
            // NO truncation: every ally travels; visibility is the window's scroll
            // concern (SPEC Part III §The meter window — Scrolling), never the data's.

            foreach (var row in rows)
            {
                row.Percent = total > 0 ? row.Value / total : 0;     // share of ALL allies
                row.FormattedPercent = Math.Round(row.Percent * 100) + "%";
                row.BarFraction = top > 0 ? row.Value / top : 0;     // rank 1 = full bar
                row.FormattedValue = metric.Format(row.Value);
                row.FillArgb = paletteArgb[_palette.IndexFor(row.Name) % paletteArgb.Count];
                row.Secondaries = new List<SecondaryValue>();        // shape ships in slice 1; selection UX is slice 2
            }

            return new MeterFrame
            {
                Rows = rows,
                DurationText = FormatDuration(duration),
                Title = encounter != null && encounter.Exists ? (encounter.Title ?? "") : "",
                MetricLabel = metric.Label,
                TotalText = metric.Format(total),
            };
        }

        internal static string FormatDuration(double seconds)
        {
            int t = (int)Math.Max(0, seconds);
            return (t / 60) + ":" + (t % 60).ToString("00");
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
Expected: PASS (all).

- [ ] **Step 5: Commit**

```bash
git add src/eq2auras.Core/Meter/MeterEngine.cs tests/eq2auras.Core.Tests/MeterEngineTests.cs
git commit -m "Core: MeterEngine — wall-clock rates, freeze-at-final branch, sort/tiebreak over the full ally set, own palette map"
```

---

### Task 4: Extract `OverlayWindowBase`; re-seat both timer windows (behavior-preserving)

**Files:**
- Create: `src/eq2auras.Plugin/Overlay/OverlayWindowBase.cs`
- Modify: `src/eq2auras.Plugin/Overlay/TimerListWindow.xaml` (root element), `TimerListWindow.xaml.cs`
- Modify: `src/eq2auras.Plugin/Overlay/CenterZoneWindow.xaml` (root element), `CenterZoneWindow.xaml.cs`

**Interfaces:**
- Consumes: existing `ClickThrough.Set(Window, bool)`, `GrowDirection`.
- Produces (Task 6 relies on): `OverlayWindowBase : Window` with protected ctor `(double left, double top, GrowDirection grow, Action<double,double> persistPosition, bool clickThroughBaseline)`, `public double AnchorY`, `public void SetGrowDirection(GrowDirection)`, `protected void BeginDragAndPersist()`, `protected void SetClickThrough(bool)`.
- The duplicated members move to the base **verbatim in logic and order** — the moved set, present identically in both timer windows today: the `_persistPosition`/`_growDirection`/`_dragging` fields, the anchor-seeding ctor lines (`Left`/`Top` assignment + the click-through `SourceInitialized` hook + the `SizeChanged` subscription), `AnchorY`, `OnSizeChanged`, `SetGrowDirection`, and the drag-persist sequence inside `OnDragStart`. **Not** moved (they stay per-window): `SetMoveMode`, `SetStyle`, the move-chrome field/build, and each window's render method.

No Mac-runnable test exists for plugin code — the gates are (a) code moves verbatim, (b) the Task-5 CI checkpoint compiles it, (c) the merge-gate timer-regression pass. **Known residual after a green CI:** the XAML roots re-parent onto an abstract base with no parameterless ctor; the markup compile is what CI arbitrates, but a BAML-*load*-time failure would only surface in the live script — if the windows fail to construct on the box despite green CI, the fallback is composition (move the base's behaviors into a controller object attached from each window's ctor; same extraction, different mechanics).

- [ ] **Step 1: Create the base class**

```csharp
// src/eq2auras.Plugin/Overlay/OverlayWindowBase.cs
using System;
using System.Windows;
using Eq2Auras.Core.Config;

namespace Eq2Auras.Plugin.Overlay
{
    /// The shared overlay-window base (SPEC Part III §The shared rendering substrate):
    /// geometry + position persistence, grow-direction anchoring, drag-and-persist, and
    /// the click-through baseline axis of the three-axis interaction model. Timer
    /// windows pass clickThroughBaseline: true (move mode grants interactivity);
    /// the meter window passes false (interactive; its own lock gates dragging).
    public abstract class OverlayWindowBase : Window
    {
        private readonly Action<double, double> _persistPosition;
        private GrowDirection _growDirection;
        private bool _dragging;

        protected OverlayWindowBase(double left, double top, GrowDirection grow,
            Action<double, double> persistPosition, bool clickThroughBaseline)
        {
            _growDirection = grow;
            // Initial Top = the stored ANCHOR for both directions: the first
            // SizeChanged compensates the full initial height under Up, landing the
            // bottom edge on the anchor (SPEC §Window growth).
            Left = left;
            Top = top;
            _persistPosition = persistPosition;

            if (clickThroughBaseline)
            {
                SourceInitialized += (s, e) => ClickThrough.Set(this, true);
            }
            SizeChanged += OnSizeChanged;
        }

        /// The persisted vertical coordinate (SPEC §Window growth): the edge that
        /// doesn't move — top when growing down, bottom when growing up.
        public double AnchorY => _growDirection == GrowDirection.Up ? Top + ActualHeight : Top;

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Growing up = bottom edge anchored: compensate Top by the height delta.
            // Suppressed mid-drag (DragMove owns Top then); drag-end persists whatever
            // the user chose, which IS the reconciliation.
            if (_growDirection != GrowDirection.Up || _dragging) return;
            Top -= e.NewSize.Height - e.PreviousSize.Height;
        }

        /// Knob flip: converts and persists the anchored edge from the window's actual
        /// on-screen geometry — even from a null stored position. The knob changes how
        /// the window GROWS, never where it IS (SPEC §Window growth).
        public void SetGrowDirection(GrowDirection direction)
        {
            if (direction == _growDirection) return;
            _growDirection = direction;
            _persistPosition(Left, AnchorY);
        }

        /// Blocks until the button is released; persists the anchored edge — crash-safe.
        protected void BeginDragAndPersist()
        {
            _dragging = true;
            DragMove();
            _dragging = false;
            _persistPosition(Left, AnchorY);
        }

        protected bool GrowsUp => _growDirection == GrowDirection.Up;

        protected void SetClickThrough(bool clickThrough) => ClickThrough.Set(this, clickThrough);
    }
}
```

- [ ] **Step 2: Re-seat `TimerListWindow`**

`TimerListWindow.xaml` — change only the root element (attributes and children unchanged):

```xml
<overlay:OverlayWindowBase x:Class="Eq2Auras.Plugin.Overlay.TimerListWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:overlay="clr-namespace:Eq2Auras.Plugin.Overlay"
        WindowStyle="None" AllowsTransparency="True" Background="Transparent"
        Topmost="True" ShowInTaskbar="False" ResizeMode="NoResize"
        SizeToContent="Height" Width="260">
    <Grid x:Name="RootGrid">
        <StackPanel x:Name="RowsPanel" />
    </Grid>
</overlay:OverlayWindowBase>
```

`TimerListWindow.xaml.cs` — the class inherits the base; delete every member that moved (`_persistPosition`, `_growDirection`, `_dragging` fields; `AnchorY`; `OnSizeChanged`; `SetGrowDirection`; the body of drag handling). Result:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Eq2Auras.Core.Config;
using Eq2Auras.Core.Timers;

namespace Eq2Auras.Plugin.Overlay
{
    public partial class TimerListWindow : OverlayWindowBase
    {
        private const double WindowSlack = 10;

        // Retained visuals keyed by timer identity — updated, never rebuilt, so the
        // drain animations run continuously at display refresh.
        private readonly Dictionary<string, TimerRowVisual> _rows = new Dictionary<string, TimerRowVisual>();
        private readonly Grid _moveChrome;
        private VisualStyle _style;

        public TimerListWindow(string moveLabel, double left, double top, VisualStyle style,
            GrowDirection grow, Action<double, double> persistPosition)
            : base(left, top, grow, persistPosition, clickThroughBaseline: true)
        {
            InitializeComponent();
            _style = style;
            Width = style.RowWidth + WindowSlack;

            _moveChrome = MoveChrome.Build(moveLabel);
            RootGrid.Children.Add(_moveChrome);

            MouseLeftButtonDown += OnDragStart;
        }

        public void SetMoveMode(bool moving)
        {
            _moveChrome.Visibility = moving ? Visibility.Visible : Visibility.Collapsed;
            SetClickThrough(!moving);
        }

        /// Knob change (font/dimensions): drop the retained visuals once; the next tick
        /// recreates them under the new style. Pulses/drains restart once — accepted.
        public void SetStyle(VisualStyle style)
        {
            _style = style;
            Width = style.RowWidth + WindowSlack;
            _rows.Clear();
            RowsPanel.Children.Clear();
        }

        private void OnDragStart(object sender, MouseButtonEventArgs e)
        {
            if (_moveChrome.Visibility != Visibility.Visible) return;
            BeginDragAndPersist();
        }

        /// Called on the overlay's dispatcher thread with a fresh sorted snapshot.
        /// Row order anchors with the window (SPEC §Window growth): soonest-to-expire
        /// sits nearest the anchored edge, so grow-up reverses the visual order.
        public void RenderRows(List<TimerRow> rows)
        {
            var ordered = GrowsUp
                ? Enumerable.Reverse(rows)
                : rows;

            var seen = new HashSet<string>();
            RowsPanel.Children.Clear();   // same element instances re-added in sort order — animations continue
            foreach (var row in ordered)
            {
                var key = row.Name + "|" + row.Combatant;
                seen.Add(key);
                if (!_rows.TryGetValue(key, out var visual))
                {
                    visual = new TimerRowVisual(_style);
                    _rows[key] = visual;
                }
                visual.Update(row);
                RowsPanel.Children.Add(visual.Root);
            }

            foreach (var stale in _rows.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                _rows.Remove(stale);
            }
        }
    }
}
```

*(The loop body is byte-identical to today's `TimerListWindow.xaml.cs:104-122`; the only change in this method is `GrowsUp` replacing the local `_growDirection == GrowDirection.Up` check.)*

**Grow-direction read in `RenderRows`:** the field moved to the base — the base's `GrowsUp` property (Step 1) replaces the local `_growDirection == GrowDirection.Up` check. Everything inside the `RenderRows` loop body stays byte-identical to today.

- [ ] **Step 3: Re-seat `CenterZoneWindow` the same way**

Same XAML root change (`overlay:OverlayWindowBase`, add the `xmlns:overlay` declaration; `Width="200"`, child `ElementsPanel` untouched). Same code-behind deletion set; ctor becomes:

```csharp
        public CenterZoneWindow(string moveLabel, double left, double top, VisualStyle style,
            GrowDirection grow, Action<double, double> persistPosition)
            : base(left, top, grow, persistPosition, clickThroughBaseline: true)
        {
            InitializeComponent();
            _style = style;
            Width = style.RadialSize * BaseCenterWidth / VisualStyle.DefaultRadialSize;

            _moveChrome = MoveChrome.Build(moveLabel);
            RootGrid.Children.Add(_moveChrome);

            MouseLeftButtonDown += OnDragStart;
        }
```

`OnDragStart`, `SetMoveMode` identical in shape to TimerListWindow's; `RenderElements` body unchanged. `OverlayHost` needs **no changes** — `AnchorY`, `SetGrowDirection`, `SetMoveMode`, `SetStyle` keep their exact signatures.

- [ ] **Step 4: Commit**

```bash
git add src/eq2auras.Plugin/Overlay/OverlayWindowBase.cs src/eq2auras.Plugin/Overlay/TimerListWindow.xaml src/eq2auras.Plugin/Overlay/TimerListWindow.xaml.cs src/eq2auras.Plugin/Overlay/CenterZoneWindow.xaml src/eq2auras.Plugin/Overlay/CenterZoneWindow.xaml.cs
git commit -m "Plugin: extract OverlayWindowBase (grow/anchor/drag/persist + click-through axis); re-seat both timer windows"
```

---

### Task 5: Extract `BarRowVisual`; re-seat `TimerRowVisual` — then CI checkpoint

**Files:**
- Create: `src/eq2auras.Plugin/Overlay/BarRowVisual.cs`
- Modify: `src/eq2auras.Plugin/Overlay/TimerRowVisual.cs`

**Interfaces:**
- Consumes: `VisualStyle`, `OverlayTheme`.
- Produces (Task 6 relies on): `BarRowVisual` with ctor `(VisualStyle style, bool spark)`; members `UIElement Root`, `TextBlock NameText`, `TextBlock TrailingText`, `StackPanel TrailingPanel` (modules may append blocks), `Border RootBorder` (accent), `double UsableWidth`, `double CurrentFillWidth`, `void SetFillColor(int argb)`, `void AnimateDrain(double fromWidth, double seconds)`, `void AnimateToFraction(double fraction)`; `const double LerpSeconds = 0.35`.
- Every constant moves **verbatim** from today's `TimerRowVisual`: corner radii `3*hr`/`4*hr`, margins `8*hr`, spark thickness `3*hr`, fill alpha `90`, drain usable width `RowWidth - 2`, `CalmBackground`, border thickness 1, `ClipToBounds`, SemiBold trailing.

- [ ] **Step 1: Create the primitive**

```csharp
// src/eq2auras.Plugin/Overlay/BarRowVisual.cs
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Eq2Auras.Plugin.Overlay
{
    /// The shared row/bar primitive (SPEC Part III §The shared rendering substrate):
    /// one configurable component — horizontal bar, animatable proportional fill, fill
    /// color, leading name, trailing value area. Optional features are row CONFIG:
    /// the timer's spark is `spark: true`, not a separate timer bar. The pluggable
    /// part is the animation target: AnimateDrain (wall-clock, timer) vs.
    /// AnimateToFraction (data-driven lerp, meter).
    internal sealed class BarRowVisual
    {
        public const double LerpSeconds = 0.35;   // meter catch-up rate (tunable constant)

        private readonly double _rowWidth;
        private readonly bool _spark;
        private readonly Border _root;
        private readonly Border _fill;
        private readonly TextBlock _name;
        private readonly TextBlock _trailing;
        private readonly StackPanel _trailingPanel;

        public UIElement Root => _root;
        public Border RootBorder => _root;
        public TextBlock NameText => _name;
        public TextBlock TrailingText => _trailing;
        public StackPanel TrailingPanel => _trailingPanel;
        public double UsableWidth => _rowWidth - 2;
        public double CurrentFillWidth => _fill.Width;   // reflects the animated value

        public BarRowVisual(VisualStyle style, bool spark)
        {
            _rowWidth = style.RowWidth;
            _spark = spark;
            double hr = style.HeightRatio;

            _fill = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                CornerRadius = new CornerRadius(3 * hr),
                // The spark: a bright right-edge border riding the animated fill width —
                // marks the moving edge. Width is a future knob. Row config: meter rows
                // ship spark-less (SPEC Part III — spark is a customization of the row).
                BorderThickness = spark ? new Thickness(0, 0, 3 * hr, 0) : new Thickness(0)
            };
            _name = new TextBlock
            {
                Foreground = new SolidColorBrush(OverlayTheme.Text),
                Margin = new Thickness(8 * hr, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            style.ApplyFont(_name, style.RowText);
            _trailing = new TextBlock
            {
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            style.ApplyFont(_trailing, style.RowText);
            _trailingPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8 * hr, 0)
            };
            _trailingPanel.Children.Add(_trailing);

            var grid = new Grid();
            grid.Children.Add(_fill);
            grid.Children.Add(_name);
            grid.Children.Add(_trailingPanel);

            _root = new Border
            {
                Width = _rowWidth,
                Height = style.RowHeight,
                Margin = new Thickness(0, 0, 0, style.RowSpacing),
                CornerRadius = new CornerRadius(4 * hr),
                Background = new SolidColorBrush(OverlayTheme.CalmBackground),
                BorderThickness = new Thickness(1),
                ClipToBounds = true,
                Child = grid
            };
        }

        public void SetFillColor(int argb)
        {
            var color = OverlayTheme.FromArgbInt(argb);
            _fill.Background = new SolidColorBrush(Color.FromArgb(90, color.R, color.G, color.B));
            if (_spark) _fill.BorderBrush = new SolidColorBrush(OverlayTheme.Spark(color));
        }

        /// Timer target model: one linear drain to zero over the remaining seconds.
        public void AnimateDrain(double fromWidth, double seconds)
        {
            var drain = new DoubleAnimation(fromWidth, 0, TimeSpan.FromSeconds(Math.Max(0.05, seconds)));
            _fill.BeginAnimation(FrameworkElement.WidthProperty, drain);
        }

        /// Meter target model: rate-limited catch-up toward a data-driven fraction,
        /// re-targeted each poll. First bind grows from zero (reads as a fade-in).
        public void AnimateToFraction(double fraction)
        {
            double target = Math.Max(0, Math.Min(1, fraction)) * UsableWidth;
            if (double.IsNaN(_fill.Width)) _fill.Width = 0;
            var lerp = new DoubleAnimation(target, TimeSpan.FromSeconds(LerpSeconds))
                { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
            _fill.BeginAnimation(FrameworkElement.WidthProperty, lerp);
        }
    }
}
```

- [ ] **Step 2: Re-seat `TimerRowVisual` as a composition over the primitive**

Full replacement — behavior identical (drift logic, urgency styling, LATE text all preserved; anatomy delegated):

```csharp
// src/eq2auras.Plugin/Overlay/TimerRowVisual.cs
using System;
using System.Windows;
using System.Windows.Media;
using Eq2Auras.Core.Timers;

namespace Eq2Auras.Plugin.Overlay
{
    /// One retained list row: the shared bar primitive configured timer-style
    /// (spark on, wall-clock drain target), plus the timer-only urgency styling.
    /// Created once per timer and UPDATED across ticks — never rebuilt.
    internal sealed class TimerRowVisual
    {
        // Wall-clock targets keep drift ~0; only a genuine reset (new frame/instance)
        // should re-target the drain, so the tolerance is generous — in SECONDS.
        private const double DriftToleranceSeconds = 0.75;

        private readonly BarRowVisual _bar;
        private int _fillArgb = int.MinValue;
        private TimerUrgency _urgency = (TimerUrgency)(-1);

        public UIElement Root => _bar.Root;

        public TimerRowVisual(VisualStyle style)
        {
            _bar = new BarRowVisual(style, spark: true);
        }

        public void Update(TimerRow row)
        {
            _bar.NameText.Text = row.Name;
            // Wall-clock seconds so the text agrees with the smooth fill; overdue rows
            // (HighlightInPlace mode, linger-configured timers) count up as LATE.
            _bar.TrailingText.Text = row.Urgency == TimerUrgency.Overdue
                ? "LATE +" + (-row.TimeLeft) + "s"
                : (int)Math.Max(0, Math.Ceiling(row.PreciseTimeLeft)) + "s";

            if (row.Urgency != _urgency)
            {
                _urgency = row.Urgency;
                _bar.RootBorder.BorderBrush = new SolidColorBrush(OverlayTheme.AccentFor(row.Urgency));
                // Calm's accent is dark slate — invisible as text on the dark backplate.
                _bar.TrailingText.Foreground = new SolidColorBrush(row.Urgency == TimerUrgency.Calm
                    ? OverlayTheme.Text
                    : OverlayTheme.AccentFor(row.Urgency));
            }

            if (row.FillArgb != _fillArgb)
            {
                _fillArgb = row.FillArgb;
                _bar.SetFillColor(row.FillArgb);   // Core resolved it
            }

            if (row.TotalSeconds <= 0) return;
            double pxPerSecond = _bar.UsableWidth / row.TotalSeconds;
            double desired = Math.Max(0, Math.Min(1, row.PreciseTimeLeft / row.TotalSeconds)) * _bar.UsableWidth;
            double current = _bar.CurrentFillWidth;
            if (double.IsNaN(current) || Math.Abs(current - desired) > pxPerSecond * DriftToleranceSeconds)
            {
                _bar.AnimateDrain(desired, row.PreciseTimeLeft);
            }
        }
    }
}
```

*(Note: today's drain math uses `_rowWidth - 2` — `UsableWidth` is that expression moved, not changed.)*

- [ ] **Step 3: Commit**

```bash
git add src/eq2auras.Plugin/Overlay/BarRowVisual.cs src/eq2auras.Plugin/Overlay/TimerRowVisual.cs
git commit -m "Plugin: extract BarRowVisual (shared bar primitive; spark = row config); TimerRowVisual composes it"
```

- [ ] **Step 4: CI checkpoint — the extractions compile against real WPF/ACT**

```bash
git push -u origin parse-meter-slice1
gh run list --branch parse-meter-slice1 --limit 1   # grab the run id
gh run watch <id> --exit-status
```

Expected: verify-only CI green (Core tests + WPF plugin compile + artifact). If the XAML-root-inheritance compile fails here, fix before building the meter on top.

---

### Task 6: `MeterRowVisual` + `MeterWindow` (interactive, header, menu, lock)

**Files:**
- Create: `src/eq2auras.Plugin/Overlay/MeterRowVisual.cs`
- Create: `src/eq2auras.Plugin/Overlay/MeterWindow.cs`

**Interfaces:**
- Consumes: Task 4's `OverlayWindowBase`, Task 5's `BarRowVisual`, Task 2's `MeterFrame`/`MeterRow`, `MetricRegistry.All`, `VisualStyle`, `OverlayTheme`.
- Produces (Task 8 relies on): `MeterWindow` ctor `(double left, double top, VisualStyle style, string metricKey, bool locked, Action<double,double> persistPosition, Action<string> onMetricPicked, Action<bool> onLockChanged)`; `void Render(MeterFrame frame)` (dispatcher thread). Lock state is menu-owned: the window self-syncs on click and reports via `onLockChanged` — no external setter exists (YAGNI; slice 1 has no other lock surface). Scrolling is fully internal (`VisibleRows` slots + a transient wheel offset over the frame's full row list) — nothing for Task 8 to wire.

- [ ] **Step 1: The meter row visual**

```csharp
// src/eq2auras.Plugin/Overlay/MeterRowVisual.cs
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Eq2Auras.Core.Meter;

namespace Eq2Auras.Plugin.Overlay
{
    /// One slot of the meter's retained row pool: the shared bar primitive configured
    /// meter-style (no spark, data-driven lerp target) plus a dimmer percent block.
    /// Slot-keyed by design (SPEC Part III §Row animation): combatants re-bind to
    /// slots as sort order changes; the width convergence masks the rebind swap —
    /// row-reorder position animation is deliberately absent, not missing.
    internal sealed class MeterRowVisual
    {
        private const double FadeSeconds = 0.15;

        private readonly BarRowVisual _bar;
        private readonly TextBlock _percent;

        public UIElement Root => _bar.Root;

        public MeterRowVisual(VisualStyle style)
        {
            _bar = new BarRowVisual(style, spark: false);
            _bar.RootBorder.BorderBrush = new SolidColorBrush(OverlayTheme.CalmBorder);

            _percent = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromArgb(255, 0xC4, 0xCA, 0xD6)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0)
            };
            style.ApplyFont(_percent, style.RowText * 11.0 / 13.0);   // dimmer, slightly smaller
            _bar.TrailingPanel.Children.Add(_percent);
        }

        public void Update(MeterRow row)
        {
            _bar.NameText.Text = row.Name;
            _bar.TrailingText.Text = row.FormattedValue;
            _percent.Text = row.FormattedPercent;
            _bar.SetFillColor(row.FillArgb);
            _bar.AnimateToFraction(row.BarFraction);
        }

        public void FadeIn()
        {
            _bar.Root.Opacity = 0;
            _bar.Root.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(1, TimeSpan.FromSeconds(FadeSeconds)));
        }

        public void FadeOutAndRemove(Panel parent)
        {
            var fade = new DoubleAnimation(0, TimeSpan.FromSeconds(FadeSeconds));
            fade.Completed += (s, e) => parent.Children.Remove(_bar.Root);
            _bar.Root.BeginAnimation(UIElement.OpacityProperty, fade);
        }
    }
}
```

- [ ] **Step 2: The meter window (code-only, on the base)**

```csharp
// src/eq2auras.Plugin/Overlay/MeterWindow.cs
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Eq2Auras.Core.Config;
using Eq2Auras.Core.Meter;

namespace Eq2Auras.Plugin.Overlay
{
    /// The Parse Meter window (SPEC Part III §The meter window): INTERACTIVE — never
    /// click-through — with the header as the interaction surface: drag handle,
    /// state display ((duration) title — metric | total), and right-click menu
    /// (metric picker + lock). Lock freezes geometry only; content stays clickable.
    /// The mouse wheel scrolls the rank window (Details' model — no scrollbar chrome;
    /// works while locked: scrolling is content, not geometry). The timer module's
    /// move mode does not govern this window.
    public sealed class MeterWindow : OverlayWindowBase
    {
        public const int VisibleRows = 10;   // view constant: slot count; the frame always carries every ally
        private const double WindowSlack = 10;

        private readonly List<MeterRowVisual> _slots = new List<MeterRowVisual>();
        private MeterFrame _lastFrame;
        private int _scrollOffset;           // transient view state — never persisted, clamps to the data
        private readonly VisualStyle _style;
        private readonly Action<string> _onMetricPicked;
        private readonly Action<bool> _onLockChanged;
        private readonly StackPanel _rowsPanel;
        private readonly TextBlock _durationText;
        private readonly TextBlock _titleText;
        private readonly TextBlock _metricText;
        private readonly TextBlock _totalText;
        private readonly ContextMenu _menu;
        private string _metricKey;
        private bool _locked;

        public MeterWindow(double left, double top, VisualStyle style, string metricKey, bool locked,
            Action<double, double> persistPosition, Action<string> onMetricPicked, Action<bool> onLockChanged)
            : base(left, top, GrowDirection.Down, persistPosition, clickThroughBaseline: false)
        {
            _style = style;
            _metricKey = MetricRegistry.Resolve(metricKey).Key;   // normalize unknown -> default
            _locked = locked;
            _onMetricPicked = onMetricPicked;
            _onLockChanged = onLockChanged;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height;
            Width = style.RowWidth + WindowSlack;

            double hr = style.HeightRatio;
            _durationText = HeaderBlock(style, dim: true);
            _titleText = HeaderBlock(style, dim: false);
            _titleText.FontWeight = FontWeights.SemiBold;
            _titleText.TextTrimming = TextTrimming.CharacterEllipsis;
            _metricText = HeaderBlock(style, dim: true);
            _totalText = HeaderBlock(style, dim: false);
            _totalText.FontWeight = FontWeights.SemiBold;

            var leftPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            leftPanel.Children.Add(_durationText);
            leftPanel.Children.Add(_titleText);
            leftPanel.Children.Add(_metricText);

            var affordance = HeaderBlock(style, dim: true);
            affordance.Text = " ⋯";   // ⋯ — hints the right-click menu (SPEC Part III §Header)
            var rightPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            rightPanel.Children.Add(_totalText);
            rightPanel.Children.Add(affordance);

            var headerGrid = new Grid { Margin = new Thickness(8 * hr, 0, 8 * hr, 0) };
            headerGrid.Children.Add(leftPanel);
            headerGrid.Children.Add(rightPanel);

            var header = new Border
            {
                Height = style.RowHeight,
                Margin = new Thickness(0, 0, 0, style.RowSpacing),
                CornerRadius = new CornerRadius(4 * hr),
                // A real background — a transparent surface would be mouse-invisible,
                // and the header IS the drag/menu hit target.
                Background = new SolidColorBrush(Color.FromArgb(224, 18, 20, 26)),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(OverlayTheme.CalmBorder),
                Child = headerGrid
            };
            header.MouseLeftButtonDown += OnHeaderDrag;
            MouseWheel += OnScroll;   // window-wide: header and rows both scroll

            _menu = BuildMenu();
            SyncMenuChecks();             // AFTER the field assignment — the sync walks _menu.Items
            header.ContextMenu = _menu;   // WPF opens it on right-click

            _rowsPanel = new StackPanel();
            var root = new StackPanel { Width = style.RowWidth };
            root.Children.Add(header);
            root.Children.Add(_rowsPanel);
            Content = root;
        }

        private TextBlock HeaderBlock(VisualStyle style, bool dim)
        {
            var block = new TextBlock
            {
                Foreground = new SolidColorBrush(dim
                    ? Color.FromArgb(255, 0x8B, 0x93, 0xA3)
                    : OverlayTheme.Text),
                VerticalAlignment = VerticalAlignment.Center
            };
            style.ApplyFont(block, style.RowText);
            return block;
        }

        private ContextMenu BuildMenu()
        {
            var menu = new ContextMenu();
            foreach (var metric in MetricRegistry.All)
            {
                var item = new MenuItem { Header = metric.Label, Tag = metric.Key, IsCheckable = true };
                item.Click += (s, e) =>
                {
                    var key = (string)((MenuItem)s).Tag;
                    _metricKey = key;
                    SyncMenuChecks();
                    _onMetricPicked(key);
                };
                menu.Items.Add(item);
            }
            menu.Items.Add(new Separator());
            var lockItem = new MenuItem { Header = "Lock window", IsCheckable = true };
            lockItem.Click += (s, e) =>
            {
                _locked = ((MenuItem)s).IsChecked;
                _onLockChanged(_locked);
            };
            menu.Items.Add(lockItem);
            return menu;   // no sync here — _menu is still null until the ctor assigns it
        }

        private void SyncMenuChecks()
        {
            foreach (var entry in _menu.Items)
            {
                if (entry is MenuItem item && item.Tag is string key) item.IsChecked = key == _metricKey;
                else if (entry is MenuItem lockItem && lockItem.Tag == null) lockItem.IsChecked = _locked;
            }
        }

        private void OnHeaderDrag(object sender, MouseButtonEventArgs e)
        {
            if (_locked) return;   // lock freezes geometry ONLY — the menu keeps working
            BeginDragAndPersist();
        }

        /// One wheel notch = one rank. No lock check — scrolling is content
        /// interaction, same side of the lock axis as the menu (SPEC Part III).
        private void OnScroll(object sender, MouseWheelEventArgs e)
        {
            if (_lastFrame == null) return;
            _scrollOffset += e.Delta < 0 ? 1 : -1;
            RenderSlots();   // immediate re-bind from the retained frame — no waiting for the next poll
        }

        /// Called on the overlay's dispatcher thread with a fresh frame. Slot-keyed
        /// pool: visual i shows rank (_scrollOffset + i); grow with fade-in, shrink
        /// with fade-out; the offset clamps to the data on every render.
        public void Render(MeterFrame frame)
        {
            _lastFrame = frame;
            _durationText.Text = "(" + frame.DurationText + ") ";
            _titleText.Text = frame.Title;
            _metricText.Text = (frame.Title.Length > 0 ? " — " : "") + frame.MetricLabel;
            _totalText.Text = frame.TotalText;

            RenderSlots();
        }

        private void RenderSlots()
        {
            var rows = _lastFrame.Rows;
            int total = rows.Count;
            _scrollOffset = Math.Max(0, Math.Min(_scrollOffset, total - VisibleRows));   // <= 10 allies -> always 0
            int visible = Math.Min(VisibleRows, total);

            while (_slots.Count < visible)
            {
                var slot = new MeterRowVisual(_style);
                _slots.Add(slot);
                _rowsPanel.Children.Add(slot.Root);
                slot.FadeIn();
            }
            while (_slots.Count > visible)
            {
                var last = _slots[_slots.Count - 1];
                _slots.RemoveAt(_slots.Count - 1);
                last.FadeOutAndRemove(_rowsPanel);
            }

            for (int i = 0; i < visible; i++)
            {
                _slots[i].Update(rows[_scrollOffset + i]);
            }
        }
    }
}
```

*(`SyncMenuChecks` walks metric items by `Tag`; the lock item carries no Tag and syncs from `_locked` — the `else if` pattern above covers it.)*

- [ ] **Step 3: Commit**

```bash
git add src/eq2auras.Plugin/Overlay/MeterRowVisual.cs src/eq2auras.Plugin/Overlay/MeterWindow.cs
git commit -m "Plugin: MeterWindow (interactive header/menu/lock, wheel-scrolled rank window) + MeterRowVisual on the shared primitives"
```

---

### Task 7: `EncounterProbe` + the poll-tick divider hook

**Files:**
- Create: `src/eq2auras.Plugin/Act/EncounterProbe.cs`
- Modify: `src/eq2auras.Plugin/Act/TimerProbe.cs` (optional `onPollTick` callback)

**Interfaces:**
- Consumes: ACT (`ActGlobals.oFormActMain`: `AfterCombatActionDataLock`, `ActiveZone.ActiveEncounter`, `LastEstimatedTime`), Task 2's reading DTOs.
- Produces (Task 8 relies on): `EncounterProbe` ctor `(Func<bool> enabled, Action<EncounterReading, List<CombatantReading>> onSample)`; `void OnTick()` (call once per 100 ms poll tick; it self-divides). `TimerProbe` ctor gains a 4th parameter `Action onPollTick = null`, invoked at the end of every `OnPoll`.

- [ ] **Step 1: Hook the existing poll tick**

In `TimerProbe.cs`: add a field `private readonly Action _onPollTick;`, extend the ctor signature to

```csharp
        public TimerProbe(JsonlLogWriter log, Func<bool> debugLogging,
            Action<List<TimerReading>> onReadings, Action onPollTick = null)
```

assign `_onPollTick = onPollTick;`, and add as the **first line** of `OnPoll` (before the `GetTimerFrames()` try/catch):

```csharp
            _onPollTick?.Invoke();
```

First-line placement is deliberate: `OnPoll` has two early-outs (a throwing `GetTimerFrames()` and a null frame list), and the meter's sampling cadence must not be coupled to the timer read succeeding.

- [ ] **Step 2: The encounter adapter**

```csharp
// src/eq2auras.Plugin/Act/EncounterProbe.cs
using System;
using System.Collections.Generic;
using Advanced_Combat_Tracker;
using Eq2Auras.Core.Meter;

namespace Eq2Auras.Plugin.Act
{
    /// The encounter adapter (SPEC Part III §Assembly split & polling): samples ACT's
    /// computed combat model on a divider of the existing 100 ms poll tick — briefly
    /// under AfterCombatActionDataLock, snapshot into Core DTOs, release, hand off.
    /// Reads only the cheap shapes: per-combatant totals, the ally list, the live
    /// title, StartTime (live branch) / Duration (frozen branch). Never holds an
    /// EncounterData reference across ticks; never touches EncId/GetHashCode.
    public sealed class EncounterProbe
    {
        public const int SampleEveryNthTick = 3;   // 100 ms tick -> ~300 ms effective (SPEC: ~2-4 Hz)

        private readonly Func<bool> _enabled;
        private readonly Action<EncounterReading, List<CombatantReading>> _onSample;
        private int _tick;

        public EncounterProbe(Func<bool> enabled, Action<EncounterReading, List<CombatantReading>> onSample)
        {
            _enabled = enabled;
            _onSample = onSample;
        }

        /// Called once per TimerProbe poll tick, on ACT's UI thread.
        public void OnTick()
        {
            if (++_tick % SampleEveryNthTick != 0) return;
            if (!_enabled()) return;

            EncounterReading encounterReading;
            var allies = new List<CombatantReading>();
            try
            {
                var form = ActGlobals.oFormActMain;
                lock (form.AfterCombatActionDataLock)
                {
                    var encounter = form.ActiveZone?.ActiveEncounter;
                    if (encounter == null)
                    {
                        encounterReading = new EncounterReading { Exists = false };
                    }
                    else
                    {
                        bool active = encounter.Active;
                        encounterReading = new EncounterReading
                        {
                            Exists = true,
                            Active = active,
                            Title = encounter.GetStrongestEnemy(ActGlobals.charName),
                            // Degenerate pre-first-swing polls (StartTime == DateTime.MaxValue)
                            // produce a hugely negative estimate here — MeterEngine clamps.
                            LiveDurationSeconds = (form.LastEstimatedTime - encounter.StartTime).TotalSeconds,
                            FinalDurationSeconds = active ? 0 : encounter.Duration.TotalSeconds,
                        };

                        foreach (var ally in encounter.GetAllies())
                        {
                            allies.Add(new CombatantReading
                            {
                                Name = ally.Name,
                                Damage = ally.Damage,
                                Healed = ally.Healed,
                                CureDispels = ally.CureDispels,
                            });
                        }
                    }
                }
            }
            catch
            {
                return;   // same defensive stance as TimerProbe's GetTimerFrames read
            }

            _onSample(encounterReading, allies);   // outside the lock — hold it briefly
        }
    }
}
```

**Signature notes for the implementer** (CI is the arbiter — if it flags a mismatch, adapt the call site, not the design): `GetStrongestEnemy(string)` is the only overload — `ActGlobals.charName` is the argument ACT's own combat-end finalize passes, which is exactly the you-relative perspective the title wants; `EncounterData.Duration` is `TimeSpan` (hence `.TotalSeconds`); `CombatantData.Damage`/`Healed` convert implicitly to `long` whether the property is `long` or `Dnum`.

- [ ] **Step 3: Commit**

```bash
git add src/eq2auras.Plugin/Act/EncounterProbe.cs src/eq2auras.Plugin/Act/TimerProbe.cs
git commit -m "Plugin: EncounterProbe — locked snapshot of ACT's computed model on a poll-tick divider"
```

---

### Task 8: Host + tab wiring, teardown — then CI checkpoint

**Files:**
- Modify: `src/eq2auras.Plugin/Overlay/OverlayHost.cs` (meter window hosting)
- Modify: `src/eq2auras.Plugin/Eq2AurasPlugin.cs` (engine + probe wiring, tab checkbox, teardown)
- Modify: `src/eq2auras.Core/Timers/PaletteAssigner.cs` (doc comment only)

**Interfaces:**
- Consumes: everything above.
- Produces: `OverlayHost.SetMeterEnabled(bool)`, `OverlayHost.UpdateMeterFrame(MeterFrame)`.

- [ ] **Step 1: Generalize the `PaletteAssigner` doc comment** (it now serves two keyspaces; code unchanged):

Replace the class doc comment with:

```csharp
    /// Session-stable color identity: keyed by NORMALIZED NAME ONLY, first-fired
    /// order, stable for the plugin-instance lifetime. Two instances exist by design
    /// (SPEC §Timer colors; SPEC Part III §The meter window — Rows): the timer
    /// module's (timer names — the ability as players think of it) and the meter's
    /// (ally names). The namespaces are disjoint and the maps deliberately separate,
    /// so ally names never shift timer slot assignments.
```

- [ ] **Step 2: `OverlayHost` hosts the meter window**

Add field + using:

```csharp
using Eq2Auras.Core.Meter;   // top of file
...
        private MeterWindow _meterWindow;
```

In `Start()`'s thread body, after `_grid = new GridOverlayWindow();`:

```csharp
                if (_settings.Meter.Enabled) CreateMeterWindow();
```

Add the members (defaults follow the existing rough-placement stance — dragging is the real mechanism):

```csharp
        private void CreateMeterWindow()
        {
            var style = new VisualStyle();   // slice 1: baked defaults matching the timer look (SPEC Part III §Settings)
            var meter = _settings.Meter;
            _meterWindow = new MeterWindow(
                meter.Left ?? SystemParameters.PrimaryScreenWidth - style.RowWidth - 60,
                meter.Top ?? 320,
                style,
                meter.MetricKey,
                meter.Locked,
                (left, top) => SettingsStore.Update(_settings, () => { meter.Left = left; meter.Top = top; }),
                key => SettingsStore.Update(_settings, () => meter.MetricKey = key),
                locked => SettingsStore.Update(_settings, () => meter.Locked = locked));
            _meterWindow.Show();
        }

        /// Tab toggle, applied live. The meter window is NOT part of move mode:
        /// its interactivity makes a separate unlock unnecessary (SPEC Part III).
        public void SetMeterEnabled(bool enabled)
        {
            var dispatcher = _dispatcher;
            if (dispatcher == null) return;
            dispatcher.BeginInvoke((Action)(() =>
            {
                if (enabled && _meterWindow == null) CreateMeterWindow();
                else if (!enabled && _meterWindow != null)
                {
                    _meterWindow.Close();
                    _meterWindow = null;
                }
            }));
        }

        /// Callable from any thread (the sample runs on ACT's UI thread).
        public void UpdateMeterFrame(MeterFrame frame)
        {
            var dispatcher = _dispatcher;
            if (dispatcher == null) return;
            dispatcher.BeginInvoke((Action)(() => _meterWindow?.Render(frame)));
        }
```

In `Dispose()`, alongside the other window closes:

```csharp
                _meterWindow?.Close();
                _meterWindow = null;
```

`SetMoveMode` deliberately unchanged — it never touches the meter window.

- [ ] **Step 3: Wire the plugin**

In `Eq2AurasPlugin.cs`, add fields + using:

```csharp
using Eq2Auras.Core.Meter;   // top of file
...
        private MeterEngine _meterEngine;
        private EncounterProbe _encounterProbe;
```

In `InitPlugin`, replace the `_probe = new TimerProbe(...)` statement with (order matters — the encounter probe must exist before the timer probe's callback can reference it):

```csharp
            _meterEngine = new MeterEngine();
            _encounterProbe = new EncounterProbe(
                () => _settings.Meter.Enabled,
                (encounter, allies) => _overlay.UpdateMeterFrame(
                    _meterEngine.Tick(encounter, allies, _settings.Meter.MetricKey, _settings.PaletteArgb)));
            _probe = new TimerProbe(_log,
                () => _settings.DebugLogging,
                readings => _overlay.UpdateFrames(
                    _engine.Tick(readings)),
                onPollTick: () => _encounterProbe.OnTick());
```

In `BuildConfigTab`, after the `debugBox` block:

```csharp
            var meterBox = new CheckBox
            {
                Text = "Parse Meter (interactive DPS window)",
                Left = 10, Top = 730, Width = 280,
                Checked = _settings.Meter.Enabled
            };
            meterBox.CheckedChanged += (s, e) =>
            {
                SettingsStore.Update(_settings, () => _settings.Meter.Enabled = meterBox.Checked);
                _overlay.SetMeterEnabled(meterBox.Checked);
            };
```

and `tab.Controls.Add(meterBox);` with the other adds.

In `DeInitPlugin`, after `_probe = null;`:

```csharp
            _encounterProbe = null;   // no timers/subscriptions of its own — driven by the probe's tick
            _meterEngine = null;
```

(The meter window itself closes inside `_overlay.Dispose()` — teardown discipline holds.)

- [ ] **Step 4: Run Core tests one more time (regression)**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
Expected: PASS (all).

- [ ] **Step 5: Commit**

```bash
git add src/eq2auras.Plugin/Overlay/OverlayHost.cs src/eq2auras.Plugin/Eq2AurasPlugin.cs src/eq2auras.Core/Timers/PaletteAssigner.cs
git commit -m "Plugin: meter hosting + tab toggle + probe wiring; PaletteAssigner comment covers both keyspaces"
```

- [ ] **Step 6: CI checkpoint — full branch verify**

```bash
git push
gh run list --branch parse-meter-slice1 --limit 1
gh run watch <id> --exit-status
```

Expected: green (Core tests + WPF compile + artifact). Fix any ACT-signature mismatches surfaced by the compile (see Task 7's signature notes) and re-push until green.

---

### Task 9: Backlog link + present at the merge gate

**Files:**
- Modify: `docs/backlog.md` (in-flight entry gains the plan link)

- [ ] **Step 1:** In the backlog's IN FLIGHT entry, append to the first paragraph: `Plan: docs/plans/2026-07-15-parse-meter-slice1.md (third-party reviewed).`

- [ ] **Step 2: Commit + push**

```bash
git add docs/backlog.md
git commit -m "Backlog: link parse-meter slice-1 plan"
git push
```

- [ ] **Step 3:** Present the branch **ready-for-review** (never ready-to-merge): Alex reviews `git diff main..parse-meter-slice1`, runs the merge-gate live script below on the box, and owns the merge call.

---

## Merge-gate live script (for Alex, on the Windows box, after CI ships the branch artifact or post-merge dev build)

Concrete "do X, expect Y" — meter verification first, then the timer-regression pass (spec-review plan-watch item 3).

**Meter:**
1. Load the build → eq2auras tab → **"Parse Meter (interactive DPS window)" checkbox exists, unchecked** (default off). Check it → the meter window appears immediately (header only, no rows) without a plugin reload.
2. Hit a training dummy / any mob solo → within ~a second the window shows `(m:ss) <mob> — DPS | <total>` in the header and one row: your name, colored fill, abbreviated value, `100%`. The bar visibly lerps as damage lands (not instant snaps).
3. Right-click the header → menu shows **DPS / HPS / Cures / — / Lock window**, DPS checked. Pick **HPS** → header says HPS; heal yourself → a row with your healing rate. Pick **Cures**, cure something → integer count, no rate.
4. Drag the header → window moves. Right-click → **Lock window** → drag does nothing; **the right-click menu still opens** (lock freezes geometry only). Unlock → drag works again.
5. Reload the plugin (ACT plugin checkbox off/on) → window reappears at the dragged position, same metric selected, still locked/unlocked as left.
6. Let combat end → numbers freeze at final totals (the rate may step up slightly at the freeze — the finalized duration excludes trailing heals; **expected**, not a bug). Frozen totals stay while idle.
7. Zone → the frozen totals **remain** displayed; first fight in the new zone replaces them (SPEC Part III lifecycle).
8. `/act clear` (or the Clear All button) → the meter empties: no rows, header shows `(0:00) DPS | 0` (no title, so no ` — ` separator).
9. Group/raid opportunity: multiple allies → rows sort live, an overtake reads via the converging bar widths (no row sliding), ally colors stable across a re-pull. With **more than 10 allies**: exactly 10 rows show; mouse-wheel down walks deeper ranks and clamps at the bottom; wheel up returns and clamps at rank 1; **scrolling still works while locked**; percents and the header total are unchanged by scrolling (they cover the whole raid). (Tester note: the wheel only registers over hit-testable content — a pointer resting exactly in a transparent row-spacing gap scrolls nothing; that's standard WPF, not a broken build. Point at a row or the header.)
10. Uncheck the meter checkbox → window disappears live. Disable the plugin entirely → no leaked meter window.

**Timer-regression pass (the extractions must be invisible):**
11. Timers running (custom trigger or real): calm rows look **identical to the shipped build** — dark backplate, colored translucent fill draining smoothly, **spark edge riding the drain**, name left / countdown right, palette colors as before.
12. Escalation: warning threshold → center radial (or highlight-in-place per panel setting); LATE card on overdue; recast clears LATE instantly (newest-master rule intact).
13. Move mode: checkbox on → chrome + placement grid appear, drag both timer windows, positions persist, re-lock hides chrome and restores click-through. **The meter window shows no move chrome and ignores move mode.**
14. Grow direction: flip a list to Up → window stays put, grows upward, soonest row at the bottom edge; flip back.
15. Dimensions/font/spacing knobs: change row width/height + font → rows rebuild once and honor them, exactly as before.
16. Settings file: open `%APPDATA%\Advanced Combat Tracker\eq2auras\settings.json` → panel positions/knobs unchanged from before the update; a new `"meter"` section exists.

---

## Self-review notes (run before presenting)

- Spec coverage: Part III §data rule → Task 7; §segments/lifecycle → Tasks 3+7 (Exists/Active branches); §wall clock → Task 3; §registry/two-tier → Tasks 2–3; §window/header/menu/lock/scrolling → Task 6 (rank window: `VisibleRows` slots + wheel offset; frame carries every ally per Task 3); §row animation → Tasks 5–6; §shared substrate + both-brackets guardrail → Tasks 4–5; §assembly split/polling/settings/teardown → Tasks 1, 7, 8; §slice map deferred list → nothing here builds it (YAGNI held); plan-watch items 1–3 → Task 3 tests + merge-gate script.
- The `Settings.ToJson()` legacy-mirror path and `Normalize()` clamps are untouched — old settings files round-trip with the new `meter` section added.
- Plugin code cannot be tested on the Mac: the two CI checkpoints (Tasks 5, 8) are the compile gates; the merge-gate script is the behavior gate.
