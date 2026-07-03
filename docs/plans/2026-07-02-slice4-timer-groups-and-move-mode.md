# Slice 4: Timer Groups (Dual Panels) + Unlock/Move Mode Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task, **inline in the main session** (repo convention: Alex watches execution; do not dispatch subagents). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Two independent timer groups (ACT Panel A / Panel B), each a full instance of the existing pipeline — own list window, own center zone, own knobs, own dragged-and-persisted positions — plus the unlock/move mode that makes positioning possible.

**Architecture:** Core gains `PanelSettings` (per-group knobs + nullable positions inside `Settings.Panels`) and `OverlayEngine` (one `EscalationTracker` per group, one shared `PaletteAssigner`, routing by ACT's per-timer panel booleans). The plugin stays instantiator/data supplier: `TimerProbe` carries the two panel flags, `OverlayHost` hosts a window pair per group, and a tab checkbox toggles move mode (chrome + `DragMove` + persist). Spec: `docs/SPEC.md` §Timer groups, §Moving the overlay, §Configuration, §Timer colors.

**Tech Stack:** existing (netstandard2.0 Core / net472 WPF plugin / xUnit / GitHub Actions msbuild). DCJS only.

## Global Constraints

- **Never build the plugin/solution on the Mac.** Only Core tests run locally: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`. The plugin compiles only in CI.
- **Single assembly:** Core sources are `<Compile Include>`d into the plugin. Never reference a second DLL.
- **Scan-safety:** no `async` anywhere in the plugin project; no non-GAC types in plugin fields beyond types compiled into the assembly itself.
- **DCJS only** — never `System.Web.Extensions` (breaks the WPF XAML markup compiler in CI).
- **Enum knob defaults must be the 0-value** (DCJS skips field initializers on deserialize).
- **Window positions must be nullable numerics** — DCJS materializes missing numerics as `0`, a real screen corner; `null` means "unset, use default layout".
- **WPF: retain elements, animate properties** — the existing `RenderRows`/`RenderElements` retained-visual pattern must not be rebuilt per tick.
- Commits go straight to `main`, no ticket prefixes (solo repo convention). Commit per task; push + CI watch only in the final task.
- Reviewer-mandated requirements (backlog NEXT UP entry) are woven into Tasks 1–6 and called out where they land.

## File Structure

```
src/eq2auras.Core/
├── Config/
│   ├── PanelSettings.cs             # NEW: per-group knobs + nullable window positions
│   └── Settings.cs                  # panels list, normalization, bidirectional legacy migration
└── Timers/
    ├── TimerReading.cs              # + ShowInPanelA/ShowInPanelB
    ├── EscalationTracker.cs         # ctor(PanelSettings, PaletteAssigner); non-mutating color resolution
    └── OverlayEngine.cs             # NEW: trackers-from-panels, shared assigner, flag routing
src/eq2auras.Core/Diagnostics/
└── TimerSnapshotRecord.cs           # + panelA/panelB flags in JSONL
src/eq2auras.Plugin/
├── Act/TimerProbe.cs                # copies Panel1Display/Panel2Display into readings + log records
├── Eq2AurasPlugin.cs                # OverlayEngine wiring; per-panel group boxes; move checkbox
├── SelfUpdate/SettingsStore.cs      # Update(settings, mutate) under one lock (Save removed)
└── Overlay/
    ├── ClickThrough.cs              # NEW: shared WS_EX_TRANSPARENT toggle (P/Invoke moves here)
    ├── MoveChrome.cs                # NEW: dashed outline + translucent fill + label chip
    ├── OverlayHost.cs               # window pair per group; positions; SetMoveMode; re-lock save
    ├── TimerListWindow.xaml(.cs)    # ctor(label, left, top, persist); chrome; preview code deleted
    ├── CenterZoneWindow.xaml(.cs)   # same ctor shape; positioning moves to OverlayHost
    └── OverlayTheme.cs              # Palette mirror deleted (preview was its only consumer)
tests/eq2auras.Core.Tests/
├── SettingsTests.cs                 # panels round-trip, migration, normalization, zero-trap
├── EscalationTrackerTests.cs        # ctor updates; non-mutation + shared-assigner tests
├── OverlayEngineTests.cs            # NEW: routing table, per-group knobs, color identity
└── TimerSnapshotRecordTests.cs      # panel flags in JSONL
```

---

### Task 1: PanelSettings + Settings.Panels (TDD, Mac)

**Files:**
- Create: `src/eq2auras.Core/Config/PanelSettings.cs`
- Modify: `src/eq2auras.Core/Config/Settings.cs`
- Test: `tests/eq2auras.Core.Tests/SettingsTests.cs`

**Interfaces:**
- Produces: `PanelSettings { ColorSource ColorSource; EscalationStyle EscalationStyle; double? ListLeft; double? ListTop; double? CenterLeft; double? CenterTop; }`
- Produces: `Settings.Panels : List<PanelSettings>` (always exactly `Settings.GroupCount == 2` entries after `Parse`/construction); `Settings.ToJson()` mirrors `Panels[0]` knobs into the legacy flat fields.

- [ ] **Step 1: Write the failing tests** — append to `tests/eq2auras.Core.Tests/SettingsTests.cs`:

```csharp
    [Fact]
    public void Roundtrips_per_panel_knobs_and_positions()
    {
        var settings = new Settings();
        settings.Panels[0].ColorSource = ColorSource.Greyscale;
        settings.Panels[0].ListLeft = 42.5;
        settings.Panels[0].ListTop = 0;              // zero is a REAL position, must survive
        settings.Panels[1].EscalationStyle = EscalationStyle.HighlightInPlace;
        settings.Panels[1].CenterLeft = 900;

        var parsed = Settings.Parse(settings.ToJson());

        Assert.Equal(2, parsed.Panels.Count);
        Assert.Equal(ColorSource.Greyscale, parsed.Panels[0].ColorSource);
        Assert.Equal(42.5, parsed.Panels[0].ListLeft);
        Assert.Equal(0.0, parsed.Panels[0].ListTop);
        Assert.Null(parsed.Panels[0].CenterLeft);    // unset stays null — never 0
        Assert.Equal(EscalationStyle.HighlightInPlace, parsed.Panels[1].EscalationStyle);
        Assert.Equal(900.0, parsed.Panels[1].CenterLeft);
        Assert.Null(parsed.Panels[1].ListLeft);
    }

    [Fact]
    public void Legacy_flat_file_seeds_panel_A_and_defaults_panel_B()
    {
        var parsed = Settings.Parse("{\"colorSource\":1,\"escalationStyle\":1}");

        Assert.Equal(2, parsed.Panels.Count);
        Assert.Equal(ColorSource.Greyscale, parsed.Panels[0].ColorSource);
        Assert.Equal(EscalationStyle.HighlightInPlace, parsed.Panels[0].EscalationStyle);
        Assert.Equal(ColorSource.Palette, parsed.Panels[1].ColorSource);
        Assert.Equal(EscalationStyle.CenterRadial, parsed.Panels[1].EscalationStyle);
        Assert.Null(parsed.Panels[0].ListLeft);
    }

    [Fact]
    public void Save_mirrors_panel_A_knobs_to_the_legacy_flat_fields()
    {
        var settings = new Settings();
        settings.Panels[0].ColorSource = ColorSource.ActColor;
        settings.Panels[0].EscalationStyle = EscalationStyle.HighlightInPlace;

        var json = settings.ToJson();

        Assert.Contains("\"colorSource\":2", json);
        Assert.Contains("\"escalationStyle\":1", json);
    }

    [Theory]
    [InlineData("{\"panels\":[]}")]                     // empty list
    [InlineData("{\"panels\":[{\"colorSource\":1}]}")]  // one entry
    [InlineData("{\"panels\":[{},{},{}]}")]             // three entries
    public void Panel_list_normalizes_to_exactly_two(string json)
    {
        Assert.Equal(2, Settings.Parse(json).Panels.Count);
    }

    [Fact]
    public void Short_panel_list_keeps_existing_entries_and_pads_defaults()
    {
        var parsed = Settings.Parse("{\"panels\":[{\"colorSource\":1}]}");

        Assert.Equal(ColorSource.Greyscale, parsed.Panels[0].ColorSource);
        Assert.Equal(ColorSource.Palette, parsed.Panels[1].ColorSource);
    }
```

Also extend the existing `Bad_or_partial_json_yields_defaults` theory body with one more assertion (defaults now include two panels):

```csharp
        Assert.Equal(2, parsed.Panels.Count);
```

- [ ] **Step 2: Run red** — `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
Expected: FAIL — `Settings` has no `Panels`, `PanelSettings` missing.

- [ ] **Step 3: Implement** — create `src/eq2auras.Core/Config/PanelSettings.cs`:

```csharp
using System.Runtime.Serialization;

namespace Eq2Auras.Core.Config
{
    /// Per-group knobs + window positions (SPEC §Timer groups, §Moving the overlay).
    /// Positions are nullable on purpose: DCJS materializes missing numerics as 0 — a
    /// real screen corner — so null (never zero) means "unset, use the default layout".
    [DataContract]
    public sealed class PanelSettings
    {
        [DataMember(Name = "colorSource")]
        public ColorSource ColorSource { get; set; } = ColorSource.Palette;

        [DataMember(Name = "escalationStyle")]
        public EscalationStyle EscalationStyle { get; set; } = EscalationStyle.CenterRadial;

        [DataMember(Name = "listLeft")]
        public double? ListLeft { get; set; }

        [DataMember(Name = "listTop")]
        public double? ListTop { get; set; }

        [DataMember(Name = "centerLeft")]
        public double? CenterLeft { get; set; }

        [DataMember(Name = "centerTop")]
        public double? CenterTop { get; set; }
    }
}
```

Modify `src/eq2auras.Core/Config/Settings.cs` — add `using System.Collections.Generic;` and `using System.Linq;`, then inside the class add the panels member and normalization, and update `Parse`/`ToJson`:

```csharp
        public const int GroupCount = 2;

        [DataMember(Name = "panels")]
        public List<PanelSettings> Panels { get; set; } = DefaultPanels();

        private static List<PanelSettings> DefaultPanels()
        {
            var panels = new List<PanelSettings>();
            for (int i = 0; i < GroupCount; i++)
            {
                panels.Add(new PanelSettings());
            }
            return panels;
        }

        /// DCJS skips initializers, so a deserialized instance may carry a null or
        /// wrong-length panel list. Normalizes to exactly GroupCount entries. A legacy
        /// flat file (no panels key at all) seeds Panel A from its top-level knobs;
        /// Panel B starts at defaults (SPEC §Configuration).
        private void Normalize()
        {
            bool legacyFile = Panels == null;

            Panels = (Panels ?? new List<PanelSettings>()).Where(p => p != null).ToList();
            while (Panels.Count < GroupCount) Panels.Add(new PanelSettings());
            if (Panels.Count > GroupCount) Panels = Panels.Take(GroupCount).ToList();

            if (legacyFile)
            {
                Panels[0].ColorSource = ColorSource;
                Panels[0].EscalationStyle = EscalationStyle;
            }
        }
```

`Parse` gains the normalize call on the success path (the catch path runs initializers, which already build two default panels):

```csharp
                    var settings = (Settings)serializer.ReadObject(stream) ?? new Settings();
                    settings.Normalize();
                    return settings;
```

`ToJson` mirrors Panel A into the legacy flat fields before serializing (backward compatibility: an older build reads the flat knobs as its only knobs):

```csharp
        public string ToJson()
        {
            Normalize();
            ColorSource = Panels[0].ColorSource;
            EscalationStyle = Panels[0].EscalationStyle;

            var serializer = new DataContractJsonSerializer(typeof(Settings));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, this);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
```

- [ ] **Step 4: Run green** — `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
Expected: PASS, all tests.

- [ ] **Step 5: Commit**

```bash
git add src/eq2auras.Core/Config/ tests/eq2auras.Core.Tests/SettingsTests.cs
git commit -m "Core: PanelSettings + Settings.Panels — nullable positions, normalization, bidirectional legacy migration"
```

---

### Task 2: EscalationTracker — per-group settings, injected assigner, non-mutating colors (TDD, Mac)

**Files:**
- Modify: `src/eq2auras.Core/Timers/TimerReading.cs`
- Modify: `src/eq2auras.Core/Timers/EscalationTracker.cs`
- Test: `tests/eq2auras.Core.Tests/EscalationTrackerTests.cs`

**Interfaces:**
- Consumes: `PanelSettings` (Task 1).
- Produces: `TimerReading.ShowInPanelA / ShowInPanelB : bool`; `EscalationTracker(PanelSettings settings = null, PaletteAssigner palette = null)`; `Tick` never mutates its input readings.

- [ ] **Step 1: Write the failing tests** — in `tests/eq2auras.Core.Tests/EscalationTrackerTests.cs`, first mechanically update the four existing constructions that pass `new Settings {...}` to pass `new PanelSettings {...}` instead (the `ActColor_mode_keeps_the_timers_own_color_softened` test and the three `HighlightInPlace_*` tests — same property names, only the type changes). Then append:

```csharp
    [Fact]
    public void Color_resolution_does_not_mutate_the_input_readings()
    {
        var reading = Reading("boss", 25);              // FillArgb = -16776961 (ACT blue)
        new EscalationTracker().Tick(R(reading));

        Assert.Equal(-16776961, reading.FillArgb);      // original untouched
    }

    [Fact]
    public void Two_trackers_sharing_readings_each_resolve_from_the_ACT_original()
    {
        var palette = new PaletteAssigner();
        var trackerA = new EscalationTracker(new PanelSettings(), palette);
        var trackerB = new EscalationTracker(new PanelSettings { ColorSource = ColorSource.ActColor }, palette);
        var readings = R(Reading("boss", 25));

        trackerA.Tick(readings);                        // Palette mode resolves first
        var frameB = trackerB.Tick(readings);

        // B must soften ACT's original blue — NOT A's already-assigned palette color.
        Assert.Equal(ColorPolicy.Soften(-16776961), frameB.ListRows[0].FillArgb);
    }

    [Fact]
    public void Shared_assigner_gives_one_name_one_slot_across_trackers()
    {
        var palette = new PaletteAssigner();
        var trackerA = new EscalationTracker(new PanelSettings(), palette);
        var trackerB = new EscalationTracker(new PanelSettings(), palette);

        trackerA.Tick(R(Reading("First", 25)));
        var frameB = trackerB.Tick(R(Reading("Second", 25), Reading("First", 20)));

        Assert.Equal(ColorPolicy.PaletteArgb[0], frameB.ListRows.Single(r => r.Name == "First").FillArgb);
        Assert.Equal(ColorPolicy.PaletteArgb[1], frameB.ListRows.Single(r => r.Name == "Second").FillArgb);
    }
```

- [ ] **Step 2: Run red** — `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
Expected: FAIL — no `EscalationTracker(PanelSettings, PaletteAssigner)` ctor; mutation test fails against current in-place write.

- [ ] **Step 3: Implement** — add the routing flags to `src/eq2auras.Core/Timers/TimerReading.cs` (after `FillArgb`):

```csharp
        public bool ShowInPanelA { get; set; }   // TimerData.Panel1Display — group routing
        public bool ShowInPanelB { get; set; }   // TimerData.Panel2Display
```

In `src/eq2auras.Core/Timers/EscalationTracker.cs`, replace the class docstring, fields, ctor, the governing-selection statement, and the color-mutation loop:

```csharp
    /// The escalation policy for ONE timer group: a per-tick mapping from the group's
    /// readings to display elements, parameterized by the group's PanelSettings. The
    /// palette assigner is injected and SHARED across groups (color identity is global —
    /// SPEC §Timer colors). Every display state still derives from the data ACT reports
    /// this tick, and nothing on screen ever outlives the data.
    public sealed class EscalationTracker
    {
        private const int CenterSlots = 3;          // future config knob

        private readonly PanelSettings _settings;
        private readonly PaletteAssigner _palette;

        public EscalationTracker(PanelSettings settings = null, PaletteAssigner palette = null)
        {
            _settings = settings ?? new PanelSettings();
            _palette = palette ?? new PaletteAssigner();
        }
```

The governing selection gains the copy-with-color step (and the `foreach` mutation loop below it is **deleted**):

```csharp
            var governing = readings
                .GroupBy(KeyOf)
                .Select(g => g.OrderBy(TimerMath.PreciseOf).First())
                .Select(WithResolvedColor)
                .ToList();
```

Add the copy method (bottom of the class, above `KeyOf`):

```csharp
        /// Resolves the final display color into a COPY of the governing reading — never
        /// in place: the engine routes the same reading objects to multiple groups, and
        /// an in-place write would hand the second group the first group's output (e.g.
        /// ActColor softening an already-assigned palette color).
        private TimerReading WithResolvedColor(TimerReading reading)
        {
            return new TimerReading
            {
                Name = reading.Name,
                Combatant = reading.Combatant,
                TimeLeft = reading.TimeLeft,
                RawPreciseTimeLeft = reading.RawPreciseTimeLeft,
                WarningValue = reading.WarningValue,
                RemoveValueSeconds = reading.RemoveValueSeconds,
                TotalSeconds = reading.TotalSeconds,
                ShowInPanelA = reading.ShowInPanelA,
                ShowInPanelB = reading.ShowInPanelB,
                FillArgb = ColorPolicy.Resolve(_settings.ColorSource, _palette.IndexFor(reading.Name), reading.FillArgb)
            };
        }
```

The rest of `Tick` (lates, centered, pies, list source) is unchanged — it already reads `_settings.EscalationStyle`, and `PanelSettings` carries the same property names.

- [ ] **Step 4: Run green** — `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
Expected: PASS, all tests (old style/color tests prove behavior is preserved under the new ctor).

- [ ] **Step 5: Commit**

```bash
git add src/eq2auras.Core/Timers/TimerReading.cs src/eq2auras.Core/Timers/EscalationTracker.cs tests/eq2auras.Core.Tests/EscalationTrackerTests.cs
git commit -m "Core: tracker takes PanelSettings + shared PaletteAssigner; color resolution copies, never mutates"
```

---

### Task 3: OverlayEngine — routing + per-group frames (TDD, Mac)

**Files:**
- Create: `src/eq2auras.Core/Timers/OverlayEngine.cs`
- Test: `tests/eq2auras.Core.Tests/OverlayEngineTests.cs`

**Interfaces:**
- Consumes: `Settings.Panels` (Task 1), `EscalationTracker(PanelSettings, PaletteAssigner)` + `TimerReading.ShowInPanelA/B` (Task 2).
- Produces: `OverlayEngine(Settings)`; `Tick(IReadOnlyList<TimerReading>) : List<OverlayFrame>` — index-aligned with `Settings.Panels`.

- [ ] **Step 1: Write the failing tests** — create `tests/eq2auras.Core.Tests/OverlayEngineTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using Eq2Auras.Core.Config;
using Eq2Auras.Core.Timers;
using Xunit;

public class OverlayEngineTests
{
    private static TimerReading Reading(string name, int timeLeft, bool inA = false, bool inB = false)
        => new TimerReading
        {
            Name = name, Combatant = "none", TimeLeft = timeLeft,
            RawPreciseTimeLeft = timeLeft, WarningValue = 10, TotalSeconds = 30,
            RemoveValueSeconds = -15, FillArgb = -16776961,
            ShowInPanelA = inA, ShowInPanelB = inB
        };

    [Fact]
    public void Routes_by_panel_flags_mirroring_ACT()
    {
        var engine = new OverlayEngine(new Settings());

        var frames = engine.Tick(new List<TimerReading>
        {
            Reading("a-only", 25, inA: true),
            Reading("b-only", 24, inB: true),
            Reading("both", 23, inA: true, inB: true),
            Reading("neither", 22)
        });

        Assert.Equal(2, frames.Count);
        Assert.Equal(new[] { "both", "a-only" }, frames[0].ListRows.Select(r => r.Name).ToArray());
        Assert.Equal(new[] { "both", "b-only" }, frames[1].ListRows.Select(r => r.Name).ToArray());
    }

    [Fact]
    public void Groups_apply_their_own_knobs_independently()
    {
        var settings = new Settings();
        settings.Panels[1].EscalationStyle = EscalationStyle.HighlightInPlace;
        var engine = new OverlayEngine(settings);

        var frames = engine.Tick(new List<TimerReading> { Reading("boss", 5, inA: true, inB: true) });

        Assert.Empty(frames[0].ListRows);            // A: CenterRadial -> pie in the zone
        Assert.Single(frames[0].CenterElements);
        Assert.Single(frames[1].ListRows);           // B: stays in the list, highlighted
        Assert.Empty(frames[1].CenterElements);
    }

    [Fact]
    public void Dual_flagged_timer_keeps_one_color_identity_across_groups()
    {
        var settings = new Settings();
        settings.Panels[1].ColorSource = ColorSource.Greyscale;
        var engine = new OverlayEngine(settings);

        var frames = engine.Tick(new List<TimerReading>
        {
            Reading("First", 25, inA: true, inB: true),
            Reading("Second", 20, inA: true, inB: true)
        });

        // One slot per name everywhere; each group renders its own ColorSource over it.
        Assert.Equal(ColorPolicy.PaletteArgb[0], frames[0].ListRows.Single(r => r.Name == "First").FillArgb);
        Assert.Equal(ColorPolicy.GreyArgb[0], frames[1].ListRows.Single(r => r.Name == "First").FillArgb);
        Assert.Equal(ColorPolicy.PaletteArgb[1], frames[0].ListRows.Single(r => r.Name == "Second").FillArgb);
        Assert.Equal(ColorPolicy.GreyArgb[1], frames[1].ListRows.Single(r => r.Name == "Second").FillArgb);
    }
}
```

- [ ] **Step 2: Run red** — `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
Expected: FAIL — `OverlayEngine` missing.

- [ ] **Step 3: Implement** — create `src/eq2auras.Core/Timers/OverlayEngine.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using Eq2Auras.Core.Config;

namespace Eq2Auras.Core.Timers
{
    /// The multi-group policy (SPEC §Timer groups): one EscalationTracker per configured
    /// group, all sharing ONE PaletteAssigner — color identity is global, so an ability
    /// keeps its color in every group. Routing mirrors ACT's panel flags exactly: a
    /// reading goes to every group whose flag is set — both -> both, neither -> nowhere.
    public sealed class OverlayEngine
    {
        private readonly PaletteAssigner _palette = new PaletteAssigner();
        private readonly List<EscalationTracker> _trackers;

        public OverlayEngine(Settings settings)
        {
            _trackers = (settings ?? new Settings()).Panels
                .Select(panel => new EscalationTracker(panel, _palette))
                .ToList();
        }

        /// One frame per group, index-aligned with Settings.Panels.
        public List<OverlayFrame> Tick(IReadOnlyList<TimerReading> readings)
        {
            return _trackers
                .Select((tracker, i) => tracker.Tick(
                    readings.Where(r => RoutesTo(i, r)).ToList()))
                .ToList();
        }

        // The one deliberately two-shaped piece: ACT's routing data IS two panel
        // booleans. N-groups later = per-group source rules here; nothing else
        // changes shape (SPEC §Timer groups).
        private static bool RoutesTo(int panelIndex, TimerReading reading)
            => panelIndex == 0 ? reading.ShowInPanelA : reading.ShowInPanelB;
    }
}
```

- [ ] **Step 4: Run green** — `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
Expected: PASS, all tests.

- [ ] **Step 5: Commit**

```bash
git add src/eq2auras.Core/Timers/OverlayEngine.cs tests/eq2auras.Core.Tests/OverlayEngineTests.cs
git commit -m "Core: OverlayEngine — per-group trackers, shared color identity, mirror-ACT panel routing"
```

---

### Task 4: Group-aware diagnostics (TDD, Mac)

**Files:**
- Modify: `src/eq2auras.Core/Diagnostics/TimerSnapshotRecord.cs`
- Test: `tests/eq2auras.Core.Tests/TimerSnapshotRecordTests.cs`

**Interfaces:**
- Produces: `TimerSnapshotRecord.PanelA / PanelB : bool`, emitted as `"panelA"`/`"panelB"` JSON booleans.

- [ ] **Step 1: Write the failing test** — append to `tests/eq2auras.Core.Tests/TimerSnapshotRecordTests.cs`:

```csharp
    [Fact]
    public void Includes_panel_routing_flags()
    {
        var record = new TimerSnapshotRecord { Kind = "poll", Name = "t", PanelA = true, PanelB = false };

        var json = record.ToJsonl();

        Assert.Contains("\"panelA\":true", json);
        Assert.Contains("\"panelB\":false", json);
    }
```

- [ ] **Step 2: Run red** — `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
Expected: FAIL — `PanelA` missing.

- [ ] **Step 3: Implement** — in `src/eq2auras.Core/Diagnostics/TimerSnapshotRecord.cs`, add after `TotalValue`:

```csharp
        public bool PanelA { get; set; }    // TimerData.Panel1Display — group routing (SPEC §Diagnostic logging)
        public bool PanelB { get; set; }    // TimerData.Panel2Display
```

and in `ToJsonl()`, before the closing `sb.Append("}")`:

```csharp
            sb.Append(",\"panelA\":").Append(PanelA ? "true" : "false");
            sb.Append(",\"panelB\":").Append(PanelB ? "true" : "false");
```

- [ ] **Step 4: Run green** — `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
Expected: PASS, all tests.

- [ ] **Step 5: Commit**

```bash
git add src/eq2auras.Core/Diagnostics/TimerSnapshotRecord.cs tests/eq2auras.Core.Tests/TimerSnapshotRecordTests.cs
git commit -m "Core: diagnostics records carry panel routing flags"
```

---

### Task 5: Overlay windows — ClickThrough/MoveChrome helpers, per-group hosting, move mode [CI-only compile]

**Files:**
- Create: `src/eq2auras.Plugin/Overlay/ClickThrough.cs`
- Create: `src/eq2auras.Plugin/Overlay/MoveChrome.cs`
- Modify: `src/eq2auras.Plugin/SelfUpdate/SettingsStore.cs`
- Modify: `src/eq2auras.Plugin/Overlay/TimerListWindow.xaml` + `.xaml.cs`
- Modify: `src/eq2auras.Plugin/Overlay/CenterZoneWindow.xaml` + `.xaml.cs`
- Modify: `src/eq2auras.Plugin/Overlay/OverlayHost.cs`
- Modify: `src/eq2auras.Plugin/Overlay/OverlayTheme.cs`

**Interfaces:**
- Consumes: `Settings.Panels`/`PanelSettings` (Task 1), `OverlayFrame` (existing).
- Produces: `SettingsStore.Update(Settings, Action)` (replaces `Save`); `OverlayHost(Settings)` with `Start()`, `UpdateFrames(List<OverlayFrame>)`, `SetMoveMode(bool)`, `Dispose()`; window ctors `TimerListWindow(string moveLabel, double left, double top, Action<double,double> persistPosition)` and `CenterZoneWindow(...same...)`; both windows expose `SetMoveMode(bool)`.

No Mac tests possible — this task's gate is the CI compile in Task 7. Keep each file's change verbatim as below.

- [ ] **Step 1: SettingsStore — one gate around mutate-and-save** (reviewer requirement: knob handlers run on ACT's UI thread, drag-end saves on the overlay STA thread). Replace the class body:

```csharp
using System;
using System.IO;
using Advanced_Combat_Tracker;
using Eq2Auras.Core.Config;

namespace Eq2Auras.Plugin.SelfUpdate
{
    public static class SettingsStore
    {
        private static readonly object Gate = new object();

        private static string PathOnDisk => Path.Combine(
            ActGlobals.oFormActMain.AppDataFolder.FullName, "eq2auras", "settings.json");

        public static Settings Load()
        {
            if (!File.Exists(PathOnDisk)) return new Settings();
            return Settings.Parse(File.ReadAllText(PathOnDisk));
        }

        /// ALL settings mutation goes through here: the mutate action and the file
        /// write share one gate, so writers on ACT's UI thread (tab knobs) and the
        /// overlay STA thread (drag-end positions) can't interleave or tear the file.
        public static void Update(Settings settings, Action mutate)
        {
            lock (Gate)
            {
                mutate();
                Directory.CreateDirectory(Path.GetDirectoryName(PathOnDisk));
                File.WriteAllText(PathOnDisk, settings.ToJson());
            }
        }
    }
}
```

(The old `Save` is deleted; Task 6 updates its call sites.)

- [ ] **Step 2: ClickThrough helper** — create `src/eq2auras.Plugin/Overlay/ClickThrough.cs` (the P/Invoke pair moves here from both windows):

```csharp
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Eq2Auras.Plugin.Overlay
{
    /// WS_EX_TRANSPARENT toggling shared by all overlay windows: on = clicks pass
    /// through to the game (normal play); off = the window is mouse-hittable (move mode).
    internal static class ClickThrough
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_TRANSPARENT = 0x20;

        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        public static void Set(Window window, bool clickThrough)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            int ex = GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_LAYERED;
            SetWindowLong(hwnd, GWL_EXSTYLE,
                clickThrough ? ex | WS_EX_TRANSPARENT : ex & ~WS_EX_TRANSPARENT);
        }
    }
}
```

- [ ] **Step 3: MoveChrome helper** — create `src/eq2auras.Plugin/Overlay/MoveChrome.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Eq2Auras.Plugin.Overlay
{
    /// The unlock-mode chrome (SPEC §Moving the overlay): dashed outline + translucent
    /// fill + label chip. The fill is what makes the window mouse-hittable at all — a
    /// transparent WPF window has no hit-test surface — and its MinHeight gives empty
    /// windows (a quiet list, an idle center zone) a grabbable footprint.
    internal static class MoveChrome
    {
        public static Grid Build(string label)
        {
            var outline = new Rectangle
            {
                Stroke = new SolidColorBrush(Color.FromArgb(230, 86, 180, 233)),
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                RadiusX = 4,
                RadiusY = 4,
                Fill = new SolidColorBrush(Color.FromArgb(70, 86, 180, 233))
            };
            var chip = new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Colors.WhiteSmoke),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var chrome = new Grid { MinHeight = 60, Visibility = Visibility.Collapsed };
            chrome.Children.Add(outline);
            chrome.Children.Add(chip);
            return chrome;
        }
    }
}
```

- [ ] **Step 4: TimerListWindow** — `TimerListWindow.xaml` loses its hardcoded position and the preview panel; the content gets a root Grid so chrome can overlay it:

```xml
<Window x:Class="Eq2Auras.Plugin.Overlay.TimerListWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        WindowStyle="None" AllowsTransparency="True" Background="Transparent"
        Topmost="True" ShowInTaskbar="False" ResizeMode="NoResize"
        SizeToContent="Height" Width="260">
    <Grid x:Name="RootGrid">
        <StackPanel x:Name="RowsPanel" />
    </Grid>
