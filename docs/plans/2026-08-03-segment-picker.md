# Segment Picker Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: this plan is executed **inline** in the writer session via `superpowers:executing-plans` (eq2auras convention — Alex watches; not subagent-driven). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let each meter window bind to a **segment** — Current, the current zone's Zonewide "All", or any past encounter — chosen from a header chip → flyout, with a `Return to Current` knob governing whether a non-Current pick yields to live combat.

**Architecture:** Core owns the whole *pure* segment model, ACT-free and Mac-tested: the mode enum, the two config fields, the flyout list-build, the selection/knob rules, and the **resolution keys + dedup + culled-fallback decision**. The Plugin is a thin ACT adapter: `SegmentResolver` turns Core keys into `EncounterData` (via `ActiveEncounter` / `ActiveZone.Items[0]` gated on `ZoneData.PopulateAll` / a `ZoneList` walk); `EncounterProbe` resolves the distinct requested segments **per poll under the lock** and emits **one snapshot per key** plus the current-zone key it used; `OverlayHost` maps each window to its snapshot by the same Core key, snaps non-pinned windows to Current on a new-combat edge, and falls a window whose historical segment was culled back to Current. The flyout + header chip are new WPF on the theme kit (a new `ThemeCheckbox`).

**Tech Stack:** C# — Core `netstandard2.0`, tested with **xUnit** in `tests/eq2auras.Core.Tests` (flat, no subfolders; classes are top-level, no namespace; method names `Sentence_case_with_underscores`), run on the Mac via `dotnet test`. Plugin `net472` + WPF, compiled into the single plugin DLL (CI-compile-gated + on-box live verification).

## Global Constraints

_Copied from SPEC.md; every task's requirements implicitly include these._

- **Single-assembly packaging** — Core sources are `<Compile Include>`d into the Plugin (`eq2auras.Plugin.csproj:30-35`). No second DLL; no non-GAC types in fields unless compiled in.
- **No `async` in the Plugin project.**
- **Never reference `System.Web.Extensions`** — JSON is `DataContractJsonSerializer` (DCJS).
- **DCJS skips field initializers on deserialize** → every enum/bool knob default must be the **0-value**; nullable numerics mean "unset, use default", never zero.
- **Never hold an `EncounterData` reference across polls** — re-resolve by handle each poll; degrade to empty/Current when gone.
- **All ACT reads happen briefly under `ActGlobals.oFormActMain.AfterCombatActionDataLock`** — snapshot into Core DTOs, release, render from the snapshot.
- **Core is ACT-free and Mac-testable** — no `Advanced_Combat_Tracker` types in Core. Plugin code is transcribe-only (CI-compile + on-box script), not TDD.
- **Wall clock owns visuals; the poll updates state only** (~300 ms sample).

---

## File Structure

**Core (new):** `Meter/SegmentMode.cs`, `Meter/SegmentSelection.cs`, `Meter/SegmentRules.cs`, `Meter/SegmentKeys.cs`, `Meter/SegmentListing.cs` (DTOs + `SegmentListBuilder`).
**Core (modified):** `Config/MeterWindowConfig.cs` (+2 fields), `Meter/MeterReading.cs` (+`SegmentSample`, +`SegmentSampleSet`).
**Plugin (new):** `Overlay/ThemeCheckbox.cs`, `Overlay/SegmentFlyout.cs`, `Act/SegmentResolver.cs`.
**Plugin (modified):** `Act/EncounterProbe.cs`, `Overlay/OverlayHost.cs`, `Overlay/MeterWindow.cs`, `Overlay/MeterWindowCallbacks.cs`, `Eq2AurasPlugin.cs`.

---

## Phase 1 — Core (strict TDD; Mac loop: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`)

### Task 1: `SegmentMode` enum + `MeterWindowConfig` fields (with DCJS/legacy/unknown coverage)

**Files:**
- Create: `src/eq2auras.Core/Meter/SegmentMode.cs`
- Modify: `src/eq2auras.Core/Config/MeterWindowConfig.cs` (add two `[DataMember]` fields after the `Scope` member, `MeterWindowConfig.cs:21-22`)
- Test: `tests/eq2auras.Core.Tests/SegmentConfigTests.cs`

**Interfaces:**
- Produces: `enum SegmentMode { Current = 0, Zonewide = 1 }`; `MeterWindowConfig.SegmentMode` (default `Current` at 0-value); `MeterWindowConfig.PinnedToSegment` (bool, default `false` = knob **on** / auto-return — the DCJS-inverted knob, SPEC §Settings).

