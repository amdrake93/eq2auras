# Segment Picker Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: this plan is executed **inline** in the writer session via `superpowers:executing-plans` (eq2auras convention — Alex watches; not subagent-driven). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let each meter window bind to a **segment** — Current, the current zone's Zonewide "All", or any past encounter — chosen from a header chip → flyout, with a `Return to Current` knob governing whether a non-Current pick yields to live combat.

**Architecture:** Core gains a pure segment model (mode enum, selection descriptor, the list-build + knob rules) and two `MeterWindowConfig` fields; it stays ACT-free and Mac-testable. The Plugin's `EncounterProbe` resolves each window's selection to an `EncounterData` **per poll** (never caching the ref) and emits **one snapshot per distinct segment**; `OverlayHost` feeds each window the snapshot for *its* segment, so `MeterEngine.Tick`/`DeathsEngine` and every deep read run unchanged against the window's segment handle. The flyout + header chip are new WPF on the theme kit (a new `ThemeCheckbox` primitive included).

**Tech Stack:** C# — Core `netstandard2.0` (xUnit-style tests via the existing `eq2auras.Core.Tests` project, run on the Mac); Plugin `net472` + WPF (compiled into the single plugin DLL, CI-compile-gated, on-box live verification).

## Global Constraints

_Copied from SPEC.md; every task's requirements implicitly include these._