</Window>
```

`TimerListWindow.xaml.cs` — delete the P/Invoke block, `MakeClickThrough`, `ShowPalettePreview`, and `BuildPalettePreview` (dead since slice 3); new ctor + move mode:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Eq2Auras.Core.Timers;

namespace Eq2Auras.Plugin.Overlay
{
    public partial class TimerListWindow : Window
    {
        // Retained visuals keyed by timer identity — updated, never rebuilt, so the
        // drain animations run continuously at display refresh.
        private readonly Dictionary<string, TimerRowVisual> _rows = new Dictionary<string, TimerRowVisual>();
        private readonly Grid _moveChrome;
        private readonly Action<double, double> _persistPosition;

        public TimerListWindow(string moveLabel, double left, double top, Action<double, double> persistPosition)
        {
            InitializeComponent();
            Left = left;
            Top = top;
            _persistPosition = persistPosition;

            _moveChrome = MoveChrome.Build(moveLabel);
            RootGrid.Children.Add(_moveChrome);

            SourceInitialized += (s, e) => ClickThrough.Set(this, true);
            MouseLeftButtonDown += OnDragStart;
        }

        public void SetMoveMode(bool moving)
        {
            _moveChrome.Visibility = moving ? Visibility.Visible : Visibility.Collapsed;
            ClickThrough.Set(this, !moving);
        }

        private void OnDragStart(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_moveChrome.Visibility != Visibility.Visible) return;
            DragMove();                          // blocks until the button is released
            _persistPosition(Left, Top);         // crash-safe: saved on every drag-end
        }

        /// Called on the overlay's dispatcher thread with a fresh sorted snapshot.
        public void RenderRows(List<TimerRow> rows)
        {
            var seen = new HashSet<string>();
            RowsPanel.Children.Clear();   // same element instances re-added in sort order — animations continue
            foreach (var row in rows)
            {
                var key = row.Name + "|" + row.Combatant;
                seen.Add(key);
                if (!_rows.TryGetValue(key, out var visual))
                {
                    visual = new TimerRowVisual();
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

- [ ] **Step 5: CenterZoneWindow** — `CenterZoneWindow.xaml` gets the same root-Grid treatment:

```xml
<Window x:Class="Eq2Auras.Plugin.Overlay.CenterZoneWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        WindowStyle="None" AllowsTransparency="True" Background="Transparent"
        Topmost="True" ShowInTaskbar="False" ResizeMode="NoResize"
        SizeToContent="Height" Width="200">
    <Grid x:Name="RootGrid">
        <StackPanel x:Name="ElementsPanel" />
    </Grid>