- [ ] **Step 1: Write the failing tests** (cover the 0-value default, DCJS deserialize-with-field-absent → Current, an unknown newer-version value, and numeric round-trip — the spec's Core-TDD scope)

`tests/eq2auras.Core.Tests/SegmentConfigTests.cs`:
```csharp
using Eq2Auras.Core.Config;
using Eq2Auras.Core.Meter;
using Xunit;

public class SegmentConfigTests
{
    [Fact]
    public void Segment_mode_zero_value_is_current()
        => Assert.Equal(0, (int)SegmentMode.Current);

    [Fact]
    public void New_config_defaults_to_current_and_auto_return()
    {
        var c = new MeterWindowConfig();
        Assert.Equal(SegmentMode.Current, c.SegmentMode);
        Assert.False(c.PinnedToSegment);          // false = knob on (auto-return)
    }

    [Fact]
    public void A_window_with_no_segment_mode_defaults_to_current()
    {
        // DCJS skips the initializer on deserialize; an absent "segmentMode" -> 0-value Current.
        var json = "{\"meter\":{\"enabled\":true,\"windows\":[{\"metricKey\":\"encdps\"}]}}";
        var parsed = Settings.Parse(json);
        Assert.Equal(SegmentMode.Current, parsed.Meter.Windows[0].SegmentMode);
        Assert.False(parsed.Meter.Windows[0].PinnedToSegment);
    }

    [Fact]
    public void An_unknown_segment_mode_value_is_left_as_is_and_resolves_to_current()
    {
        // A newer version could write a value we don't know; SegmentRules.FromMode maps any
        // non-Zonewide value to Current (Task 3), so an unknown value degrades safely.
        var json = "{\"meter\":{\"enabled\":true,\"windows\":[{\"metricKey\":\"encdps\",\"segmentMode\":7}]}}";
        var parsed = Settings.Parse(json);
        Assert.Equal(SegmentKind.Current, SegmentRules.FromMode(parsed.Meter.Windows[0].SegmentMode).Kind);
    }

    [Fact]
    public void Zonewide_mode_and_pinned_round_trip_numerically()
    {
        var json = "{\"meter\":{\"enabled\":true,\"windows\":[{\"metricKey\":\"encdps\",\"segmentMode\":1,\"pinnedToSegment\":true}]}}";
        var parsed = Settings.Parse(json);
        Assert.Equal(SegmentMode.Zonewide, parsed.Meter.Windows[0].SegmentMode);
        Assert.True(parsed.Meter.Windows[0].PinnedToSegment);
        Assert.Contains("\"segmentMode\":1", parsed.ToJson());   // DCJS enum-as-number house style
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter SegmentConfigTests`
Expected: FAIL — types/members missing (some tests also depend on Task 3's `SegmentRules`/`SegmentKind`; that's fine — this file compiles once Task 3 lands; run this filter again after Task 3).

- [ ] **Step 3: Implement**

`src/eq2auras.Core/Meter/SegmentMode.cs`:
```csharp
namespace Eq2Auras.Core.Meter
{
    /// The persisted segment selection. Only the two live modes persist; a historical pick is
    /// runtime-only (SPEC §Segments Persistence). Current is the 0-value so a never-set /
    /// legacy window deserializes to it (DCJS rule).
    public enum SegmentMode { Current = 0, Zonewide = 1 }
}
```

In `MeterWindowConfig.cs`, after the `Scope` member (`:21-22`):
```csharp
        [DataMember(Name = "segmentMode")]
        public SegmentMode SegmentMode { get; set; } = SegmentMode.Current;

        // Inverted per the DCJS 0-value rule: false = knob ON (auto-return, the default);
        // true = pinned (the window stays on its selection). SPEC §Settings.
        [DataMember(Name = "pinnedToSegment")]
        public bool PinnedToSegment { get; set; }
```

- [ ] **Step 4: Run to verify pass** (after Task 3 exists, since two tests reference `SegmentRules`)

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter SegmentConfigTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/eq2auras.Core/Meter/SegmentMode.cs src/eq2auras.Core/Config/MeterWindowConfig.cs tests/eq2auras.Core.Tests/SegmentConfigTests.cs
git commit -m "segment: SegmentMode enum + MeterWindowConfig fields (default Current / auto-return, DCJS-covered)"
```

_(No `MeterSettings.Normalize()` change is needed — the 0-value default and forward-compat guard do the work; legacy windows carry no `segmentMode` and land on Current via the test above.)_

---

### Task 2: `SegmentListing` DTOs + `SegmentListBuilder`

**Files:**
- Create: `src/eq2auras.Core/Meter/SegmentListing.cs`
- Test: `tests/eq2auras.Core.Tests/SegmentListBuilderTests.cs`

**Interfaces:**
- Produces:
  - `enum EncounterOutcome { Unknown = 0, Win = 1, Partial = 2, Wipe = 3 }`
  - `sealed class SegmentEntry { string Title; double DurationSeconds; EncounterOutcome Outcome; bool IsAll; bool Available; string ZoneKey; long StartTicks; }` (`Available` = false only for an `All` entry in a zone without `PopulateAll` — rendered **disabled**, SPEC §Availability).
  - `sealed class ZoneGroup { string ZoneName; string ZoneKey; bool IsCurrent; long StartTicks; SegmentEntry All; List<SegmentEntry> Fights; }` (`All` is **always non-null** — either the real aggregate or a disabled placeholder).
  - `sealed class SegmentListing { bool ZonewideAvailable; List<ZoneGroup> Zones; }` (`ZonewideAvailable` = the **current** zone's `PopulateAll` — gates the top-level Zonewide chip entry).
  - `static class SegmentListBuilder`:
    - `RawEncounter { string Title; double DurationSeconds; int SuccessLevel; long StartTicks; bool IsAll; }`, `RawZone { string ZoneName; string ZoneKey; bool IsCurrent; long StartTicks; bool PopulateAll; List<RawEncounter> Encounters; }`.
    - `static SegmentListing Build(IEnumerable<RawZone> zones)`.
    - Rules: fights with `DurationSeconds <= 0` dropped; fights **newest-first** (`StartTicks` desc); every group carries an `All` (real when `PopulateAll`, else a disabled placeholder `{ Title="All", Available=false }`); **zones current-first, then newest-first** (`StartTicks` desc); a group with **no available `All` and no fights is dropped** (the ACT "Import" placeholder and all-zero zones); `ZonewideAvailable` = the current zone's `PopulateAll`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Collections.Generic;
using System.Linq;
using Eq2Auras.Core.Meter;
using Xunit;

public class SegmentListBuilderTests
{
    static SegmentListBuilder.RawEncounter Enc(string t, double d, int lvl, long ticks, bool all = false)
        => new SegmentListBuilder.RawEncounter { Title = t, DurationSeconds = d, SuccessLevel = lvl, StartTicks = ticks, IsAll = all };

    static SegmentListBuilder.RawZone Zone(string name, string key, bool current, bool populateAll, long ticks, params SegmentListBuilder.RawEncounter[] encs)
        => new SegmentListBuilder.RawZone { ZoneName = name, ZoneKey = key, IsCurrent = current, PopulateAll = populateAll, StartTicks = ticks, Encounters = encs.ToList() };

    [Fact]
    public void Drops_zero_duration_fights_orders_newest_first_and_all_leads()
    {
        var z = Zone("Nizara", "Nizara#1", current: true, populateAll: true, ticks: 500,
            Enc("All", 847, 0, 0, all: true), Enc("Meas", 134, 1, 200), Enc("a temple rat", 0, 3, 300), Enc("Trash", 38, 1, 100));
        var g = SegmentListBuilder.Build(new[] { z }).Zones.Single();
        Assert.True(g.All.Available);
        Assert.True(g.All.IsAll);
        Assert.Equal(EncounterOutcome.Unknown, g.All.Outcome);
        Assert.Equal(new[] { "Meas", "Trash" }, g.Fights.Select(f => f.Title).ToArray());
        Assert.Equal(EncounterOutcome.Win, g.Fights[0].Outcome);
    }

    [Fact]
    public void Zone_without_populate_all_carries_a_disabled_all_placeholder()
    {
        var g = SegmentListBuilder.Build(new[] {
            Zone("Antonica", "a#2", false, populateAll: false, ticks: 10, Enc("a stalker", 22, 1, 5)) }).Zones.Single();
        Assert.NotNull(g.All);
        Assert.False(g.All.Available);   // disabled placeholder, not omitted (SPEC §Availability)
        Assert.Single(g.Fights);
    }

    [Fact]
    public void Zonewide_available_reflects_the_current_zone_populate_all()
    {
        var currentOff = SegmentListBuilder.Build(new[] {
            Zone("Nizara", "n", current: true, populateAll: false, ticks: 2, Enc("y", 5, 1, 2)) });
        Assert.False(currentOff.ZonewideAvailable);
        var currentOn = SegmentListBuilder.Build(new[] {
            Zone("Nizara", "n", current: true, populateAll: true, ticks: 2, Enc("All", 5, 0, 0, all: true), Enc("y", 5, 1, 2)) });
        Assert.True(currentOn.ZonewideAvailable);
    }

    [Fact]
    public void Zones_ordered_current_first_then_newest_first()
    {
        var older = Zone("Antonica", "a", false, false, ticks: 100, Enc("x", 5, 1, 1));
        var newerNonCurrent = Zone("Zek", "z", false, false, ticks: 300, Enc("w", 5, 1, 1));
        var current = Zone("Nizara", "n", true, false, ticks: 200, Enc("y", 5, 1, 1));
        var zones = SegmentListBuilder.Build(new[] { older, newerNonCurrent, current }).Zones;
        Assert.True(zones[0].IsCurrent);                       // current first
        Assert.Equal(new[] { "Zek", "Antonica" }, zones.Skip(1).Select(z => z.ZoneName).ToArray());  // then newest-first
    }

    [Fact]
    public void Empty_group_with_no_available_all_and_no_fights_is_dropped()
    {
        var junk = Zone("zoneDataTerm-import", "imp", false, false, ticks: 1);   // no encounters
        var real = Zone("Nizara", "n", true, false, ticks: 2, Enc("y", 5, 1, 1));
        var zones = SegmentListBuilder.Build(new[] { junk, real }).Zones;
        Assert.Single(zones);
        Assert.Equal("Nizara", zones[0].ZoneName);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter SegmentListBuilderTests` → FAIL (types missing).

- [ ] **Step 3: Implement**

`src/eq2auras.Core/Meter/SegmentListing.cs`:
```csharp
using System.Collections.Generic;
using System.Linq;

namespace Eq2Auras.Core.Meter
{
    public enum EncounterOutcome { Unknown = 0, Win = 1, Partial = 2, Wipe = 3 }

    public sealed class SegmentEntry
    {
        public string Title { get; set; }
        public double DurationSeconds { get; set; }
        public EncounterOutcome Outcome { get; set; }
        public bool IsAll { get; set; }
        public bool Available { get; set; } = true;   // false only for a placeholder All in a PopulateAll-off zone
        public string ZoneKey { get; set; }
        public long StartTicks { get; set; }
    }

    public sealed class ZoneGroup
    {
        public string ZoneName { get; set; }
        public string ZoneKey { get; set; }
        public bool IsCurrent { get; set; }
        public long StartTicks { get; set; }
        public SegmentEntry All { get; set; }         // always non-null (real aggregate or disabled placeholder)
        public List<SegmentEntry> Fights { get; set; } = new List<SegmentEntry>();
    }

    public sealed class SegmentListing
    {
        public bool ZonewideAvailable { get; set; }   // the current zone's PopulateAll (gates the top-level Zonewide)
        public List<ZoneGroup> Zones { get; set; } = new List<ZoneGroup>();
    }

    public static class SegmentListBuilder
    {
        public sealed class RawEncounter
        {
            public string Title { get; set; }
            public double DurationSeconds { get; set; }
            public int SuccessLevel { get; set; }
            public long StartTicks { get; set; }
            public bool IsAll { get; set; }
        }

        public sealed class RawZone
        {
            public string ZoneName { get; set; }
            public string ZoneKey { get; set; }
            public bool IsCurrent { get; set; }
            public long StartTicks { get; set; }
            public bool PopulateAll { get; set; }
            public List<RawEncounter> Encounters { get; set; } = new List<RawEncounter>();
        }

        public static EncounterOutcome OutcomeOf(int successLevel)
        {
            switch (successLevel) { case 1: return EncounterOutcome.Win; case 2: return EncounterOutcome.Partial; case 3: return EncounterOutcome.Wipe; default: return EncounterOutcome.Unknown; }
        }

        public static SegmentListing Build(IEnumerable<RawZone> zones)
        {
            var listing = new SegmentListing();
            var ordered = zones.OrderByDescending(z => z.IsCurrent).ThenByDescending(z => z.StartTicks);

            foreach (var z in ordered)
            {
                var group = new ZoneGroup { ZoneName = z.ZoneName, ZoneKey = z.ZoneKey, IsCurrent = z.IsCurrent, StartTicks = z.StartTicks };

                var rawAll = z.Encounters.FirstOrDefault(e => e.IsAll);
                group.All = (z.PopulateAll && rawAll != null)
                    ? Entry(rawAll, z.ZoneKey, isAll: true, available: true)
                    : new SegmentEntry { Title = "All", IsAll = true, Available = false, Outcome = EncounterOutcome.Unknown, ZoneKey = z.ZoneKey };

                group.Fights = z.Encounters
                    .Where(e => !e.IsAll && e.DurationSeconds > 0)
                    .OrderByDescending(e => e.StartTicks)
                    .Select(e => Entry(e, z.ZoneKey, isAll: false, available: true))
                    .ToList();

                if (!group.All.Available && group.Fights.Count == 0) continue;   // drop empty/junk groups
                listing.Zones.Add(group);

                if (z.IsCurrent) listing.ZonewideAvailable = z.PopulateAll;
            }
            return listing;
        }

        static SegmentEntry Entry(RawEncounter e, string zoneKey, bool isAll, bool available) => new SegmentEntry
        {
            Title = e.Title, DurationSeconds = e.DurationSeconds,
            Outcome = isAll ? EncounterOutcome.Unknown : OutcomeOf(e.SuccessLevel),
            IsAll = isAll, Available = available, ZoneKey = zoneKey, StartTicks = e.StartTicks,
        };
    }
}
```

- [ ] **Step 4: Run to verify pass** → PASS.
- [ ] **Step 5: Commit**

```bash
git add src/eq2auras.Core/Meter/SegmentListing.cs tests/eq2auras.Core.Tests/SegmentListBuilderTests.cs
git commit -m "segment: SegmentListBuilder — All-always (disabled placeholder), current-then-newest zones, drop empty, drop 0:00"
```

---

### Task 3: `SegmentSelection` descriptor + `SegmentRules`

**Files:**
- Create: `src/eq2auras.Core/Meter/SegmentSelection.cs`, `src/eq2auras.Core/Meter/SegmentRules.cs`
- Test: `tests/eq2auras.Core.Tests/SegmentRulesTests.cs`

**Interfaces:**
- Produces: `enum SegmentKind { Current = 0, Zonewide, Historical }`; `sealed class SegmentSelection` (value-equality; factories `Current()`/`Zonewide()`/`Historical(zoneKey, startTicks)`; fields `Kind`, `ZoneKey`, `StartTicks`); `static SegmentRules` — `FromMode(SegmentMode)`, `ClearsKnobOnPick(SegmentSelection)`, `OnNewCombat(SegmentSelection current, bool pinned)`.

- [ ] **Step 1: Write the failing tests**

```csharp
using Eq2Auras.Core.Meter;
using Xunit;

public class SegmentRulesTests
{
    [Fact]
    public void New_combat_snaps_non_pinned_non_current_to_current()
        => Assert.Equal(SegmentKind.Current, SegmentRules.OnNewCombat(SegmentSelection.Zonewide(), pinned: false).Kind);

    [Fact]
    public void New_combat_leaves_a_pinned_selection_unchanged()
    {
        var r = SegmentRules.OnNewCombat(SegmentSelection.Historical("Nizara#1", 123), pinned: true);
        Assert.Equal(SegmentKind.Historical, r.Kind);
        Assert.Equal(123, r.StartTicks);
    }

    [Fact]
    public void New_combat_on_current_is_a_no_op()
        => Assert.Equal(SegmentKind.Current, SegmentRules.OnNewCombat(SegmentSelection.Current(), pinned: false).Kind);

    [Fact]
    public void Picking_zonewide_clears_the_knob_others_do_not()
    {
        Assert.True(SegmentRules.ClearsKnobOnPick(SegmentSelection.Zonewide()));
        Assert.False(SegmentRules.ClearsKnobOnPick(SegmentSelection.Current()));
        Assert.False(SegmentRules.ClearsKnobOnPick(SegmentSelection.Historical("z", 1)));
    }

    [Fact]
    public void From_mode_maps_persisted_mode_to_selection()
    {
        Assert.Equal(SegmentKind.Current, SegmentRules.FromMode(SegmentMode.Current).Kind);
        Assert.Equal(SegmentKind.Zonewide, SegmentRules.FromMode(SegmentMode.Zonewide).Kind);
    }

    [Fact]
    public void Selections_have_value_equality()
    {
        Assert.Equal(SegmentSelection.Historical("z", 5), SegmentSelection.Historical("z", 5));
        Assert.NotEqual(SegmentSelection.Historical("z", 5), SegmentSelection.Historical("z", 6));
    }
}
```

- [ ] **Step 2: Run to verify failure** → FAIL.

- [ ] **Step 3: Implement**

`src/eq2auras.Core/Meter/SegmentSelection.cs`:
```csharp
using System;

namespace Eq2Auras.Core.Meter
{
    public enum SegmentKind { Current = 0, Zonewide, Historical }

    public sealed class SegmentSelection : IEquatable<SegmentSelection>
    {
        public SegmentKind Kind { get; }
        public string ZoneKey { get; }
        public long StartTicks { get; }

        private SegmentSelection(SegmentKind kind, string zoneKey, long startTicks)
        { Kind = kind; ZoneKey = zoneKey; StartTicks = startTicks; }

        public static SegmentSelection Current() => new SegmentSelection(SegmentKind.Current, null, 0);
        public static SegmentSelection Zonewide() => new SegmentSelection(SegmentKind.Zonewide, null, 0);
        public static SegmentSelection Historical(string zoneKey, long startTicks) => new SegmentSelection(SegmentKind.Historical, zoneKey, startTicks);

        public bool Equals(SegmentSelection o) => o != null && Kind == o.Kind && ZoneKey == o.ZoneKey && StartTicks == o.StartTicks;
        public override bool Equals(object obj) => Equals(obj as SegmentSelection);
        public override int GetHashCode() => ((int)Kind * 397) ^ ((ZoneKey?.GetHashCode() ?? 0) * 31) ^ StartTicks.GetHashCode();
    }
}
```

`src/eq2auras.Core/Meter/SegmentRules.cs`:
```csharp
namespace Eq2Auras.Core.Meter
{
    public static class SegmentRules
    {
        public static SegmentSelection FromMode(SegmentMode mode)
            => mode == SegmentMode.Zonewide ? SegmentSelection.Zonewide() : SegmentSelection.Current();

        public static bool ClearsKnobOnPick(SegmentSelection pick)
            => pick != null && pick.Kind == SegmentKind.Zonewide;

        public static SegmentSelection OnNewCombat(SegmentSelection current, bool pinned)
        {
            if (pinned || current == null || current.Kind == SegmentKind.Current) return current ?? SegmentSelection.Current();
            return SegmentSelection.Current();
        }
    }
}
```

- [ ] **Step 4: Run to verify pass** → PASS. Then re-run `--filter SegmentConfigTests` (Task 1) — now green.
- [ ] **Step 5: Commit**

```bash
git add src/eq2auras.Core/Meter/SegmentSelection.cs src/eq2auras.Core/Meter/SegmentRules.cs tests/eq2auras.Core.Tests/SegmentRulesTests.cs
git commit -m "segment: SegmentSelection + SegmentRules (return-to-current, Zonewide pins, mode map)"
```

---

### Task 4: `SegmentKeys` — pure resolution keys, dedup, and the culled-fallback decision

_This is the Core-testable resolution the spec's §Assembly split / §Testing assign to Core: the pure key computation, the per-distinct-segment collapse, and the "a historical handle that did not resolve falls back to Current" decision. Turning a key into an ACT `EncounterData` is the Plugin's adapter (Task 5)._

**Files:**
- Create: `src/eq2auras.Core/Meter/SegmentKeys.cs`
- Test: `tests/eq2auras.Core.Tests/SegmentKeysTests.cs`

**Interfaces:**
- Produces `static class SegmentKeys`:
  - `static string Of(SegmentSelection sel, string currentZoneKey)` — `"C"` for Current; `"Z:" + currentZoneKey` for Zonewide; `"H:" + sel.ZoneKey + ":" + sel.StartTicks` for Historical.
  - `static List<string> Distinct(IEnumerable<SegmentSelection> selections, string currentZoneKey)` — the dedup: distinct `Of(...)` keys, **always including `"C"`** (the host always needs Current for fallback), Current appearing once.
  - `static SegmentSelection FallbackOnMissing(SegmentSelection sel, bool resolved)` — `sel` when `resolved` **or** `sel.Kind != Historical`; else `SegmentSelection.Current()` (a culled historical handle → Current; Current/Zonewide unavailability is handled as a sample flag, not a selection change).

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Linq;
using Eq2Auras.Core.Meter;
using Xunit;

public class SegmentKeysTests
{
    [Fact]
    public void Key_encodes_kind_and_uses_current_zone_for_zonewide()
    {
        Assert.Equal("C", SegmentKeys.Of(SegmentSelection.Current(), "Nizara#1"));
        Assert.Equal("Z:Nizara#1", SegmentKeys.Of(SegmentSelection.Zonewide(), "Nizara#1"));
        Assert.Equal("H:Antonica#2:55", SegmentKeys.Of(SegmentSelection.Historical("Antonica#2", 55), "Nizara#1"));
    }

    [Fact]
    public void Distinct_collapses_all_current_windows_to_one_and_always_includes_current()
    {
        var keys = SegmentKeys.Distinct(new[] { SegmentSelection.Current(), SegmentSelection.Current() }, "n");
        Assert.Equal(new[] { "C" }, keys);
    }

    [Fact]
    public void Distinct_splits_when_selections_differ_and_still_includes_current()
    {
        var keys = SegmentKeys.Distinct(new[] { SegmentSelection.Zonewide(), SegmentSelection.Historical("a", 3) }, "n");
        Assert.Contains("C", keys);              // added for fallback even though no window asked for it
        Assert.Contains("Z:n", keys);
        Assert.Contains("H:a:3", keys);
        Assert.Equal(3, keys.Count);
    }

    [Fact]
    public void Fallback_sends_a_culled_historical_to_current_but_leaves_resolved_and_non_historical_alone()
    {
        var h = SegmentSelection.Historical("a", 3);
        Assert.Equal(SegmentKind.Current, SegmentKeys.FallbackOnMissing(h, resolved: false).Kind);
        Assert.Equal(SegmentKind.Historical, SegmentKeys.FallbackOnMissing(h, resolved: true).Kind);
        Assert.Equal(SegmentKind.Zonewide, SegmentKeys.FallbackOnMissing(SegmentSelection.Zonewide(), resolved: false).Kind);
    }
}
```

- [ ] **Step 2: Run to verify failure** → FAIL.

- [ ] **Step 3: Implement**

```csharp
using System.Collections.Generic;
using System.Linq;

namespace Eq2Auras.Core.Meter
{
    /// The pure part of segment resolution: keys, the per-distinct-segment collapse, and the
    /// culled->Current decision. The Plugin's SegmentResolver turns a key into an EncounterData.
    public static class SegmentKeys
    {
        public static string Of(SegmentSelection sel, string currentZoneKey)
        {
            switch (sel.Kind)
            {
                case SegmentKind.Zonewide: return "Z:" + currentZoneKey;
                case SegmentKind.Historical: return "H:" + sel.ZoneKey + ":" + sel.StartTicks;
                default: return "C";
            }
        }

        public static List<string> Distinct(IEnumerable<SegmentSelection> selections, string currentZoneKey)
        {
            var keys = new List<string> { "C" };   // always resolve Current (fallback target)
            foreach (var s in selections)
            {
                var k = Of(s, currentZoneKey);
                if (!keys.Contains(k)) keys.Add(k);
            }
            return keys;
        }

        public static SegmentSelection FallbackOnMissing(SegmentSelection sel, bool resolved)
            => (resolved || sel.Kind != SegmentKind.Historical) ? sel : SegmentSelection.Current();
    }
}
```

- [ ] **Step 4: Run to verify pass** → PASS.
- [ ] **Step 5: Commit + full Core suite**

```bash
git add src/eq2auras.Core/Meter/SegmentKeys.cs tests/eq2auras.Core.Tests/SegmentKeysTests.cs
git commit -m "segment: SegmentKeys — pure resolution keys, always-Current dedup, culled->Current fallback"
dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj   # all green
```

---

## Phase 2 — Plugin (transcribe-only; verify by CI compile + the on-box live script)

> Each task ends by pushing for **verify-only CI** (`gh run watch <id> --exit-status`). Behavioral correctness is Alex's on-box gate (script at the end). Commits stage **explicit paths** (repo staging discipline — never `-A`/`.`).

### Task 5: `SegmentSample` DTO + `SegmentResolver` adapter + per-distinct-segment probe/host

**Files:**
- Modify: `src/eq2auras.Core/Meter/MeterReading.cs` (add `SegmentSample`)
- Create: `src/eq2auras.Plugin/Act/SegmentResolver.cs`
- Modify: `src/eq2auras.Plugin/Act/EncounterProbe.cs`, `src/eq2auras.Plugin/Overlay/OverlayHost.cs`, `src/eq2auras.Plugin/Eq2AurasPlugin.cs`

**Interfaces & contract:**
- Core `sealed class SegmentSample { string Key; EncounterReading Encounter; List<CombatantReading> Combatants; List<DeathRecord> Deaths; bool Unavailable; }` — `Unavailable` = Zonewide with `PopulateAll` off (Task 11 renders it). A **missing** historical key simply produces **no sample** for that key (the host detects the miss → Task 6 fallback).
- `SegmentResolver` (Plugin, ACT):
  - `string ZoneKey(ZoneData z)` = `z.ZoneName + "#" + z.StartTime.Ticks`.
  - `string CurrentZoneKey(FormActMain form)` = `ZoneKey(form.ActiveZone)` (called **once, under the lock**, and returned to the host so its window→key lookups use the *same* snapshot — closes the F3 race).
  - `EncounterData ResolveByKey(FormActMain form, string key, out bool unavailable)` — `"C"` → `ActiveEncounter`; `"Z:..."` → `ActiveZone.PopulateAll && Items.Count>0 ? Items[0] : (null, unavailable:true)`; `"H:zoneKey:ticks"` → the `ZoneList` zone whose `ZoneKey==zoneKey` then its encounter whose `StartTimes[0].Ticks==ticks`, else `(null, unavailable:false)` (culled).
- **Probe contract change:** `_onSample` becomes `Action<SegmentSampleSet, List<BreakdownReading>, List<RecapReading>>` where `SegmentSampleSet { string CurrentZoneKey; List<SegmentSample> Samples; }`. The host supplies `Func<IReadOnlyList<SegmentSelection>>` (each window's live selection; Task 9). Each poll, under the lock: read `CurrentZoneKey`; `SegmentKeys.Distinct(selections, currentZoneKey)`; for each key resolve + `ReadSegment(enc, key, unavailable)` (the extracted single-encounter read block, `EncounterProbe.cs:56-99` + deaths capture keyed per-segment); emit the set.
- **Host fan-out:** map each window→sample by `SegmentKeys.Of(window.CurrentSelection, set.CurrentZoneKey)`. If the key has **no sample** (culled historical) → use the `"C"` sample and mark the window for fallback (Task 6). Feed `MeterEngine.Tick`/`DeathsEngine.BuildList` from the sample's `Encounter`/`Combatants`/`Deaths` — signatures unchanged.

- [ ] **Step 1:** Add `SegmentSample` + `SegmentSampleSet` to `MeterReading.cs`.
- [ ] **Step 2:** Write `SegmentResolver` (`ZoneKey`/`CurrentZoneKey`/`ResolveByKey`).
- [ ] **Step 3:** Refactor `EncounterProbe.OnTick`: extract `ReadSegment(FormActMain, EncounterData, string key, bool unavailable) → SegmentSample` from the current read block; loop over `SegmentKeys.Distinct`; make `_deathStore`/`_deathsSeen` `Dictionary<string,…>` keyed by segment key; change `_onSample` to the new delegate; emit `SegmentSampleSet`.
- [ ] **Step 4:** `OverlayHost.UpdateMeterSample(SegmentSampleSet set, List<BreakdownReading>, List<RecapReading>)` — index `set.Samples` by `Key`; in the fan-out use `SegmentKeys.Of(config-or-window selection, set.CurrentZoneKey)`; fall to `"C"` on a miss. Add `IReadOnlyList<SegmentSelection> CurrentSegmentRequests()` returning `SegmentRules.FromMode(config.SegmentMode)` per window (Task 9 upgrades to the live selection).
- [ ] **Step 5:** Update `Eq2AurasPlugin.cs:43-47` wiring (pass `CurrentSegmentRequests`, the new `_onSample`).
- [ ] **Step 6:** Push, watch verify-only CI.

```bash
git add src/eq2auras.Core/Meter/MeterReading.cs src/eq2auras.Plugin/Act/SegmentResolver.cs src/eq2auras.Plugin/Act/EncounterProbe.cs src/eq2auras.Plugin/Overlay/OverlayHost.cs src/eq2auras.Plugin/Eq2AurasPlugin.cs
git commit -m "segment: SegmentSample + SegmentResolver + per-distinct-segment probe/host (list follows segment)"
git push -u origin segment-picker && gh run watch <id> --exit-status
```

---

### Task 6: New-combat snap + culled-historical fallback (the selection lifecycle)

**Files:**
- Modify: `src/eq2auras.Plugin/Overlay/OverlayHost.cs`, `src/eq2auras.Plugin/Overlay/MeterWindow.cs`

**Design (wires the two Core rules the reviewer flagged as produced-but-unconsumed):**
- **New-combat edge:** the host tracks the last-seen Current encounter identity (`set.Samples["C"].Encounter` + its start-ticks, exposed as a field on the sample). When it changes to a *new active* encounter (`Active` true and a start-ticks change), for each window with `!config.PinnedToSegment`, call `window.ApplySelection(SegmentRules.OnNewCombat(window.CurrentSelection, pinned:false))` (snaps a non-Current selection to Current + updates the chip) and rebuild the segment-request set.
- **Culled fallback:** when the host's fan-out finds no sample for a window's historical key (Task 5 Step 4), call `window.ApplySelection(SegmentKeys.FallbackOnMissing(window.CurrentSelection, resolved:false))` → Current, chip updates, requests rebuilt. Mirrors the drill's auto-exit.
- `MeterWindow.ApplySelection(SegmentSelection)` sets `_selection`, updates the chip label (Task 9), and invokes the `SegmentChanged` callback so the host rebuilds requests. Add `SegmentSelection CurrentSelection { get; }`.

- [ ] **Step 1:** Add `MeterWindow.CurrentSelection` + `ApplySelection` (chip update stubbed until Task 9; selection state + callback live now).
- [ ] **Step 2:** Host: new-combat edge detector + per-window snap; culled-miss → fallback. Expose the Current encounter start-ticks on `SegmentSample` (add `long EncounterStartTicks`).
- [ ] **Step 3:** Push, watch verify-only CI.

```bash
git add src/eq2auras.Plugin/Overlay/OverlayHost.cs src/eq2auras.Plugin/Overlay/MeterWindow.cs src/eq2auras.Core/Meter/MeterReading.cs
git commit -m "segment: new-combat snap-to-current + culled-historical fallback (selection lifecycle)"
git push && gh run watch <id> --exit-status
```

---

### Task 7: Deep reads (drill / hover / Deaths) follow the window's segment

**Files:** Modify `src/eq2auras.Core/Meter/Breakdown.cs` (`DrillRequest` +`string SegmentKey`), `EncounterProbe.cs`, `OverlayHost.cs`, `MeterWindow.cs`.

- [ ] **Step 1:** `DrillRequest.SegmentKey` set in `MeterWindow.DrillTarget`/`HoverTarget` from `SegmentKeys.Of(_selection, <current zone key>)` (the window is given a `Func<string> currentZoneKey` from the host's last `set.CurrentZoneKey`). Deaths already flow from the window's sample (Task 5). 
- [ ] **Step 2:** In `EncounterProbe`, keep a `Dictionary<string, EncounterData>` of this-poll resolved encounters (built in Task 5's loop, under the lock) and service each drill/hover request against `bySegKey[request.SegmentKey]` instead of `ActiveEncounter`; the recap read likewise.
- [ ] **Step 3:** Push, watch verify-only CI.

```bash
git add src/eq2auras.Core/Meter/Breakdown.cs src/eq2auras.Plugin/Act/EncounterProbe.cs src/eq2auras.Plugin/Overlay/OverlayHost.cs src/eq2auras.Plugin/Overlay/MeterWindow.cs
git commit -m "segment: drill/hover/Deaths deep reads target the window's resolved segment"
git push && gh run watch <id> --exit-status
```

---

### Task 8: The flyout enumeration read (`ZoneList` → `SegmentListing`)

**Files:** Modify `src/eq2auras.Plugin/Act/SegmentResolver.cs` (+`Enumerate`).

- [ ] **Step 1:** `SegmentResolver.Enumerate(FormActMain form) → SegmentListing` — on flyout open, under the lock, per `ZoneData` a `RawZone { ZoneName, ZoneKey=ZoneKey(z), IsCurrent=ReferenceEquals(z,form.ActiveZone), StartTicks=z.StartTime.Ticks, PopulateAll=z.PopulateAll, Encounters }`; per `EncounterData` at index `i`: `RawEncounter { Title = string.IsNullOrEmpty(e.Title) ? "Encounter" : e.Title, DurationSeconds = e.Duration.TotalSeconds, SuccessLevel = e.GetEncounterSuccessLevel(), StartTicks = e.StartTimes.Count>0 ? e.StartTimes[0].Ticks : 0, IsAll = z.PopulateAll && i==0 }`; feed `SegmentListBuilder.Build`. **On-open only.** (Ordering/filter/availability/empty-drop live in the tested builder.)
- [ ] **Step 2:** Push, watch verify-only CI.

```bash
git add src/eq2auras.Plugin/Act/SegmentResolver.cs
git commit -m "segment: on-open ZoneList enumeration -> SegmentListing"
git push && gh run watch <id> --exit-status
```

---

### Task 9: `ThemeCheckbox` primitive

**Files:** Create `src/eq2auras.Plugin/Overlay/ThemeCheckbox.cs`.

- [ ] **Step 1:** `ThemeCheckbox : Border` — `ctor(string label, bool initial)`; `event Action<bool> Toggled`; `bool Checked { get; set; }`. A 13px box (`Theme.Divider` border, `Theme.AccentAmber` `✓` when checked, `Theme.Surface(0x0D)` fill) + `Theme.TextLabel` text; toggles on left-button-up. Mirror `MetricGridItem.cs` structure/state idiom.
- [ ] **Step 2:** Push, watch verify-only CI (compiles; unused until Task 10).

```bash
git add src/eq2auras.Plugin/Overlay/ThemeCheckbox.cs
git commit -m "theme: add ThemeCheckbox kit primitive"
git push && gh run watch <id> --exit-status
```

---

### Task 10: Header segment chip + the flyout UI + selection/knob wiring

**Files:** Create `src/eq2auras.Plugin/Overlay/SegmentFlyout.cs`; modify `MeterWindow.cs`, `MeterWindowCallbacks.cs`, `OverlayHost.cs`.

**Design:**
- **Chip:** a compact `Border` added as a new `Auto` column in the header grid between `_metricText` and `_affordance` (`MeterWindow.cs:145-154`), text = the current selection's label (`Current` / `Zonewide` / a past fight title), ellipsis-trimmed; left-click opens `SegmentFlyout`; `Collapsed` while `_drilledCombatant != null`. `Render` sets its label from `_selection` (Task 6 already updates `_selection`).
- **`SegmentFlyout`** (a `Popup` like `MeterPopup`): the theme popup panel → **Current** and **Zonewide** list-items on top (Zonewide rendered **disabled/greyed and non-interactive** when `listing.ZonewideAvailable` is false — SPEC §Availability), then a `ScrollViewer` (MaxHeight) of per-zone collapsible groups (a `ZoneGroup` header row toggles its body; current zone expanded), each group's `All` (**disabled** when `entry.Available` is false) then its fights (title + duration + a win/partial/wipe dot from `EncounterOutcome`), and a footer `ThemeCheckbox` "Return to Current when a fight starts" bound to `!config.PinnedToSegment`.
- **Picking** calls `window.ApplySelection(pick)`; the flyout applies `SegmentRules.ClearsKnobOnPick` → on a Zonewide pick, set the checkbox unchecked + `PinnedChanged(true)`. Persist: `Current`/`Zonewide` picks → `SegmentModeChanged(mode)`; historical picks are runtime-only. The checkbox drives `PinnedChanged`.
- **Callbacks** (`MeterWindowCallbacks` +): `Action<SegmentMode> SegmentModeChanged`, `Action<bool> PinnedChanged`, `Func<SegmentListing> EnumerateSegments`, `Action SegmentChanged` (host rebuilds requests, like `DrillChanged`). `OverlayHost.AddMeterWindow` wires them to `SettingsStore.Update(...)` (persist `SegmentMode`/`PinnedToSegment`) and `EnumerateSegments = () => SegmentResolver.Enumerate(ActGlobals.oFormActMain)`. `CurrentSegmentRequests()` now reads each `window.CurrentSelection`.

- [ ] **Step 1:** `SegmentFlyout` (transcribe against `MeterPopup.cs` + `ThemeCheckbox`; disabled rendering for unavailable Zonewide/`All`).
- [ ] **Step 2:** Chip + label render + open-handler + hide-while-drilled in `MeterWindow`; finish `ApplySelection`'s chip update.
- [ ] **Step 3:** Callbacks + `EnumerateSegments` + persistence + `CurrentSegmentRequests` reads live selection, in `OverlayHost`/`MeterWindowCallbacks`.
- [ ] **Step 4:** Push, watch verify-only CI.

```bash
git add src/eq2auras.Plugin/Overlay/SegmentFlyout.cs src/eq2auras.Plugin/Overlay/MeterWindow.cs src/eq2auras.Plugin/Overlay/MeterWindowCallbacks.cs src/eq2auras.Plugin/Overlay/OverlayHost.cs
git commit -m "segment: header chip + flyout (disabled-when-unavailable) + selection/knob persistence"
git push && gh run watch <id> --exit-status
```

---

### Task 11: Zonewide-unavailable dormant body

**Files:** Modify `MeterWindow.cs`, `OverlayHost.cs`.

- [ ] **Step 1:** When the window's resolved `SegmentSample.Unavailable` is true (persisted-Zonewide window in a `PopulateAll`-off zone), render the **cleared-primary dormant body** (backdrop, no rows) + a single one-line hint `TextBlock` ("Enable ACT's \"Zone All listing\" for Zonewide"); header keeps duration + chip + cog.
- [ ] **Step 2:** Push, watch verify-only CI.

```bash
git add src/eq2auras.Plugin/Overlay/MeterWindow.cs src/eq2auras.Plugin/Overlay/OverlayHost.cs
git commit -m "segment: Zonewide-unavailable dormant body with a one-line hint"
git push && gh run watch <id> --exit-status
```

---

## On-box live verification (Alex's merge gate — concrete "do X, expect Y")

1. **Chip + flyout:** header shows a `Current` chip; click → flyout with **Current** and **Zonewide** on top, then zone groups (current expanded, older collapsed), each its `All` then fights as **title + duration** with **win/partial/wipe** dots; **no `0:00` fights**; no empty/`import` groups.
2. **History peek + drill:** pick a past fight → the window shows it; drill an ally → **that fight's** abilities. With **`Return to Current` on** (default), a new pull **snaps to Current**. Toggle it **off**, pick the fight → it **stays** through the next pull.
3. **Zonewide follow + auto-pin:** pick **Zonewide** → the box **auto-unchecks**; the window shows the current zone's All and **updates live**; **zone into a new area** → the **new** zone's All shows with no action. A specific zone's group **`All`** picked from the flyout **stays** on that zone after you leave.
4. **Availability (disabled, not vanished):** turn ACT's **"Zone All listing" off** → in the flyout the top-level **Zonewide** and the group **`All`** rows render **disabled/greyed and non-clickable** (they do **not** disappear); a window persisted on Zonewide shows the **unavailable** body + hint. Turn it back on → they re-enable.
5. **Persistence:** set a window to **Zonewide** (pinned), reload → reopens on **Zonewide**; a window **peeking a past fight** reopens on **Current**.
6. **Culled fallback:** in a long session that culls old fights, a window parked (knob off) on a fight that ACT culls **falls back to Current** on its own (chip returns to `Current`).
7. **Multi-window / cost:** a **Current** DPS + a **Zonewide** HPS side by side both update live (two segments/poll); three Current windows share one snapshot (no stutter).
8. **Regression:** all-Current windows unchanged; drill/hover/Deaths on Current unchanged; **timer overlay** unaffected (light sanity check).

## Plan-watch items (from the spec review)

1. **`PopulateAll` detection** — grounded and reviewer-decompile-confirmed: `ZoneData.PopulateAll` public, per-zone construction-stamped; `Items[0]` is the "All" only when set. Field-confirm via live step 4.
2. **`ZoneList`/handle re-resolution + culling** — `ZoneName + StartTime.Ticks` zone key + `StartTimes[0].Ticks` encounter handle re-resolve each poll; culled → Current (live step 6).
3. **Same-named zone revisits** — reviewer-confirmed from the decompile: ACT appends a **new** `ZoneData` per visit (fresh `StartTime`), so the zone key disambiguates two same-named visits; live step 3 exercises a re-zone.
4. **Per-distinct-segment snapshot cost** — bounded by window count; no per-poll stutter with mixed segments (live step 7).

---

## Execution

Executed **inline** in this session via `superpowers:executing-plans` (Core tasks strict-TDD on the Mac loop; Plugin tasks transcribe-only + verify-CI + the on-box script). Presents **ready-for-review**; the reviewer's plan-review loop and Alex's merge gate follow.