- **Single-assembly packaging** — Core sources are `<Compile Include>`d into the Plugin. No second DLL; no non-GAC types in fields unless compiled in.
- **No `async` in the Plugin project** (ACT's pre-`InitPlugin` type scan resolves all field types, including async state machines).
- **Never reference `System.Web.Extensions`** — JSON is `DataContractJsonSerializer` (DCJS).
- **DCJS skips field initializers on deserialize** → every enum/bool knob default must be the **0-value**; nullable numeric fields mean "unset, use default", never zero.
- **Never hold an `EncounterData` reference across polls** — ACT culls at every combat end; re-resolve by handle each poll, degrade to empty/Current when gone.
- **All ACT reads happen briefly under `ActGlobals.oFormActMain.AfterCombatActionDataLock`** — snapshot into Core DTOs, release, render only from the snapshot.
- **Core is Mac-testable and ACT-free** — no `Advanced_Combat_Tracker` types in Core; the ACT binary is not on the Mac. Plugin code is transcribe-only here (CI-compile-gated + on-box live script), not TDD.
- **Wall clock owns visuals; the poll updates state only.** Meter samples on the existing 100 ms tick at a divider (~300 ms).

---

## File Structure

**Core (new):**
- `src/eq2auras.Core/Meter/SegmentMode.cs` — the persisted enum (`Current = 0`, `Zonewide`).
- `src/eq2auras.Core/Meter/SegmentSelection.cs` — the runtime descriptor (`SegmentKind` + a pure historical handle: zone key + start-ticks). ACT-free.
- `src/eq2auras.Core/Meter/SegmentListing.cs` — flyout DTOs (`SegmentListing`, `ZoneGroup`, `SegmentEntry`, `EncounterOutcome` enum) + `SegmentListBuilder` (group/order/filter/outcome-map).
- `src/eq2auras.Core/Meter/SegmentRules.cs` — pure state rules (return-to-current transition; Zonewide-pick pins; forward-compat mode resolution).

**Core (modified):**
- `src/eq2auras.Core/Config/MeterWindowConfig.cs` — +`SegmentMode`, +`PinnedToSegment`.
- `src/eq2auras.Core/Meter/MeterReading.cs` — +`SegmentSample` DTO.

**Plugin (new):**
- `src/eq2auras.Plugin/Overlay/ThemeCheckbox.cs` — the kit's checkbox primitive.
- `src/eq2auras.Plugin/Overlay/SegmentFlyout.cs` — the chip's flyout (scrolling, per-zone-collapsible list + the knob).
- `src/eq2auras.Plugin/Act/SegmentResolver.cs` — `SegmentSelection` → `EncounterData` (via `ActiveEncounter` / `ActiveZone.Items[0]` gated on `PopulateAll` / a `ZoneList` walk); and the `ZoneList` → `SegmentListing` enumeration read.

**Plugin (modified):**
- `src/eq2auras.Plugin/Act/EncounterProbe.cs` — per-distinct-segment resolution + snapshot; callback → `List<SegmentSample>`.
- `src/eq2auras.Plugin/Overlay/OverlayHost.cs` — fan each window its segment's sample; collect per-window segment requests.
- `src/eq2auras.Plugin/Overlay/MeterWindow.cs` — segment state + header chip + selection callbacks + unavailable body.
- `src/eq2auras.Plugin/Overlay/MeterWindowCallbacks.cs` — +segment callbacks.
- `src/eq2auras.Plugin/Eq2AurasPlugin.cs` — updated probe→host wiring.

---

## Phase 1 — Core (strict TDD, Mac loop: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`)

### Task 1: `SegmentMode` enum + `MeterWindowConfig` fields + migration

**Files:**
- Create: `src/eq2auras.Core/Meter/SegmentMode.cs`
- Modify: `src/eq2auras.Core/Config/MeterWindowConfig.cs` (add two `[DataMember]` fields after `Scope`, `MeterWindowConfig.cs:20`)
- Modify: `src/eq2auras.Core/Config/MeterSettings.cs` (`Normalize()` seeds/clamps — no new clamp needed, but a legacy window must resolve to `Current`)
- Test: `tests/eq2auras.Core.Tests/SegmentConfigTests.cs`

**Interfaces:**
- Produces: `enum SegmentMode { Current = 0, Zonewide = 1 }`; `MeterWindowConfig.SegmentMode` (default `Current` at 0-value); `MeterWindowConfig.PinnedToSegment` (bool, default `false` = knob **on** / auto-return, per the DCJS inversion — SPEC §Settings).

- [ ] **Step 1: Write the failing test**

```csharp
using Eq2Auras.Core.Config;
using Eq2Auras.Core.Meter;
using Xunit;

public class SegmentConfigTests
{
    [Fact]
    public void DefaultsAreCurrentAndAutoReturn()
    {
        var c = new MeterWindowConfig();
        Assert.Equal(SegmentMode.Current, c.SegmentMode);   // 0-value
        Assert.False(c.PinnedToSegment);                    // 0-value = knob on (auto-return)
    }

    [Fact]
    public void SegmentModeZeroValueIsCurrent()
    {
        Assert.Equal(0, (int)SegmentMode.Current);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter SegmentConfigTests`
Expected: FAIL — `SegmentMode` / `PinnedToSegment` do not exist.

- [ ] **Step 3: Write minimal implementation**

`src/eq2auras.Core/Meter/SegmentMode.cs`:
```csharp
namespace Eq2Auras.Core.Meter
{
    /// The persisted segment selection. Only the two live modes persist; a historical
    /// pick is runtime-only (SPEC §Segments Persistence). Current is the 0-value default
    /// so a never-set window deserializes to it (DCJS rule).
    public enum SegmentMode
    {
        Current = 0,
        Zonewide = 1,
    }
}
```

In `MeterWindowConfig.cs`, after the `Scope` member (`:20`):
```csharp
        [DataMember(Name = "segmentMode")]
        public SegmentMode SegmentMode { get; set; } = SegmentMode.Current;

        // Inverted per the DCJS 0-value rule: false = knob ON (auto-return, the default);
        // true = pinned (window stays on its selection). SPEC §Settings.
        [DataMember(Name = "pinnedToSegment")]
        public bool PinnedToSegment { get; set; }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter SegmentConfigTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/eq2auras.Core/Meter/SegmentMode.cs src/eq2auras.Core/Config/MeterWindowConfig.cs tests/eq2auras.Core.Tests/SegmentConfigTests.cs
git commit -m "segment: SegmentMode enum + MeterWindowConfig fields (default Current / auto-return)"
```

---

### Task 2: `SegmentListing` DTOs + `SegmentListBuilder`

**Files:**
- Create: `src/eq2auras.Core/Meter/SegmentListing.cs`
- Test: `tests/eq2auras.Core.Tests/SegmentListBuilderTests.cs`

**Interfaces:**
- Consumes: nothing (pure — the Plugin feeds it already-snapshotted rows).
- Produces:
  - `enum EncounterOutcome { Unknown = 0, Win = 1, Partial = 2, Wipe = 3 }` (mirrors ACT's `GetEncounterSuccessLevel()` 1/2/3; 0 = unknown/aggregate).
  - `sealed class SegmentEntry { string Title; double DurationSeconds; EncounterOutcome Outcome; bool IsAll; string ZoneKey; long StartTicks; }`
  - `sealed class ZoneGroup { string ZoneName; string ZoneKey; bool IsCurrent; SegmentEntry All; List<SegmentEntry> Fights; }` (`All` null when the zone has no `PopulateAll`).
  - `sealed class SegmentListing { List<ZoneGroup> Zones; }`
  - `static class SegmentListBuilder`:
    - `static SegmentListing Build(IEnumerable<RawZone> zones)` where `RawZone`/`RawEncounter` are plain input DTOs the Plugin fills from ACT.
    - Rules: fights with `DurationSeconds <= 0` are **dropped**; fights ordered **newest-first** (by `StartTicks` desc); the zone's `All` (if present) leads; zones ordered current-first then newest-first; a group `All` entry carries `Outcome = Unknown` (no single success level) and `IsAll = true`.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Collections.Generic;
using System.Linq;
using Eq2Auras.Core.Meter;
using Xunit;

public class SegmentListBuilderTests
{
    static SegmentListBuilder.RawEncounter Enc(string title, double dur, int lvl, long ticks, bool all = false)
        => new SegmentListBuilder.RawEncounter { Title = title, DurationSeconds = dur, SuccessLevel = lvl, StartTicks = ticks, IsAll = all };

    [Fact]
    public void DropsZeroDurationFights_OrdersNewestFirst_AllLeads()
    {
        var zone = new SegmentListBuilder.RawZone
        {
            ZoneName = "Nizara", ZoneKey = "Nizara#1", IsCurrent = true, PopulateAll = true,
            Encounters = new List<SegmentListBuilder.RawEncounter>
            {
                Enc("All", 847, 0, 0, all: true),
                Enc("Meas", 134, 1, 200),
                Enc("a temple rat", 0, 3, 300),   // zero-duration -> dropped
                Enc("Trash", 38, 1, 100),
            }
        };
        var listing = SegmentListBuilder.Build(new[] { zone });
        var g = listing.Zones.Single();
        Assert.True(g.IsCurrent);
        Assert.NotNull(g.All);
        Assert.True(g.All.IsAll);
        Assert.Equal(EncounterOutcome.Unknown, g.All.Outcome);   // aggregate has no single outcome
        Assert.Equal(new[] { "Meas", "Trash" }, g.Fights.Select(f => f.Title).ToArray());  // newest-first, rat dropped
        Assert.Equal(EncounterOutcome.Win, g.Fights[0].Outcome);
    }

    [Fact]
    public void ZoneWithoutPopulateAll_HasNoAll()
    {
        var zone = new SegmentListBuilder.RawZone
        {
            ZoneName = "Antonica", ZoneKey = "Antonica#2", IsCurrent = false, PopulateAll = false,
            Encounters = new List<SegmentListBuilder.RawEncounter> { Enc("a stalker", 22, 1, 10) }
        };
        var g = SegmentListBuilder.Build(new[] { zone }).Zones.Single();
        Assert.Null(g.All);
        Assert.Single(g.Fights);
    }

    [Fact]
    public void ZonesOrderedCurrentFirst()
    {
        var older = new SegmentListBuilder.RawZone { ZoneName = "Antonica", ZoneKey = "a", IsCurrent = false, PopulateAll = false, Encounters = new List<SegmentListBuilder.RawEncounter> { Enc("x", 5, 1, 1) } };
        var current = new SegmentListBuilder.RawZone { ZoneName = "Nizara", ZoneKey = "n", IsCurrent = true, PopulateAll = false, Encounters = new List<SegmentListBuilder.RawEncounter> { Enc("y", 5, 1, 2) } };
        var listing = SegmentListBuilder.Build(new[] { older, current });
        Assert.True(listing.Zones[0].IsCurrent);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter SegmentListBuilderTests`
Expected: FAIL — types missing.

- [ ] **Step 3: Write minimal implementation**

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
        public string ZoneKey { get; set; }
        public long StartTicks { get; set; }
    }

    public sealed class ZoneGroup
    {
        public string ZoneName { get; set; }
        public string ZoneKey { get; set; }
        public bool IsCurrent { get; set; }
        public SegmentEntry All { get; set; }              // null when the zone has no PopulateAll
        public List<SegmentEntry> Fights { get; set; } = new List<SegmentEntry>();
    }

    public sealed class SegmentListing
    {
        public List<ZoneGroup> Zones { get; set; } = new List<ZoneGroup>();
    }

    /// Pure: turns the Plugin's ACT-snapshotted zone/encounter rows into the flyout listing.
    /// Zero-duration fights dropped; fights newest-first; the zone All leads; zones current-first.
    public static class SegmentListBuilder
    {
        public sealed class RawEncounter
        {
            public string Title { get; set; }
            public double DurationSeconds { get; set; }
            public int SuccessLevel { get; set; }     // ACT GetEncounterSuccessLevel(): 1/2/3, 0 unknown
            public long StartTicks { get; set; }
            public bool IsAll { get; set; }
        }

        public sealed class RawZone
        {
            public string ZoneName { get; set; }
            public string ZoneKey { get; set; }
            public bool IsCurrent { get; set; }
            public bool PopulateAll { get; set; }
            public List<RawEncounter> Encounters { get; set; } = new List<RawEncounter>();
        }

        public static EncounterOutcome OutcomeOf(int successLevel)
        {
            switch (successLevel)
            {
                case 1: return EncounterOutcome.Win;
                case 2: return EncounterOutcome.Partial;
                case 3: return EncounterOutcome.Wipe;
                default: return EncounterOutcome.Unknown;
            }
        }

        public static SegmentListing Build(IEnumerable<RawZone> zones)
        {
            var listing = new SegmentListing();

            foreach (var z in zones.OrderByDescending(z => z.IsCurrent))
            {
                var group = new ZoneGroup
                {
                    ZoneName = z.ZoneName,
                    ZoneKey = z.ZoneKey,
                    IsCurrent = z.IsCurrent,
                };

                foreach (var e in z.Encounters)
                {
                    if (e.IsAll)
                    {
                        if (z.PopulateAll)
                            group.All = Entry(e, z.ZoneKey, isAll: true);
                        continue;
                    }
                    if (e.DurationSeconds <= 0) continue;   // drop degenerate fights
                    group.Fights.Add(Entry(e, z.ZoneKey, isAll: false));
                }

                group.Fights = group.Fights.OrderByDescending(f => f.StartTicks).ToList();
                listing.Zones.Add(group);
            }

            return listing;
        }

        static SegmentEntry Entry(RawEncounter e, string zoneKey, bool isAll) => new SegmentEntry
        {
            Title = e.Title,
            DurationSeconds = e.DurationSeconds,
            Outcome = isAll ? EncounterOutcome.Unknown : OutcomeOf(e.SuccessLevel),
            IsAll = isAll,
            ZoneKey = zoneKey,
            StartTicks = e.StartTicks,
        };
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter SegmentListBuilderTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/eq2auras.Core/Meter/SegmentListing.cs tests/eq2auras.Core.Tests/SegmentListBuilderTests.cs
git commit -m "segment: SegmentListBuilder — group by zone, All-first, newest-first, drop 0:00, outcome map"
```

---

### Task 3: `SegmentSelection` descriptor + `SegmentRules` (knob + pick transitions)

**Files:**
- Create: `src/eq2auras.Core/Meter/SegmentSelection.cs`
- Create: `src/eq2auras.Core/Meter/SegmentRules.cs`
- Test: `tests/eq2auras.Core.Tests/SegmentRulesTests.cs`

**Interfaces:**
- Produces:
  - `enum SegmentKind { Current = 0, Zonewide, Historical }`
  - `sealed class SegmentSelection { SegmentKind Kind; string ZoneKey; long StartTicks; }` (+ `static SegmentSelection Current()`, `Zonewide()`, `Historical(zoneKey, startTicks)`; value-equality by kind+key+ticks).
  - `static class SegmentRules`:
    - `static SegmentSelection FromMode(SegmentMode mode)` — the persisted mode → the live selection a reload opens on (`Current`/`Zonewide`).
    - `static bool ClearsKnobOnPick(SegmentSelection pick)` — true iff `pick.Kind == Zonewide` (SPEC: picking Zonewide sets `PinnedToSegment = true`).
    - `static SegmentSelection OnNewCombat(SegmentSelection current, bool pinned)` — returns `Current()` when `!pinned && current.Kind != Current`, else `current` unchanged (the return-to-current rule; uniform, Zonewide not exempt).

- [ ] **Step 1: Write the failing test**

```csharp
using Eq2Auras.Core.Meter;
using Xunit;

public class SegmentRulesTests
{
    [Fact]
    public void NewCombat_SnapsNonPinnedNonCurrentToCurrent()
    {
        var zw = SegmentSelection.Zonewide();
        Assert.Equal(SegmentKind.Current, SegmentRules.OnNewCombat(zw, pinned: false).Kind);
    }

    [Fact]
    public void NewCombat_PinnedStays()
    {
        var hist = SegmentSelection.Historical("Nizara#1", 123);
        var r = SegmentRules.OnNewCombat(hist, pinned: true);
        Assert.Equal(SegmentKind.Historical, r.Kind);
        Assert.Equal(123, r.StartTicks);
    }

    [Fact]
    public void NewCombat_CurrentIsNoOp()
    {
        Assert.Equal(SegmentKind.Current, SegmentRules.OnNewCombat(SegmentSelection.Current(), pinned: false).Kind);
    }

    [Fact]
    public void PickingZonewideClearsTheKnob_OthersDoNot()
    {
        Assert.True(SegmentRules.ClearsKnobOnPick(SegmentSelection.Zonewide()));
        Assert.False(SegmentRules.ClearsKnobOnPick(SegmentSelection.Current()));
        Assert.False(SegmentRules.ClearsKnobOnPick(SegmentSelection.Historical("z", 1)));
    }

    [Fact]
    public void FromMode_MapsPersistedModeToSelection()
    {
        Assert.Equal(SegmentKind.Current, SegmentRules.FromMode(SegmentMode.Current).Kind);
        Assert.Equal(SegmentKind.Zonewide, SegmentRules.FromMode(SegmentMode.Zonewide).Kind);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter SegmentRulesTests`
Expected: FAIL — types missing.

- [ ] **Step 3: Write minimal implementation**

`src/eq2auras.Core/Meter/SegmentSelection.cs`:
```csharp
using System;

namespace Eq2Auras.Core.Meter
{
    public enum SegmentKind { Current = 0, Zonewide, Historical }

    /// A pure, ACT-free descriptor of what a window is showing. Current/Zonewide persist
    /// (via SegmentMode); a Historical pick is runtime-only, keyed by a session-stable
    /// handle (zone key + the encounter's start-ticks) re-resolved each poll.
    public sealed class SegmentSelection : IEquatable<SegmentSelection>
    {
        public SegmentKind Kind { get; }
        public string ZoneKey { get; }
        public long StartTicks { get; }

        private SegmentSelection(SegmentKind kind, string zoneKey, long startTicks)
        {
            Kind = kind; ZoneKey = zoneKey; StartTicks = startTicks;
        }

        public static SegmentSelection Current() => new SegmentSelection(SegmentKind.Current, null, 0);
        public static SegmentSelection Zonewide() => new SegmentSelection(SegmentKind.Zonewide, null, 0);
        public static SegmentSelection Historical(string zoneKey, long startTicks)
            => new SegmentSelection(SegmentKind.Historical, zoneKey, startTicks);

        public bool Equals(SegmentSelection other)
            => other != null && Kind == other.Kind && ZoneKey == other.ZoneKey && StartTicks == other.StartTicks;
        public override bool Equals(object obj) => Equals(obj as SegmentSelection);
        public override int GetHashCode()
            => ((int)Kind * 397) ^ ((ZoneKey?.GetHashCode() ?? 0) * 31) ^ StartTicks.GetHashCode();
    }
}
```

`src/eq2auras.Core/Meter/SegmentRules.cs`:
```csharp
namespace Eq2Auras.Core.Meter
{
    /// Pure selection/knob transitions (SPEC §Segments — "Selection is a live choice plus a
    /// behavior knob"). The Plugin holds the live selection and calls these on the poll edges.
    public static class SegmentRules
    {
        public static SegmentSelection FromMode(SegmentMode mode)
            => mode == SegmentMode.Zonewide ? SegmentSelection.Zonewide() : SegmentSelection.Current();

        /// Picking Zonewide pins the window in one gesture (PinnedToSegment = true).
        public static bool ClearsKnobOnPick(SegmentSelection pick)
            => pick != null && pick.Kind == SegmentKind.Zonewide;

        /// On a new-combat transition: a non-pinned, non-Current selection snaps to Current;
        /// pinned stays; Current is a no-op. Uniform — Zonewide is not exempt.
        public static SegmentSelection OnNewCombat(SegmentSelection current, bool pinned)
        {
            if (pinned) return current;
            if (current == null || current.Kind == SegmentKind.Current) return current ?? SegmentSelection.Current();
            return SegmentSelection.Current();
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter SegmentRulesTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/eq2auras.Core/Meter/SegmentSelection.cs src/eq2auras.Core/Meter/SegmentRules.cs tests/eq2auras.Core.Tests/SegmentRulesTests.cs
git commit -m "segment: SegmentSelection descriptor + SegmentRules (return-to-current, Zonewide pins, mode map)"
```

- [ ] **Step 6: Run the full Core suite (no regressions)**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
Expected: all green (prior count + the new segment tests).

---

## Phase 2 — Plugin (transcribe-only; verify by CI compile + the on-box live script)

> These tasks touch WPF/ACT and cannot run on the Mac. Each ends by pushing the branch for **verify-only CI** (Core tests + the WPF plugin compile + artifact; publish skipped on a branch — `gh run watch <id> --exit-status`). Behavioral correctness is Alex's on-box gate via the live script at the end.

### Task 4: `SegmentSample` DTO + per-distinct-segment resolution in `EncounterProbe`

**Files:**
- Modify: `src/eq2auras.Core/Meter/MeterReading.cs` (add `SegmentSample`)
- Create: `src/eq2auras.Plugin/Act/SegmentResolver.cs`
- Modify: `src/eq2auras.Plugin/Act/EncounterProbe.cs` (`OnTick` — resolve per segment; callback shape)
- Modify: `src/eq2auras.Plugin/Overlay/OverlayHost.cs` (`UpdateMeterSample` + a `CurrentSegmentRequests()` provider)
- Modify: `src/eq2auras.Plugin/Eq2AurasPlugin.cs` (probe ctor wiring)

**Interfaces:**
- Consumes: `SegmentSelection` (Task 3), `EncounterReading`/`CombatantReading`/`DeathRecord` (existing).
- Produces:
  - Core `sealed class SegmentSample { string Key; EncounterReading Encounter; List<CombatantReading> Combatants; List<DeathRecord> Deaths; }` (`Key` = the segment's stable string id — see below).
  - `SegmentResolver.Resolve(FormActMain form, SegmentSelection sel) → (EncounterData enc, bool unavailable)`:
    - `Current` → `form.ActiveZone?.ActiveEncounter`.
    - `Zonewide` → `form.ActiveZone?.PopulateAll == true ? form.ActiveZone.Items[0] : (null, unavailable: true)`.
    - `Historical(zoneKey, ticks)` → walk `form.ZoneList` for the `ZoneData` whose `ZoneKey(zone) == zoneKey`, then its `EncounterData` whose `StartTimes[0].Ticks == ticks`; **not found → (null, unavailable:false)** (culled → caller falls back to Current).
  - `SegmentResolver.ZoneKey(ZoneData z) → string` = `z.ZoneName + "#" + z.StartTime.Ticks` (a session-stable per-visit key; **plan-watch: same-named-zone revisits** — confirm ACT appends a new `ZoneData` per visit so `StartTime` disambiguates).
  - `SegmentSelection.Key()` string used as the `SegmentSample.Key` and the dedup key: `"C"` / `"Z:" + currentZoneKey` / `"H:" + zoneKey + ":" + ticks`.

**Design (the refactor):** `OnTick` currently reads one `ActiveEncounter`. Generalize: the host provides, per poll, the **set of `(SegmentSelection)` requested by all windows** (via `CurrentSegmentRequests()`); the probe **dedups by `Key()`**, resolves each once under the lock, runs the existing read block against that `EncounterData`, and emits `List<SegmentSample>`. Deep-read requests (Task 5) carry their segment key so they attach to the right sample. `MeterEngine`/`DeathsEngine` are unchanged — each window just receives its segment's `EncounterReading`+`Combatants`.

- [ ] **Step 1: Add the `SegmentSample` DTO**

In `MeterReading.cs`:
```csharp
public sealed class SegmentSample
{
    public string Key { get; set; }
    public EncounterReading Encounter { get; set; }
    public System.Collections.Generic.List<CombatantReading> Combatants { get; set; }
    public System.Collections.Generic.List<DeathRecord> Deaths { get; set; }
    public bool Unavailable { get; set; }   // Zonewide with PopulateAll off
}
```

- [ ] **Step 2: Write `SegmentResolver`**

`src/eq2auras.Plugin/Act/SegmentResolver.cs` (references `Advanced_Combat_Tracker` — Plugin-only):
```csharp
using System.Linq;
using Advanced_Combat_Tracker;
using Eq2Auras.Core.Meter;

namespace Eq2Auras.Plugin.Act
{
    internal static class SegmentResolver
    {
        public static string ZoneKey(ZoneData z) => z == null ? null : z.ZoneName + "#" + z.StartTime.Ticks;

        public static string Key(FormActMain form, SegmentSelection sel)
        {
            switch (sel.Kind)
            {
                case SegmentKind.Zonewide: return "Z:" + ZoneKey(form.ActiveZone);
                case SegmentKind.Historical: return "H:" + sel.ZoneKey + ":" + sel.StartTicks;
                default: return "C";
            }
        }

        /// Returns the resolved encounter, or null. `unavailable` is true only for Zonewide
        /// with PopulateAll off (a communicated state); a not-found historical handle returns
        /// (null, false) so the caller falls back to Current.
        public static EncounterData Resolve(FormActMain form, SegmentSelection sel, out bool unavailable)
        {
            unavailable = false;
            var zone = form.ActiveZone;
            switch (sel.Kind)
            {
                case SegmentKind.Zonewide:
                    if (zone != null && zone.PopulateAll && zone.Items.Count > 0) return zone.Items[0];
                    unavailable = true;
                    return null;
                case SegmentKind.Historical:
                    var z = form.ZoneList?.FirstOrDefault(zz => ZoneKey(zz) == sel.ZoneKey);
                    return z?.Items.FirstOrDefault(e => e.StartTimes.Count > 0 && e.StartTimes[0].Ticks == sel.StartTicks);
                default:
                    return zone?.ActiveEncounter;
            }
        }
    }
}
```

- [ ] **Step 3: Refactor `EncounterProbe.OnTick` to per-segment**

Change the ctor delegate and `OnTick` so the sample callback is `Action<List<SegmentSample>, List<BreakdownReading>, List<RecapReading>>`. Extract today's single-encounter read block (`EncounterProbe.cs:56-99` building `EncounterReading`+combatants, and the deaths capture) into a private `ReadSegment(FormActMain form, EncounterData enc, string key, bool unavailable) → SegmentSample`. In `OnTick`, replace the single read with:
```csharp
var selections = _segmentRequests();               // Func<IReadOnlyList<SegmentSelection>>
lock (form.AfterCombatActionDataLock)
{
    var byKey = new Dictionary<string, SegmentSample>();
    foreach (var sel in selections)
    {
        var key = SegmentResolver.Key(form, sel);
        if (byKey.ContainsKey(key)) continue;
        var enc = SegmentResolver.Resolve(form, sel, out bool unavailable);
        byKey[key] = ReadSegment(form, enc, key, unavailable);
    }
    samples = byKey.Values.ToList();
    // ... existing per-request breakdown/recap reads, each resolving its request's segment key ...
}
_onSample(samples, breakdowns, recaps);
```
Deaths capture: the count-delta store keys per-segment (`_deathStore` becomes `Dictionary<string, List<DeathRecord>>` keyed by segment `Key`, and `_deathsSeen`/`_encounterStartKey` likewise). For a frozen historical segment the scan stabilizes; for Current/Zonewide it grows as today. (This preserves the "first accumulated cross-tick store" shape, now per segment.)

- [ ] **Step 4: Wire `OverlayHost` + `Eq2AurasPlugin`**

- `OverlayHost`: add `public IReadOnlyList<SegmentSelection> CurrentSegmentRequests()` returning each window's live selection (default from `SegmentRules.FromMode(config.SegmentMode)` until Task 8 gives windows a live selection); change `UpdateMeterSample` to `UpdateMeterSample(List<SegmentSample> samples, List<BreakdownReading> breakdowns, List<RecapReading> recaps)`, build a `Dictionary<string,SegmentSample>` by `Key`, and in the fan-out loop look up each window's sample by its selection's `Key` (fall back to the Current sample, always present because every poll requests Current implicitly). Feed `window.Render(...)` from that sample's `Encounter`+`Combatants` (and `Deaths`).
- `Eq2AurasPlugin.cs:43-47`: pass `() => _overlay.CurrentSegmentRequests()` and the new 3-arg `_onSample`.

- [ ] **Step 5: Push branch, watch verify-only CI**

```bash
git add -A src/eq2auras.Core/Meter/MeterReading.cs src/eq2auras.Plugin/Act/SegmentResolver.cs src/eq2auras.Plugin/Act/EncounterProbe.cs src/eq2auras.Plugin/Overlay/OverlayHost.cs src/eq2auras.Plugin/Eq2AurasPlugin.cs
git commit -m "segment: per-distinct-segment resolution + snapshot in EncounterProbe (list follows segment)"
git push -u origin segment-picker
gh run watch <id> --exit-status
```
Expected: Core tests green, WPF plugin compiles, artifact staged, publish skipped.

---

### Task 5: Deep reads (drill / hover / Deaths) follow the window's segment

**Files:**
- Modify: `src/eq2auras.Core/Meter/Breakdown.cs` (`DrillRequest` +`SegmentKey`)
- Modify: `src/eq2auras.Plugin/Act/EncounterProbe.cs` (resolve each request's segment)
- Modify: `src/eq2auras.Plugin/Overlay/OverlayHost.cs` (stamp requests with the window's segment key), `MeterWindow.cs` (`DrillTarget`/`HoverTarget` carry the segment key)

**Interfaces:**
- Consumes: `SegmentResolver.Resolve`, `SegmentSample.Key`.
- Produces: `DrillRequest.SegmentKey` (string) — the deep-read runs against the `EncounterData` that key resolves to, not `ActiveEncounter`.

- [ ] **Step 1: Add `SegmentKey` to `DrillRequest`** and set it in `MeterWindow.DrillTarget`/`HoverTarget` from the window's current selection key (via a `Func<string>` the window is given). Deaths list already comes from the window's `SegmentSample.Deaths` (Task 4).

- [ ] **Step 2: In `EncounterProbe`**, when servicing each drill/hover request, resolve its `SegmentKey` back to the `EncounterData` already resolved this poll (reuse the `byKey` map's source encounters — keep a parallel `Dictionary<string, EncounterData>` inside the lock) and deep-read that combatant from **it**; the recap deep-read likewise.

- [ ] **Step 3: Push, watch verify-only CI.**

```bash
git commit -am "segment: drill/hover/Deaths deep reads target the window's resolved segment"
git push
gh run watch <id> --exit-status
```

---

### Task 6: The flyout enumeration read (`ZoneList` → `SegmentListing`)

**Files:**
- Modify: `src/eq2auras.Plugin/Act/SegmentResolver.cs` (+`Enumerate`)

**Interfaces:**
- Produces: `SegmentResolver.Enumerate(FormActMain form) → SegmentListing` — on flyout open, under the lock, walk `form.ZoneList` → per `ZoneData` a `RawZone { ZoneName, ZoneKey, IsCurrent = (z == form.ActiveZone), PopulateAll = z.PopulateAll, Encounters }`; per `EncounterData` a `RawEncounter { Title = e.Title, DurationSeconds = e.Duration.TotalSeconds, SuccessLevel = e.GetEncounterSuccessLevel(), StartTicks = e.StartTimes[0].Ticks, IsAll = (z.PopulateAll && index == 0) }`; feed to `SegmentListBuilder.Build`. Snapshot to Core DTOs, release. **On-open only, never per poll.**

- [ ] **Step 1: Implement `Enumerate`** (transcribe; the ordering/filter live in `SegmentListBuilder`, already tested).

```csharp
public static SegmentListing Enumerate(FormActMain form)
{
    var raws = new List<SegmentListBuilder.RawZone>();
    lock (form.AfterCombatActionDataLock)
    {
        foreach (var z in form.ZoneList ?? Enumerable.Empty<ZoneData>())
        {
            var rz = new SegmentListBuilder.RawZone
            {
                ZoneName = z.ZoneName, ZoneKey = ZoneKey(z),
                IsCurrent = ReferenceEquals(z, form.ActiveZone), PopulateAll = z.PopulateAll,
            };
            for (int i = 0; i < z.Items.Count; i++)
            {
                var e = z.Items[i];
                rz.Encounters.Add(new SegmentListBuilder.RawEncounter
                {
                    Title = string.IsNullOrEmpty(e.Title) ? "Encounter" : e.Title,
                    DurationSeconds = e.Duration.TotalSeconds,
                    SuccessLevel = e.GetEncounterSuccessLevel(),
                    StartTicks = e.StartTimes.Count > 0 ? e.StartTimes[0].Ticks : 0,
                    IsAll = z.PopulateAll && i == 0,
                });
            }
            raws.Add(rz);
        }
    }
    return SegmentListBuilder.Build(raws);
}
```

- [ ] **Step 2: Push, watch verify-only CI.**

```bash
git commit -am "segment: on-open ZoneList enumeration read -> SegmentListing"
git push && gh run watch <id> --exit-status
```

---

### Task 7: `ThemeCheckbox` primitive

**Files:**
- Create: `src/eq2auras.Plugin/Overlay/ThemeCheckbox.cs`

**Interfaces:**
- Produces: `ThemeCheckbox : Border` — `ctor(string label, bool initial)`; `event Action<bool> Toggled`; `bool Checked { get; set; }`. A 13px box (Divider border, amber `✓` when checked) + `TextLabel` text; toggles on left-click; matches `MetricGridItem`'s state idiom.

- [ ] **Step 1: Implement** (mirror `MetricGridItem.cs` structure; use `Theme.AccentAmber`/`Theme.TextLabel`/`Theme.Divider`; box fill `Theme.Surface(0x0D)`).

- [ ] **Step 2: Push, watch verify-only CI** (compiles; unused until Task 8).

```bash
git commit -am "theme: add ThemeCheckbox kit primitive"
git push && gh run watch <id> --exit-status
```

---

### Task 8: Header segment chip + the flyout UI + selection wiring

**Files:**
- Create: `src/eq2auras.Plugin/Overlay/SegmentFlyout.cs`
- Modify: `src/eq2auras.Plugin/Overlay/MeterWindow.cs` (chip element in the header grid; `_selection` state; open-flyout handler; hide chip while drilled)
- Modify: `src/eq2auras.Plugin/Overlay/MeterWindowCallbacks.cs` (+`Action<SegmentMode> SegmentModeChanged`, +`Action<bool> PinnedChanged`, +`Func<SegmentListing> EnumerateSegments`)
- Modify: `src/eq2auras.Plugin/Overlay/OverlayHost.cs` (`AddMeterWindow` — wire the new callbacks to `SettingsStore.Update` + `RebuildDrillRequests`-style segment-request rebuild; provide `EnumerateSegments = () => SegmentResolver.Enumerate(ActGlobals.oFormActMain)`)

**Design:**
- The chip: a compact `Border` in the header grid between `_metricText` and `_affordance` (`MeterWindow.cs:145-154` header grid — add a fixed `Auto` column), text = the current selection label (`Current` / `Zonewide` / a past fight title), ellipsis-trimmed; left-click opens `SegmentFlyout`. Hidden (`Collapsed`) while `_drilledCombatant != null`.
- `SegmentFlyout`: a `Popup` (like `MeterPopup`) hosting the theme popup panel → `Current`/`Zonewide` list-items on top, then a scrolling (`ScrollViewer`, MaxHeight) stack of per-zone collapsible groups (a `ZoneGroup` header row toggling its body's visibility; current zone expanded), each group's `All` (if non-null; disabled + greyed when the zone's `All` is absent under PopulateAll-off — but `SegmentListing` already omits a null `All`) then its fights (title + duration + a win/partial/wipe dot from `EncounterOutcome`), and a footer `ThemeCheckbox` "Return to Current when a fight starts".
- Picking sets the window's live `_selection`; the flyout applies `SegmentRules.ClearsKnobOnPick` (Zonewide → check-off the box / `PinnedChanged(true)`); the persisted `SegmentMode` is set only for the two live modes (`Current`/`Zonewide` → `SegmentModeChanged`); a historical pick updates runtime state only. The checkbox drives `PinnedChanged`.
- `MeterWindow` exposes `SegmentSelection CurrentSelection` (for `OverlayHost.CurrentSegmentRequests` + the deep-read `SegmentKey`), and calls a `SegmentChanged` callback so the host rebuilds the segment-request set (same pattern as `DrillChanged`).

- [ ] **Step 1: Implement `SegmentFlyout`** (transcribe against `MeterPopup.cs` patterns + `ThemeCheckbox`).
- [ ] **Step 2: Add the chip + state + callbacks to `MeterWindow`**; hide while drilled; render its label from `_selection` in `Render`.
- [ ] **Step 3: Wire callbacks + `EnumerateSegments` + the segment-request rebuild in `OverlayHost`**; persist `SegmentMode`/`PinnedToSegment` via `SettingsStore.Update`.
- [ ] **Step 4: Push, watch verify-only CI.**

```bash
git commit -am "segment: header chip + flyout (Current/Zonewide/zones, collapse, knob) + persistence wiring"
git push && gh run watch <id> --exit-status
```

---

### Task 9: Zonewide-unavailable dormant body

**Files:**
- Modify: `src/eq2auras.Plugin/Overlay/MeterWindow.cs` (render the unavailable state)
- Modify: `src/eq2auras.Plugin/Overlay/OverlayHost.cs` (detect `sample.Unavailable` for the window's segment)

**Design:** when the window's resolved `SegmentSample.Unavailable` is true (Zonewide with `PopulateAll` off), render the **cleared-primary dormant body** (backdrop, no rows) plus a **single one-line hint** ("Enable ACT's \"Zone All listing\" for Zonewide") in the body; the header keeps duration + chip + cog. (The flyout naturally offers no Zonewide/`All` when off, since `SegmentListing` omits a null `All` and `SegmentResolver` reports unavailable — but a *persisted* Zonewide window entering a PopulateAll-off zone hits this path.)

- [ ] **Step 1: Implement the unavailable body** (reuse the cleared-primary rendering path + one `TextBlock` hint).
- [ ] **Step 2: Push, watch verify-only CI.**

```bash
git commit -am "segment: Zonewide-unavailable dormant body with a one-line hint"
git push && gh run watch <id> --exit-status
```

---

## On-box live verification (Alex's merge gate — concrete "do X, expect Y")

Run on the Windows box after pulling the branch build:

1. **Chip + flyout:** the header shows a `Current` chip; click it → flyout opens with **Current** and **Zonewide** on top, then zone groups (current zone expanded, older collapsed), each group its `All` then fights as **title + duration** with **win/partial/wipe** dots; **no `0:00` fights** appear (compare against an imported log that has some).
2. **History peek + drill:** pick a past fight → the window shows that fight's rows; drill an ally → **that fight's** abilities. With **`Return to Current` on** (default), start a new pull → the window **snaps to Current**. Toggle the knob **off**, pick the fight again → it **stays** through the next pull.
3. **Zonewide follow + auto-pin:** pick **Zonewide** → the box **auto-unchecks**; the window shows the current zone's All and **updates live**; **zone into a new area** → the window shows the **new** zone's All with no action. A specific zone's group **`All`** picked from the flyout **stays** on that zone after you leave.
4. **Availability:** turn ACT's **"Zone All listing" off** → Zonewide/`All` entries vanish from the flyout; a window persisted on Zonewide shows the **unavailable** body with the hint. Turn it back on → Zonewide works again.
5. **Persistence:** set one window to **Zonewide** (pinned), reload the plugin → it reopens on **Zonewide**; a window that was **peeking a past fight** reopens on **Current** (historical pick not persisted).
6. **Multi-window / cost:** a **Current** DPS window and a **Zonewide** HPS window side by side both update live (two distinct segments resolved per poll); three Current windows collapse to one snapshot (no stutter).
7. **Regression:** all-Current windows behave exactly as before; **drill/hover/Deaths** on a Current window unchanged; **timer overlay** unaffected (light sanity check).

## Plan-watch items (from the spec review — verify during implementation)

1. **`PopulateAll` detection** — grounded: `ZoneData.PopulateAll` is read directly (`SegmentResolver`); field-confirm on the box (step 4 above) that `Items[0]` is the true "All" only when it is set.
2. **`ZoneList`/handle re-resolution + culling** — the `(ZoneName + StartTime.Ticks)` zone key and the `StartTimes[0].Ticks` encounter handle re-resolve each poll; a culled encounter yields null → Current fallback (verify via a long session that culls old fights).
3. **Same-named zone revisits** — confirm ACT appends a **new** `ZoneData` per visit (so `StartTime` disambiguates two Nizara visits); if it reuses one keyed by name, revisit the zone key (step 3 above exercises a re-zone).
4. **Per-distinct-segment snapshot cost** — bounded by window count; verify no per-poll stutter with mixed segments (step 6).

---

## Execution

Executed **inline** in this session via `superpowers:executing-plans` (Alex watches; Core tasks strict-TDD on the Mac loop, Plugin tasks transcribe-only + verify-CI + the on-box script). Presents **ready-for-review**; the reviewer's plan-review loop and Alex's merge gate follow.