</Window>
```

`CenterZoneWindow.xaml.cs` — delete `ZoneVerticalScreenFraction`, the P/Invoke block, `MakeClickThrough`, and the ctor's self-positioning (OverlayHost owns defaults now); mirror the list window's ctor/move-mode shape. The `RetainedElement` class and `RenderElements` stay exactly as they are:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Eq2Auras.Core.Timers;

namespace Eq2Auras.Plugin.Overlay
{
    public partial class CenterZoneWindow : Window
    {
        private sealed class RetainedElement
        {
            public CenterElementKind Kind;
            public PieVisual Pie;
            public LateVisual Late;
            public UIElement Root => Kind == CenterElementKind.Pie ? Pie.Root : Late.Root;
        }

        // Retained visuals keyed by timer identity — updated, never rebuilt, so drains
        // and pulses run continuously at display refresh.
        private readonly Dictionary<string, RetainedElement> _elements = new Dictionary<string, RetainedElement>();
        private readonly Grid _moveChrome;
        private readonly Action<double, double> _persistPosition;

        public CenterZoneWindow(string moveLabel, double left, double top, Action<double, double> persistPosition)
        {
            InitializeComponent();
            Left = left;
            Top = top;
            _persistPosition = persistPosition;

            _moveChrome = MoveChrome.Build(moveLabel);
            RootGrid.Children.Add(_moveChrome);

            SourceInitialized += (s, e) => ClickThrough.Set(this, true);
            MouseLeftButtonDown += OnDragStart;
        }

        public void SetMoveMode(bool moving)
        {
            _moveChrome.Visibility = moving ? Visibility.Visible : Visibility.Collapsed;
            ClickThrough.Set(this, !moving);
        }

        private void OnDragStart(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_moveChrome.Visibility != Visibility.Visible) return;
            DragMove();
            _persistPosition(Left, Top);
        }

        /// Called on the overlay's dispatcher thread with a fresh snapshot.
        public void RenderElements(List<CenterElement> elements)
        {
            var seen = new HashSet<string>();
            ElementsPanel.Children.Clear();   // same instances re-added in order — animations continue
            foreach (var element in elements)
            {
                var key = element.Name + "|" + element.Combatant;
                seen.Add(key);

                if (!_elements.TryGetValue(key, out var retained) || retained.Kind != element.Kind)
                {
                    retained = element.Kind == CenterElementKind.Pie
                        ? new RetainedElement { Kind = CenterElementKind.Pie, Pie = new PieVisual() }
                        : new RetainedElement { Kind = CenterElementKind.Late, Late = new LateVisual() };
                    _elements[key] = retained;
                }

                if (retained.Kind == CenterElementKind.Pie) retained.Pie.Update(element);
                else retained.Late.Update(element);

                ElementsPanel.Children.Add(retained.Root);
            }

            foreach (var stale in _elements.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                _elements.Remove(stale);
            }
        }
    }
}
```

