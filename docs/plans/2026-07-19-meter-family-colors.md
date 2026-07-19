# Meter family-color scheme — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the meter's meaning-free per-ally palette fill with a monochromatic-per-window fill = the primary metric's family color, and collapse the meter's text to a two-tier white/subordinate-grey taxonomy.

**Architecture:** A new Core single-source-of-truth (`MeterFamilyColors`, category→ARGB) is consumed by both `MeterEngine` (row fill) and `MeterPopup` (family-column headers), so the two can never drift. `MeterEngine.Tick` drops its palette parameter and its `PaletteAssigner` and resolves each row's `FillArgb` from the resolved primary metric's `Category`. The Plugin's header/row text elements move off `TextMuted` onto `TextLabel`. The timer module is untouched — it keeps its own `PaletteAssigner` and `Settings.PaletteArgb`.

**Tech Stack:** C# / .NET — `eq2auras.Core` (netstandard2.0, xUnit, Mac-testable, strict TDD) + `eq2auras.Plugin` (net472/WPF, transcribe-only: CI-compile-verified, NOT runtime-verified on the Mac).

## Global Constraints

- **Single-assembly packaging.** The Plugin compiles Core in via the glob `..\eq2auras.Core\**\*.cs` (`eq2auras.Plugin.csproj:32-33`) — a new file under `src/eq2auras.Core/` is auto-included in both the Core build and the Plugin build; **no csproj edit is needed** (verified). Never reference a second DLL.
- **No `async` in the Plugin project.** (None introduced here.)
- **JSON via `DataContractJsonSerializer`; never `System.Web.Extensions`.** (Not touched here.)
- **ARGB int literals** use `unchecked((int)0xFFRRGGBB)` — the existing convention (`ColorPolicy.cs:11-19`).
- **Core tests are xUnit** (`[Fact]`/`[Theory]`, `Assert.Equal`), no namespace, matching `MeterEngineTests.cs`.
- **Core-only local build/test:** `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`. **Never** build the Plugin/solution on the Mac (net472+WPF) — the Plugin is verified by branch CI + Alex's on-box run.
- **Timer module stays byte-identical.** `Settings.PaletteArgb`, `ColorPolicy`, and the timer's `PaletteAssigner` (`OverlayEngine.cs:13`) are not touched; only the meter stops reading the palette.

## Consumer impact analysis

Single repo (`eq2auras`), no sibling repos. Exhaustive grep of the changing APIs:

- **`MeterEngine.Tick`** — callers: `OverlayHost.cs:228` (Plugin, updated in Task 2) + 24 `.Tick(...)` call sites in `MeterEngineTests.cs` (updated in Task 2). No others.
- **`OverlayHost.UpdateMeterSample`** — sole caller `Eq2AurasPlugin.cs:44` (updated in Task 2).
- **Meter `PaletteAssigner`** — used only in `MeterEngine.cs` (field `:15`, use `:91`). Isolated from the timer's.
- **`MeterPopup.CategoryColor` / `CategoryFallback`** — used only in `MeterPopup.FamilyHeader` (same file, updated in Task 3).
- **`Settings.PaletteArgb`** — retained: still read by timers (`OverlayEngine.cs:32`) and the config-tab palette editor (`Eq2AurasPlugin.cs:301-341`). Only the meter feed (`Eq2AurasPlugin.cs:44`) stops passing it.

## File structure

- **Create** `src/eq2auras.Core/Meter/MeterFamilyColors.cs` — the category→ARGB single source (Task 1).
- **Create** `tests/eq2auras.Core.Tests/MeterFamilyColorsTests.cs` — its tests (Task 1).
- **Modify** `src/eq2auras.Core/Meter/MeterEngine.cs` — family-color fill; drop palette param + `PaletteAssigner` (Task 2).
- **Modify** `tests/eq2auras.Core.Tests/MeterEngineTests.cs` — replace the slot-cycling test with family-color tests; drop the `Palette` arg everywhere (Task 2).
- **Modify** `src/eq2auras.Plugin/Overlay/OverlayHost.cs` + `src/eq2auras.Plugin/Eq2AurasPlugin.cs` — drop the palette arg from the meter feed (Task 2, transcribe-only).
- **Modify** `src/eq2auras.Plugin/Overlay/MeterPopup.cs` — headers consume `MeterFamilyColors`; delete the private dict (Task 3, transcribe-only).
- **Modify** `src/eq2auras.Plugin/Overlay/MeterWindow.cs` + `src/eq2auras.Plugin/Overlay/MeterRowVisual.cs` — text tier `TextMuted`→`TextLabel` (Task 3, transcribe-only).
- **Modify** `src/eq2auras.Core/Meter/MetricDef.cs` — refresh the `Category` comment (Task 3).