- [ ] **Step 6: OverlayHost — one window pair per group.** Replace `src/eq2auras.Plugin/Overlay/OverlayHost.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Eq2Auras.Core.Config;
using Eq2Auras.Core.Timers;
using Eq2Auras.Plugin.SelfUpdate;

namespace Eq2Auras.Plugin.Overlay
{
    /// Hosts one window pair (list + center zone) per timer group on a dedicated STA
    /// thread. Positions come from PanelSettings (null -> built-in defaults, laid out
    /// non-overlapping); drag-end and re-lock persist them back via SettingsStore.
    public sealed class OverlayHost : IDisposable
    {
        private static readonly string[] PanelNames = { "Panel A", "Panel B" };

        private readonly Settings _settings;
        private readonly List<TimerListWindow> _listWindows = new List<TimerListWindow>();
        private readonly List<CenterZoneWindow> _centerWindows = new List<CenterZoneWindow>();
        private Thread _thread;
        private Dispatcher _dispatcher;

        public OverlayHost(Settings settings)
        {
            _settings = settings;
        }

        public void Start()
        {
            var ready = new ManualResetEventSlim(false);
            _thread = new Thread(() =>
            {
                _dispatcher = Dispatcher.CurrentDispatcher;
                for (int i = 0; i < _settings.Panels.Count; i++)
                {
                    CreatePanelWindows(i, _settings.Panels[i]);
                }
                ready.Set();
                Dispatcher.Run();
            });
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.IsBackground = true;
            _thread.Start();
            ready.Wait(TimeSpan.FromSeconds(5));
        }

        private void CreatePanelWindows(int index, PanelSettings panel)
        {
            string name = index < PanelNames.Length ? PanelNames[index] : "Panel " + (index + 1);

            var list = new TimerListWindow(
                name + " — list",
                panel.ListLeft ?? DefaultListLeft(index),
                panel.ListTop ?? DefaultListTop,
                (left, top) => SettingsStore.Update(_settings, () => { panel.ListLeft = left; panel.ListTop = top; }));
            list.Show();
            _listWindows.Add(list);

            var center = new CenterZoneWindow(
                name + " — escalation",
                panel.CenterLeft ?? DefaultCenterLeft(),
                panel.CenterTop ?? DefaultCenterTop(index),
                (left, top) => SettingsStore.Update(_settings, () => { panel.CenterLeft = left; panel.CenterTop = top; }));
            center.Show();
            _centerWindows.Add(center);
        }

        // Defaults (WPF DIPs, primary monitor): Panel A exactly where it has always
        // been; Panel B beside/below, non-overlapping. Rough placement is fine —
        // dragging is the real positioning mechanism (SPEC §Moving the overlay).
        private static double DefaultListLeft(int index) => 160 + index * 290;   // list width 260 + gap
        private const double DefaultListTop = 320;
        private static double DefaultCenterLeft() => (SystemParameters.PrimaryScreenWidth - 200) / 2;  // center width 200
        private static double DefaultCenterTop(int index) => SystemParameters.PrimaryScreenHeight * (0.38 + index * 0.18);

        /// Callable from any thread (the poll runs on ACT's UI thread).
        public void UpdateFrames(List<OverlayFrame> frames)
        {
            var dispatcher = _dispatcher;
            if (dispatcher == null) return;
            dispatcher.BeginInvoke((Action)(() =>
            {
                for (int i = 0; i < frames.Count && i < _listWindows.Count; i++)
                {
                    _listWindows[i].RenderRows(frames[i].ListRows);
                    _centerWindows[i].RenderElements(frames[i].CenterElements);
                }
            }));
        }

        /// Unlock shows EVERY window regardless of each group's EscalationStyle, so an
        /// unused center zone can be positioned before styles are flipped (SPEC).
        public void SetMoveMode(bool moving)
        {
            var dispatcher = _dispatcher;
            if (dispatcher == null) return;
            dispatcher.BeginInvoke((Action)(() =>
            {
                foreach (var window in _listWindows) window.SetMoveMode(moving);
                foreach (var window in _centerWindows) window.SetMoveMode(moving);
                if (!moving) SaveAllPositions();   // re-lock persists everything
            }));
        }

        private void SaveAllPositions()
        {
            SettingsStore.Update(_settings, () =>
            {
                for (int i = 0; i < _settings.Panels.Count && i < _listWindows.Count; i++)
                {
                    var panel = _settings.Panels[i];
                    panel.ListLeft = _listWindows[i].Left;
                    panel.ListTop = _listWindows[i].Top;
                    panel.CenterLeft = _centerWindows[i].Left;
                    panel.CenterTop = _centerWindows[i].Top;
                }
            });
        }

        public void Dispose()
        {
            if (_dispatcher == null) return;
            _dispatcher.Invoke(() =>
            {
                foreach (var window in _listWindows) window.Close();
                _listWindows.Clear();
                foreach (var window in _centerWindows) window.Close();
                _centerWindows.Clear();
            });
            _dispatcher.InvokeShutdown();
            _thread?.Join(TimeSpan.FromSeconds(2));
        }
    }
}
```

- [ ] **Step 7: OverlayTheme cleanup** — in `src/eq2auras.Plugin/Overlay/OverlayTheme.cs`, delete the `Palette` field and `BuildPalette()` method (the deleted preview strip was their only consumer). Everything else stays.

- [ ] **Step 8: Commit**

```bash
git add src/eq2auras.Plugin/Overlay/ src/eq2auras.Plugin/SelfUpdate/SettingsStore.cs
git commit -m "Plugin: per-group window pairs, unlock/move mode (chrome + DragMove + persisted positions), SettingsStore gate"
```

---

### Task 6: Probe flags + plugin wiring + per-panel tab [CI-only compile]

**Files:**
- Modify: `src/eq2auras.Plugin/Act/TimerProbe.cs`
- Modify: `src/eq2auras.Plugin/Eq2AurasPlugin.cs`

**Interfaces:**
- Consumes: `OverlayEngine` (Task 3), `OverlayHost(Settings)`/`UpdateFrames`/`SetMoveMode` and `SettingsStore.Update` (Task 5), `TimerReading.ShowInPanelA/B` (Task 2), `TimerSnapshotRecord.PanelA/B` (Task 4).

- [ ] **Step 1: TimerProbe carries the panel flags.** In `OnPoll`'s reading construction, add after `FillArgb`:

```csharp
                        FillArgb = data.FillColor.ToArgb(),
                        ShowInPanelA = data.Panel1Display,
                        ShowInPanelB = data.Panel2Display
```

In `LogReading`, add to the record initializer:

```csharp
                PanelA = reading.ShowInPanelA,
                PanelB = reading.ShowInPanelB
```

In `LogFrameEvent`, add to the record initializer:

```csharp
                PanelA = frame.TimerData != null && frame.TimerData.Panel1Display,
                PanelB = frame.TimerData != null && frame.TimerData.Panel2Display
```

- [ ] **Step 2: Plugin wiring — engine replaces tracker.** In `src/eq2auras.Plugin/Eq2AurasPlugin.cs`, replace the `_tracker` field with `private OverlayEngine _engine;`, and in `InitPlugin` replace the overlay/tracker/probe construction block with (settings load moves *before* the host, which now needs it):

```csharp
            _log = new JsonlLogWriter();
            _settings = SettingsStore.Load();
            _overlay = new OverlayHost(_settings);
            _overlay.Start();
            _engine = new OverlayEngine(_settings);   // trackers hold the same PanelSettings instances the tab mutates
            _probe = new TimerProbe(_log,
                readings => _overlay.UpdateFrames(
                    _engine.Tick(readings)));
```