---

### Task 1: `MeterFamilyColors` — the single color source

**Files:**
- Create: `src/eq2auras.Core/Meter/MeterFamilyColors.cs`
- Test: `tests/eq2auras.Core.Tests/MeterFamilyColorsTests.cs`

**Interfaces:**
- Produces: `public static int MeterFamilyColors.ArgbFor(string category)` — returns the family ARGB for `"Damage"`/`"Healing"`/`"Utility"`, else a neutral grey fallback for null/unknown.

- [ ] **Step 1: Write the failing tests**

Create `tests/eq2auras.Core.Tests/MeterFamilyColorsTests.cs`:

```csharp
using Eq2Auras.Core.Meter;
using Xunit;

public class MeterFamilyColorsTests
{
    [Theory]
    [InlineData("Damage", unchecked((int)0xFFE05A5A))]
    [InlineData("Healing", unchecked((int)0xFF2FBF8F))]
    [InlineData("Utility", unchecked((int)0xFF56B4E9))]
    public void Known_categories_map_to_their_family_color(string category, int expectedArgb)
    {
        Assert.Equal(expectedArgb, MeterFamilyColors.ArgbFor(category));
    }

    [Theory]
    [InlineData("Threat")]
    [InlineData(null)]
    public void Unknown_or_null_category_falls_back_to_neutral_grey(string category)
    {
        Assert.Equal(unchecked((int)0xFF8B93A3), MeterFamilyColors.ArgbFor(category));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter "FullyQualifiedName~MeterFamilyColorsTests"`
Expected: FAIL to compile — `MeterFamilyColors` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/eq2auras.Core/Meter/MeterFamilyColors.cs`:

```csharp
using System.Collections.Generic;

namespace Eq2Auras.Core.Meter
{
    /// The single source of truth for metric family colors (SPEC Part III §The metric
    /// registry), keyed by MetricDef.Category. Consumed by the row fill (MeterEngine) and
    /// the popup's family-column headers so the two can never drift. The interim color
    /// model, pending row-color-by-class (SPEC Part III §Rows).
    public static class MeterFamilyColors
    {
        private static readonly int Fallback = unchecked((int)0xFF8B93A3);   // neutral grey for an uncategorized metric

        private static readonly IReadOnlyDictionary<string, int> ByCategory = new Dictionary<string, int>
        {
            { "Damage",  unchecked((int)0xFFE05A5A) },   // red
            { "Healing", unchecked((int)0xFF2FBF8F) },   // green/teal
            { "Utility", unchecked((int)0xFF56B4E9) },   // blue/sky
        };

        public static int ArgbFor(string category)
            => category != null && ByCategory.TryGetValue(category, out var argb) ? argb : Fallback;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter "FullyQualifiedName~MeterFamilyColorsTests"`
Expected: PASS (5 cases).

- [ ] **Step 5: Commit**

```bash
git add src/eq2auras.Core/Meter/MeterFamilyColors.cs tests/eq2auras.Core.Tests/MeterFamilyColorsTests.cs
git commit -m "Meter: add MeterFamilyColors — single category-to-color source"
```

---

### Task 2: `MeterEngine` fills rows by family color; palette removed

**Files:**
- Modify: `src/eq2auras.Core/Meter/MeterEngine.cs` (class doc `:8-12` + declaration `:13`, field `:15`, signature `:17-18`, fill `:91`)
- Modify: `tests/eq2auras.Core.Tests/MeterEngineTests.cs` (field `:7`, obsolete test `:124-142`, all `.Tick(...)` call sites)
- Modify (transcribe-only): `src/eq2auras.Plugin/Overlay/OverlayHost.cs:219-228`, `src/eq2auras.Plugin/Eq2AurasPlugin.cs:44`

**Interfaces:**
- Consumes: `MeterFamilyColors.ArgbFor(string)` (Task 1); `MetricDef.Category` (`MetricDef.cs:11`).
- Produces: `public MeterFrame Tick(EncounterReading encounter, List<CombatantReading> combatants, string metricKey, string secondaryKey = null)` — **no `paletteArgb` parameter**. Every `MeterRow.FillArgb` in a frame equals `MeterFamilyColors.ArgbFor(metric.Category)`.

- [ ] **Step 1: Write the failing tests (against the current signature)**

In `tests/eq2auras.Core.Tests/MeterEngineTests.cs`, replace the entire obsolete test method `Ally_colors_are_first_seen_stable_and_cycle_past_the_palette` (lines 124-142) with these two:

```csharp
    [Fact]
    public void All_rows_take_the_primary_metric_family_color()
    {
        var allies = new List<CombatantReading>
            { Ally("A", damage: 300), Ally("B", damage: 200), Ally("C", damage: 100) };

        var frame = new MeterEngine().Tick(Live(10), allies, "encdps", Palette);   // DPS -> Damage family

        var red = MeterFamilyColors.ArgbFor("Damage");
        Assert.All(frame.Rows, r => Assert.Equal(red, r.FillArgb));
    }

    [Fact]
    public void The_fill_color_follows_the_metric_family_not_the_ally()
    {
        var allies = new List<CombatantReading> { Ally("A", damage: 300, healed: 500) };

        var dps = new MeterEngine().Tick(Live(10), allies, "encdps", Palette);
        var hps = new MeterEngine().Tick(Live(10), allies, "enchps", Palette);

        Assert.Equal(MeterFamilyColors.ArgbFor("Damage"), dps.Rows[0].FillArgb);
        Assert.Equal(MeterFamilyColors.ArgbFor("Healing"), hps.Rows[0].FillArgb);
    }
```

- [ ] **Step 2: Run the new tests to verify they fail**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter "FullyQualifiedName~All_rows_take_the_primary_metric_family_color|FullyQualifiedName~The_fill_color_follows_the_metric_family_not_the_ally"`
Expected: FAIL — current impl assigns the palette (`0x11`), not the family color.

- [ ] **Step 3: Change the fill to the family color**

In `src/eq2auras.Core/Meter/MeterEngine.cs`, change the fill assignment (line 91):

```csharp
                row.FillArgb = MeterFamilyColors.ArgbFor(metric.Category);
```

(`metric` is already in scope from `var metric = MetricRegistry.ResolvePrimary(metricKey);` at the top of `Tick`.)

- [ ] **Step 4: Run the tests to verify the new ones pass**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter "FullyQualifiedName~MeterEngineTests"`
Expected: the two new tests PASS. (Other tests still pass; the `Palette` arg is now unused by the impl but still supplied — that is cleaned up next.)

- [ ] **Step 5: Remove the palette parameter and the `PaletteAssigner`**

In `src/eq2auras.Core/Meter/MeterEngine.cs`:

Replace the class doc comment through the class declaration and opening brace (lines 8-14) with:

```csharp
    /// The meter-side sibling of OverlayEngine (SPEC Part III §Assembly split):
    /// per-poll readings in, one renderable frame out. Row fill is the primary metric's
    /// family color (MeterFamilyColors, keyed by MetricDef.Category) — monochromatic per
    /// window, no palette (SPEC Part III §Rows).
    public sealed class MeterEngine
    {
```

Delete the field (line 15): `private readonly PaletteAssigner _palette = new PaletteAssigner();`

Change the signature (lines 17-18) to drop `paletteArgb`:

```csharp
        public MeterFrame Tick(EncounterReading encounter, List<CombatantReading> combatants,
            string metricKey, string secondaryKey = null)
```

- [ ] **Step 6: Drop the `Palette` argument from every test call site**

In `tests/eq2auras.Core.Tests/MeterEngineTests.cs`:
- Replace every occurrence of `, Palette)` with `)`.
- Replace every occurrence of `, Palette,` with `,`.
- Delete the now-unused field at line 7: `private static readonly List<int> Palette = new() { 0x11, 0x22, 0x33 };   // 3 slots`.

(These two replacements cover all shapes present: `"encdps", Palette)`, `"encdps", Palette, "enchps"`, `metricKey: null, Palette)`, `metricKey: null, Palette, "enchps"`.)

- [ ] **Step 7: Run the full Core suite**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
Expected: PASS (all green; the prior count + the net new family-color tests).

- [ ] **Step 8: Update the Plugin meter feed (transcribe-only — verified by CI, not on the Mac)**

In `src/eq2auras.Plugin/Overlay/OverlayHost.cs`, change the signature (lines 219-220) to drop the palette param:

```csharp
        public void UpdateMeterSample(EncounterReading encounter, List<CombatantReading> combatants)
```

and the `Tick` call (line 228) to drop the palette arg:

```csharp
                    var frame = _meterEngine.Tick(encounter, combatants, pair.Key.MetricKey, pair.Key.SecondaryKey);
```

In `src/eq2auras.Plugin/Eq2AurasPlugin.cs`, change the meter feed (line 44):

```csharp
                (encounter, combatants) => _overlay.UpdateMeterSample(encounter, combatants));
```

- [ ] **Step 9: Commit**

```bash
git add src/eq2auras.Core/Meter/MeterEngine.cs tests/eq2auras.Core.Tests/MeterEngineTests.cs src/eq2auras.Plugin/Overlay/OverlayHost.cs src/eq2auras.Plugin/Eq2AurasPlugin.cs
git commit -m "Meter: fill rows by metric family color; remove the meter palette

MeterEngine.Tick drops the paletteArgb param and its PaletteAssigner and
resolves FillArgb from the primary metric's category via MeterFamilyColors.
Meter feed (OverlayHost/Eq2AurasPlugin) stops passing Settings.PaletteArgb;
timers keep it. Slot-cycling test replaced with family-color tests."
```

---

### Task 3: Plugin consolidation — popup source + text tiers (transcribe-only)

Plugin/WPF only; **not** Mac-testable. Verified by branch CI compile + the on-box merge-gate script (Testing strategy below). No unit tests.

**Files:**
- Modify: `src/eq2auras.Plugin/Overlay/MeterPopup.cs` (dict `:27-33`, `FamilyHeader` `:147-158`)
- Modify: `src/eq2auras.Plugin/Overlay/MeterWindow.cs` (`HeaderBlock` `:218`, dim-true comment `:86`)
- Modify: `src/eq2auras.Plugin/Overlay/MeterRowVisual.cs` (secondary foreground `:59`)
- Modify: `src/eq2auras.Core/Meter/MetricDef.cs` (`Category` comment `:12`)

**Interfaces:**
- Consumes: `MeterFamilyColors.ArgbFor(string)` (Task 1); `OverlayTheme.FromArgbInt(int)` (`OverlayTheme.cs:29`); `Theme.TextLabel` (`Theme.cs:21`).

- [ ] **Step 1: Popup family headers read the Core source**

In `src/eq2auras.Plugin/Overlay/MeterPopup.cs`, delete the private dict + fallback (lines 27-33):

```csharp
        private static readonly Dictionary<string, Color> CategoryColor = new Dictionary<string, Color>
        {
            { "Damage",  Color.FromRgb(0xE0, 0x5A, 0x5A) },   // red
            { "Healing", Color.FromRgb(0x2F, 0xBF, 0x8F) },   // green/teal
            { "Utility", Color.FromRgb(0x56, 0xB4, 0xE9) },   // blue/sky
        };
        private static readonly Color CategoryFallback = Color.FromRgb(0x8B, 0x93, 0xA3);
```

Change `FamilyHeader` (the color line inside it, ~line 149) to resolve from the Core source:

```csharp
        private static UIElement FamilyHeader(string family)
        {
            var color = OverlayTheme.FromArgbInt(MeterFamilyColors.ArgbFor(family));
            return new TextBlock
            {
                Text = family,
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(color),
                Margin = new Thickness(6, 4, 6, 4)
            };
        }
```

Ensure `using Eq2Auras.Core.Meter;` is present in the file (it already references `MetricRegistry`, so it is).

- [ ] **Step 2: Header text tier — subordinate elements to `TextLabel`**

In `src/eq2auras.Plugin/Overlay/MeterWindow.cs`, change `HeaderBlock` (line 218):

```csharp
                Foreground = dim ? Theme.TextLabel : Theme.TextPrimary,
```

This lifts exactly the three `dim: true` header elements — `_durationText` (`:81`), `_secondaryLabelText` (`:86`), and the ⚙ cog `_affordance` (`:104`) — off `TextMuted` onto `TextLabel`; the `dim: false` title/metric-label/total stay white. Update the stale comment on line 86:

```csharp
            _secondaryLabelText = HeaderBlock(style, dim: true);   // secondary label — subordinate grey, matches the row's secondary column
```

- [ ] **Step 3: Row secondary column to `TextLabel`**

In `src/eq2auras.Plugin/Overlay/MeterRowVisual.cs`, the `_secondary` TextBlock foreground (line 59):

```csharp
                Foreground = Theme.TextLabel,
```

(The percent at line 49 is already `Theme.TextLabel`; name/value stay `OverlayTheme.Text` white — no change.)

- [ ] **Step 4: Refresh the `MetricDef.Category` comment**

In `src/eq2auras.Core/Meter/MetricDef.cs` (line 12):

```csharp
        public string Category { get; }        // picker grouping + family color (MeterFamilyColors) — a display attribute, never a dispatch axis
```

- [ ] **Step 5: Verify Core still builds/tests (guards the MetricDef edit)**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
Expected: PASS (the only Core change here is a comment).

- [ ] **Step 6: Commit**

```bash
git add src/eq2auras.Plugin/Overlay/MeterPopup.cs src/eq2auras.Plugin/Overlay/MeterWindow.cs src/eq2auras.Plugin/Overlay/MeterRowVisual.cs src/eq2auras.Core/Meter/MetricDef.cs
git commit -m "Meter: popup reads MeterFamilyColors; header+row text to subordinate tier

MeterPopup family headers resolve from the Core source (private dict deleted).
Header duration/secondary label/cog and the row secondary column move
TextMuted -> TextLabel (two-tier taxonomy). MetricDef.Category comment refreshed."
```

---

## Testing strategy

**Core (Mac, automated):** Tasks 1-2 are strict TDD — `MeterFamilyColorsTests` (family map + fallback) and the two new `MeterEngineTests` (all rows one family color; color follows metric not ally). Full suite green via `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`.

**Plugin (branch CI):** the branch push runs verify-only CI — Core tests + the WPF plugin compile + build artifact. Task 2 Step 8 and all of Task 3 are transcribe-only and gated by that compile (no runtime verification on the Mac).

**On-box merge-gate live script (Alex, Windows box, dev-latest after merge):**
1. Open a DPS meter → **every row is red**; names and values read cleanly (white) over the fill.
2. Right-click → switch primary to **HPS** → every row turns **green**; to **Cures** → every row turns **blue**. Colors match the popup's family-column headers.
3. Header check: **duration `(m:ss)`, the secondary label, and the ⚙ cog are the lighter grey** (same as the percent) — a visible *lightening* from the old dimmer `TextMuted` grey; **title, primary label, and total stay white**.
4. Set a secondary → the **secondary column reads in the same light grey as the percent** and stays legible on the rank-1 (fully filled) row.
5. Two windows, both DPS → **both red**; a DPS + HPS pair → **red + green** (color is identical per metric across windows — no per-ally variation).
6. **Clear the primary** → empty backdrop, no rows, no color (unchanged).
7. **Timer regression:** timer bars still use the configured palette; the config-tab palette editor (add/remove/reset/pick) still works and still recolors timers.

## Self-review

**Spec coverage** (SPEC Part III amendments → tasks):
- §The metric registry — family colors, single Core source → Task 1 (`MeterFamilyColors`), consumed by Task 2 (rows) + Task 3 (popup). ✓
- §Rows — monochromatic-per-window family fill; no `PaletteAssigner`/ally-color map; meter no longer reads `Settings.PaletteArgb` → Task 2. ✓
- §Header + §Meter display defaults (two-tier taxonomy: duration/secondary label/cog subordinate) → Task 3 Step 2. ✓
- §Rows secondary column in the subordinate tier → Task 3 Step 3. ✓
- §Configuration + §Multiple windows (popup headers from the one source; color identical per metric across windows) → Task 3 Step 1 (single source) + Task 2 (deterministic per-metric fill). ✓
- §Assembly split ("no palette" in the meter feed) → Task 2 Step 8. ✓
- Review nit: stale `MetricDef.cs:12` comment → Task 3 Step 4. ✓
- Timers untouched → Global Constraints + live-script step 7. ✓

**Placeholder scan:** none — every code step carries the exact code; every command carries expected output.

**Type consistency:** `MeterFamilyColors.ArgbFor(string) → int` used identically in Task 2 (`row.FillArgb = MeterFamilyColors.ArgbFor(metric.Category)`) and Task 3 (`OverlayTheme.FromArgbInt(MeterFamilyColors.ArgbFor(family))`). `Tick`'s new signature (`…, string metricKey, string secondaryKey = null`) matches the updated caller in Task 2 Step 8 and all test call sites in Step 6.