In `DeInitPlugin`, replace `_tracker = null;` with `_engine = null;`.

- [ ] **Step 3: Tab — per-panel group boxes + move checkbox.** In `BuildConfigTab`, replace everything from `var colorLabel = ...` down to the last `tab.Controls.Add(styleBox);` with:

```csharp
            var panelABox = BuildPanelGroupBox("Panel A", _settings.Panels[0], 78);
            var panelBBox = BuildPanelGroupBox("Panel B", _settings.Panels[1], 176);

            var moveBox = new CheckBox { Text = "Move overlay windows", Left = 10, Top = 276, Width = 200 };
            moveBox.CheckedChanged += (s, e) => _overlay.SetMoveMode(moveBox.Checked);

            tab.Controls.Add(tokenBox);
            tab.Controls.Add(saveTokenButton);
            tab.Controls.Add(updateButton);
            tab.Controls.Add(panelABox);
            tab.Controls.Add(panelBBox);
            tab.Controls.Add(moveBox);
```

(The three `tab.Controls.Add` lines for token/save/update replace the existing ones — keep exactly one set.) Then add the builder method to the class:

```csharp
        /// One labeled control set per group (SPEC §Configuration — no group selector).
        /// Dropdown changes apply live within a poll tick: the engine's trackers hold
        /// the same PanelSettings instance this mutates, and knob handlers + poll share
        /// ACT's UI thread. Persistence goes through the SettingsStore gate.
        private GroupBox BuildPanelGroupBox(string title, PanelSettings panel, int top)
        {
            var box = new GroupBox { Text = title, Left = 10, Top = top, Width = 250, Height = 90 };

            var colorLabel = new Label { Text = "Colors:", Left = 8, Top = 26, Width = 70 };
            var colorBox = new ComboBox
            {
                Left = 82, Top = 22, Width = 150,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            colorBox.Items.AddRange(new object[] { "Palette", "Greyscale", "ACT colors" });
            colorBox.SelectedIndex = (int)panel.ColorSource;
            colorBox.SelectedIndexChanged += (s, e) =>
                SettingsStore.Update(_settings, () => panel.ColorSource = (ColorSource)colorBox.SelectedIndex);

            var styleLabel = new Label { Text = "Escalation:", Left = 8, Top = 58, Width = 70 };
            var styleBox = new ComboBox
            {
                Left = 82, Top = 54, Width = 150,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            styleBox.Items.AddRange(new object[] { "Center radial", "Highlight in place" });
            styleBox.SelectedIndex = (int)panel.EscalationStyle;
            styleBox.SelectedIndexChanged += (s, e) =>
                SettingsStore.Update(_settings, () => panel.EscalationStyle = (EscalationStyle)styleBox.SelectedIndex);

            box.Controls.Add(colorLabel);
            box.Controls.Add(colorBox);
            box.Controls.Add(styleLabel);
            box.Controls.Add(styleBox);
            return box;
        }
```

- [ ] **Step 4: Sanity check Core tests still green** (plugin can't compile here; Core sources are shared, so run):

```bash
dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj
```
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/eq2auras.Plugin/Act/TimerProbe.cs src/eq2auras.Plugin/Eq2AurasPlugin.cs
git commit -m "Plugin: probe carries panel flags; OverlayEngine wiring; per-panel tab group boxes + move checkbox"
```

---

### Task 7: Ship — push, CI, live verification **[WIN]**

- [ ] **Step 1: Push and watch CI** (this is also the plugin's first compile — expect possible fixups):

```bash
git push
gh run watch $(gh run list --limit 1 --json databaseId --jq '.[0].databaseId') --exit-status
```
Expected: `dotnet test` green, msbuild green, `dev-latest` prerelease updated. If msbuild fails, fix inline (likely candidates: XAML/codebehind name mismatches, missing usings) — each fixup is its own commit, re-push, re-watch.

- [ ] **Step 2: Live verification script for Alex** (guild as jury; ACT's Spell Timers window has per-timer "Panel 1 Display" / "Panel 2 Display" checkboxes):

1. **Update:** Check for updates → reload. Tab shows **Panel A / Panel B group boxes** + **"Move overlay windows"** checkbox.
2. **Migration:** your previous Colors/Escalation choices appear under **Panel A**; Panel B shows Palette/Center radial defaults. (`settings.json` now contains a `panels` array.)
3. **Routing:** fire four test triggers configured as Panel-1-only / Panel-2-only / both / neither → expect: A's list only / B's list only (new window, right of A) / **both lists** / **nowhere**.
4. **Color identity:** the both-flagged timer shows the **same color in both lists**. Set Panel B Colors = Greyscale → B's copy turns grey **live**, A's stays palette (same slot: first-fired name ↔ same-index grey).
5. **Independent escalation:** let an A-flagged timer cross its warning → pie in A's center zone (~38% down). Let a B-flagged one cross → pie in B's zone (~56% down, below A's). Set Panel B Escalation = Highlight in place → B's timer now stays in B's list with the gold outline; A still throws pies.
6. **Move mode:** tick the checkbox → **four** outlined/labeled surfaces appear (both center zones show chrome even when empty/HighlightInPlace); game clicks on them are captured, not passed through. Drag each somewhere deliberate. Untick → chrome gone, click-through restored.
7. **Persistence:** toggle the plugin off/on (or restart ACT) → all four windows reappear at the dragged positions; both panels' dropdowns remember their values.
8. Report guild verdicts (B-list default spot, chrome feel, zone defaults) → backlog.

- [ ] **Step 3: Backlog update** — move the NEXT UP entry to a "shipped (slice 4)" note with any live-verification findings, and pick the next NEXT UP with Alex.

---

## Notes for the executor

- Enum knob defaults MUST stay 0-values; position fields MUST stay `double?` (DCJS).
- `EscalationTracker.Tick` must never mutate input readings — the engine routes the same objects to both groups (regression tests in Task 2 enforce this).
- The `PaletteAssigner` lives in `OverlayEngine` only — never construct one per tracker outside tests.
- All settings writes (knobs *and* positions) go through `SettingsStore.Update` — never write the file directly.
- Tasks 1–4 are `[MAC]` TDD; Tasks 5–6 compile only in CI; Task 7 is `[WIN]` live verification.
