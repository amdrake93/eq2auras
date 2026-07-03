# Slice 6: Element Dimensions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task, **inline in the main session** (repo convention). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Elements own their size — per-group `RowWidth`×`RowHeight` and `RadialSize` as numeric tab knobs — and the field-rejected resize grip is deleted entirely (SPEC §Element dimensions, branch `slice6-element-dimensions`, spec commits `b0e037a`/`238917e`).

**Architecture:** `PanelSettings` swaps `ListScale`/`CenterScale` for three nullable dimensions (null = 250/26/110, today's look). `VisualStyle` drops `Scale` for the dimensions plus derived ratios (`HeightRatio`, `RadialRatio`) and the **text-fit floor** (`EffectiveRowHeight`). Renderers read dimensions; windows compute their `Width` from them; the grip is a **deletion sweep across five files** enumerated below with the same rigor as the additions. Tab gains `NumericUpDown` fields per panel, live-applied via the existing `RefreshStyles` rebuild-once path. One `VisualStyle` per panel now serves both windows (`StyleFor` loses `isCenter`).

**Tech Stack:** existing. DCJS only; retired `listScale`/`centerScale` keys in field settings files are ignored by deserialization.

## Global Constraints

- Mac tests Core only; plugin compiles in branch CI (verify-only).
- Bounds are **shared `Settings` constants** (spec-pinned): row width 100–800, row height 16–100 (+text-fit floor on top), radial 40–400. `NumericUpDown` Min/Max AND `Normalize` both use them.
- Null = default (never 0); `Normalize` assigns **only when out of range** (cross-thread write discipline).
- Text never changes with element dimensions; rebuild on knob change only, never per tick.
- Plan-watch items (backlog NEXT UP) land: deletion sweep (T2–T4), radial-derived geometry enumerated (T2), text-fit floor as a real measure (T2), shared bounds constants (T1/T4), live script absorbs slice-5 leftovers (T5), `DefaultRowWidth`/`DefaultRowHeight` rename (T2).
- Merge to `main` = release (Alex's gate).

## The deletion sweep (grip retirement — complete inventory)

| File | Deletions |
|---|---|
| `PanelSettings.cs` | `ListScale`, `CenterScale` properties |
| `Settings.cs` | `MinScale`/`MaxScale` constants; the scale-clamp block in `Normalize`; `OutOfRange`/`ClampScale` (replaced by generalized dimension versions) |
| `SettingsTests.cs` | scale assertions in the round-trip test; `Out_of_range_scales_clamp_on_parse` |
| `VisualStyle.cs` | `Scale` property and every `× Scale` consumer (moves to ratio derivations) |
| `TimerListWindow.xaml.cs` / `CenterZoneWindow.xaml.cs` | `_gripStart`, `_dragStartScale`, `_scaling`, `_persistScale` field + ctor param, `CurrentScale`, `OnGripDown`/`OnGripMove`/`OnGripUp`, `ProposedScale`, the preview `LayoutTransform`, grip event wiring, `using Eq2Auras.Core.Config;` if unused after; **list window's `BaseWindowWidth` const** (replaced by `WindowSlack`); center's `BaseWindowWidth` renames to `BaseCenterWidth` and stays as the width formula's base |
| `MoveChrome.cs` | the `Chrome` holder class and the grip element (back to returning `Grid`) |
| `OverlayHost.cs` | persist-scale callbacks in `CreatePanelWindows`, scale lines in `SaveAllPositions`, `Scale =` in `StyleFor`, the `isCenter` parameter (one style serves both windows now) |

Verification of the sweep (T4 Step 4): `grep -rn "Scale" src/eq2auras.Plugin/ src/eq2auras.Core/` → only `ScaleTransform`-free, scale-free results (expected: zero hits besides unrelated words; check output manually).

---

### Task 1: Settings — dimensions replace scales (TDD, Mac)

**Files:**
- Modify: `src/eq2auras.Core/Config/PanelSettings.cs`, `src/eq2auras.Core/Config/Settings.cs`
- Test: `tests/eq2auras.Core.Tests/SettingsTests.cs`

**Interfaces:**
- Produces: `PanelSettings.RowWidth/RowHeight/RadialSize : double?` (null = default); `Settings.MinRowWidth=100, MaxRowWidth=800, MinRowHeight=16, MaxRowHeight=100, MinRadialSize=40, MaxRadialSize=400`; `ListScale`/`CenterScale` GONE.

- [ ] **Step 1: Failing tests.** In `SettingsTests.cs`: **delete** `Out_of_range_scales_clamp_on_parse`; **rewrite** `Roundtrips_palette_font_and_scale` as:

```csharp
    [Fact]
    public void Roundtrips_palette_font_and_dimensions()
    {
        var settings = new Settings();
        settings.PaletteArgb = new System.Collections.Generic.List<int> { -65536, -16711936 };
        settings.Panels[0].FontFamily = "Comic Sans MS";
        settings.Panels[0].FontBaseSize = 16.0;
        settings.Panels[0].RowWidth = 300.0;
        settings.Panels[0].RowHeight = 40.0;
        settings.Panels[1].RadialSize = 200.0;

        var parsed = Settings.Parse(settings.ToJson());

        Assert.Equal(new[] { -65536, -16711936 }, parsed.PaletteArgb);
        Assert.Equal("Comic Sans MS", parsed.Panels[0].FontFamily);
        Assert.Equal(16.0, parsed.Panels[0].FontBaseSize);
        Assert.Equal(300.0, parsed.Panels[0].RowWidth);
        Assert.Equal(40.0, parsed.Panels[0].RowHeight);
        Assert.Equal(200.0, parsed.Panels[1].RadialSize);
        Assert.Null(parsed.Panels[0].RadialSize);          // unset stays null — never 0
        Assert.Null(parsed.Panels[1].RowWidth);
        Assert.Null(parsed.Panels[1].FontFamily);
    }
```

and append:

```csharp
    [Fact]
    public void Out_of_range_dimensions_clamp_on_parse()
    {
        var parsed = Settings.Parse(
            "{\"panels\":[{\"rowWidth\":9999,\"rowHeight\":5},{\"radialSize\":10}]}");

        Assert.Equal(800.0, parsed.Panels[0].RowWidth);
        Assert.Equal(16.0, parsed.Panels[0].RowHeight);
        Assert.Equal(40.0, parsed.Panels[1].RadialSize);
    }

    [Fact]
    public void Retired_scale_keys_are_ignored()
    {
        var parsed = Settings.Parse("{\"panels\":[{\"listScale\":1.5},{\"centerScale\":0.7}]}");

        Assert.Equal(2, parsed.Panels.Count);              // parses fine, keys dropped
        Assert.Null(parsed.Panels[0].RowWidth);
    }
```

- [ ] **Step 2: Run red** — `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj` → FAIL (`RowWidth` missing; `ListScale` still referenced by the old test until rewritten — make sure the rewrite landed).

- [ ] **Step 3: Implement.** `PanelSettings.cs`: **delete** `ListScale`/`CenterScale`; add in their place:

```csharp
        [DataMember(Name = "rowWidth")]
        public double? RowWidth { get; set; }         // null = 250 (SPEC §Element dimensions)

        [DataMember(Name = "rowHeight")]
        public double? RowHeight { get; set; }        // null = 26; text-fit floor applies on top

        [DataMember(Name = "radialSize")]
        public double? RadialSize { get; set; }       // null = 110 (pie diameter)
```

`Settings.cs`: **delete** `MinScale`/`MaxScale`, the scale block in `Normalize`, and `OutOfRange`/`ClampScale`; add:

```csharp
        public const double MinRowWidth = 100, MaxRowWidth = 800;
        public const double MinRowHeight = 16, MaxRowHeight = 100;
        public const double MinRadialSize = 40, MaxRadialSize = 400;
```

and in `Normalize`, in the old block's place (assign only when out of range — cross-thread write discipline, matching the shipped slice-5 pattern):

```csharp
            foreach (var panel in Panels)
            {
                if (OutOfRange(panel.RowWidth, MinRowWidth, MaxRowWidth))
                    panel.RowWidth = Math.Min(MaxRowWidth, Math.Max(MinRowWidth, panel.RowWidth.Value));
                if (OutOfRange(panel.RowHeight, MinRowHeight, MaxRowHeight))
                    panel.RowHeight = Math.Min(MaxRowHeight, Math.Max(MinRowHeight, panel.RowHeight.Value));
                if (OutOfRange(panel.RadialSize, MinRadialSize, MaxRadialSize))
                    panel.RadialSize = Math.Min(MaxRadialSize, Math.Max(MinRadialSize, panel.RadialSize.Value));
            }
```

with the generalized guard:

```csharp
        private static bool OutOfRange(double? value, double min, double max)
            => value.HasValue && (value.Value < min || value.Value > max);
```

- [ ] **Step 4: Run green** → PASS, all tests.
- [ ] **Step 5: Commit** — `"Core: element dimensions replace window scales — RowWidth/RowHeight/RadialSize, shared bounds, scales deleted"`

---

### Task 2: VisualStyle + renderers — dimensions in, Scale out [CI-only compile]

**Files:**
- Modify: `src/eq2auras.Plugin/Overlay/VisualStyle.cs`, `TimerRowVisual.cs`, `CenterVisuals.cs`

**Interfaces:**
- Produces: `VisualStyle { RowWidth; RowHeight; RadialSize; Font; BaseSize; EffectiveRowHeight; HeightRatio; RadialRatio; DefaultRowWidth=250; DefaultRowHeight=26; DefaultRadialSize=110; RowTextPadding=6; six text roles; ApplyFont }` — **no `Scale`**.
- Radial-derived geometry enumerated: pie-name `MaxWidth` = 190 × `RadialRatio`; pie root margin = 10 × `RadialRatio`; LATE width = 170 × `RadialRatio`; LATE padding = (10, 6) × `RadialRatio`; LATE corner radius = 6 × `RadialRatio`; LATE margin = 10 × `RadialRatio`.
- Row-derived geometry: fill corner radius 3 ×, spark 3 ×, row corner radius 4 ×, name margin 8 ×, row bottom margin 4 × — all × `HeightRatio`; drain math from `RowWidth`.

- [ ] **Step 1: Rewrite `VisualStyle.cs`:**

```csharp
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Eq2Auras.Plugin.Overlay
{
    /// Per-panel display style resolved from PanelSettings (SPEC §Element dimensions,
    /// §Typography): elements own their size; text derives from BaseSize only and
    /// never changes with element dimensions. One instance serves both of a panel's
    /// windows.
    public sealed class VisualStyle
    {
        public const double DefaultRowWidth = 250;
        public const double DefaultRowHeight = 26;
        public const double DefaultRadialSize = 110;
        public const double RowTextPadding = 6;   // text-fit floor = line height + this

        public double RowWidth { get; set; } = DefaultRowWidth;
        public double RowHeight { get; set; } = DefaultRowHeight;
        public double RadialSize { get; set; } = DefaultRadialSize;
        public FontFamily Font { get; set; }          // null = system default
        public double BaseSize { get; set; } = 13.0;  // WPF DIPs

        // The six text roles (measured defaults: 13, 13, 34, 13, 22, 12).
        public double RowText => BaseSize;
        public double PieName => BaseSize;
        public double PieSeconds => BaseSize * 34.0 / 13.0;
        public double LateTag => BaseSize * 22.0 / 13.0;
        public double LateName => BaseSize * 12.0 / 13.0;

        /// Text-fit floor (SPEC §Element dimensions): a row is never shorter than its
        /// own text line plus padding, whatever the configured height says.
        public double EffectiveRowHeight
            => Math.Max(RowHeight, TextLineHeight + RowTextPadding);

        public double HeightRatio => EffectiveRowHeight / DefaultRowHeight;
        public double RadialRatio => RadialSize / DefaultRadialSize;

        private double TextLineHeight
            => (Font ?? SystemFonts.MessageFontFamily).LineSpacing * RowText;

        public void ApplyFont(TextBlock text, double size)
        {
            if (Font != null) text.FontFamily = Font;
            text.FontSize = size;
        }
    }
}
```

- [ ] **Step 2: `TimerRowVisual`** — rename the private constants (`RowWidth`→gone: use the style; the class keeps NO dimension constants — they live in `VisualStyle.Default*` now). Changed lines only:

```csharp
        private readonly double _rowWidth;
        // ctor:
            _rowWidth = style.RowWidth;
            double hr = style.HeightRatio;
            _fill = new Border { ..., CornerRadius = new CornerRadius(3 * hr), BorderThickness = new Thickness(0, 0, 3 * hr, 0) };
            _name Margin = new Thickness(8 * hr, 0, 0, 0);
            _time Margin = new Thickness(0, 0, 8 * hr, 0);
            _root = new Border { Width = _rowWidth, Height = style.EffectiveRowHeight,
                Margin = new Thickness(0, 0, 0, 4 * hr), CornerRadius = new CornerRadius(4 * hr), ... };
```

(drain math already reads `_rowWidth` — unchanged from slice 5.)

- [ ] **Step 3: `CenterVisuals`** — `PieVisual`: `double diameter = style.RadialSize;`, name `MaxWidth = 190 * style.RadialRatio`, root `Margin = new Thickness(0, 0, 0, 10 * style.RadialRatio)`. `LateVisual`: `double rr = style.RadialRatio;` → `Width = 170 * rr`, `Margin = (0,0,0,10*rr)`, `Padding = (10*rr, 6*rr, 10*rr, 6*rr)`, `CornerRadius = 6 * rr`. Fonts unchanged (already role-driven).

- [ ] **Step 4: Core tests still green** → PASS. **Step 5: Commit** — `"Plugin: VisualStyle carries element dimensions (text-fit floor, ratio derivations); Scale deleted from renderers"`

---

### Task 3: Windows + MoveChrome + OverlayHost — grip deletion, width formulas [CI-only compile]

**Files:**
- Modify: `src/eq2auras.Plugin/Overlay/MoveChrome.cs`, `TimerListWindow.xaml.cs`, `CenterZoneWindow.xaml.cs`, `OverlayHost.cs`

**Interfaces:**
- Produces: `MoveChrome.Build(label) : Grid` (holder class + grip deleted); window ctors `(string moveLabel, double left, double top, VisualStyle style, Action<double,double> persistPosition)`; `SetStyle(VisualStyle)` retained (rebuild-once + width); `OverlayHost.StyleFor(PanelSettings) : VisualStyle` (no `isCenter`); `RefreshStyles()` retained.

- [ ] **Step 1: MoveChrome** — delete the `Chrome` class and grip element; `Build` returns the `Grid` (outline + chip only), signature `public static Grid Build(string label)`. Remove `using System.Windows.Input;` if unused.

- [ ] **Step 2: Both windows** — apply the deletion-sweep rows for these files (fields `_gripStart`/`_dragStartScale`/`_scaling`/`_persistScale`, `CurrentScale`, the three grip handlers, `ProposedScale`, grip event wiring, ctor param `persistScale`, `using Eq2Auras.Core.Config;`, `using System.Windows.Media;` if then unused). `_chrome` field type returns to `Grid _moveChrome`. Width formulas (ctor AND `SetStyle` — both windows):

```csharp
        // TimerListWindow — BaseWindowWidth const DELETED, replaced by:
        private const double WindowSlack = 10;
        // ctor + SetStyle (today: 250 + 10 = 260):
        Width = style.RowWidth + WindowSlack;

        // CenterZoneWindow — BaseWindowWidth RENAMED, stays as the formula's base:
        private const double BaseCenterWidth = 200;
        // ctor + SetStyle (today: 110 × 200/110 = 200):
        Width = style.RadialSize * BaseCenterWidth / VisualStyle.DefaultRadialSize;
```

`SetStyle` bodies otherwise unchanged (assign style, clear retained dict + panel).

- [ ] **Step 3: OverlayHost** — `StyleFor` loses `isCenter` and scale:

```csharp
        private static VisualStyle StyleFor(PanelSettings panel)
        {
            return new VisualStyle
            {
                RowWidth = panel.RowWidth ?? VisualStyle.DefaultRowWidth,
                RowHeight = panel.RowHeight ?? VisualStyle.DefaultRowHeight,
                RadialSize = panel.RadialSize ?? VisualStyle.DefaultRadialSize,
                Font = panel.FontFamily != null ? new System.Windows.Media.FontFamily(panel.FontFamily) : null,
                BaseSize = panel.FontBaseSize ?? 13.0
            };
        }
```

`CreatePanelWindows`: both windows get `StyleFor(panel)`; the persist-scale lambdas are **deleted** (ctor calls drop to five args). `SaveAllPositions`: delete the two `…Scale = …CurrentScale` lines. `RefreshStyles`: `SetStyle(StyleFor(_settings.Panels[i]))` for both windows.

- [ ] **Step 4: Core tests green**; sweep check: `grep -rn "Scale" src/ | grep -v RadialSize` → expect ZERO scale remnants (inspect output). **Step 5: Commit** — `"Plugin: grip deletion sweep — move-only unlock, width from dimensions, one style per panel"`

---

### Task 4: Tab — numeric dimension fields [CI-only compile]

**Files:**
- Modify: `src/eq2auras.Plugin/Eq2AurasPlugin.cs`

**Interfaces:**
- Consumes: `Settings.Min*/Max*` bounds (T1), `VisualStyle.Default*` (T2), `OverlayHost.RefreshStyles()` (T3).

- [ ] **Step 1: Group box grows** (`Height = 122` → `186`); layout shifts: Panel B `Top = 208` → `272`; palette label/row `Top = 344/338` → `474/468`; moveBox `Top = 416` → `546`. The layout now needs ~580 px — add scroll insurance in `BuildConfigTab`, since a `TabPage` doesn't scroll by default and ACT's window can be shorter:

```csharp
            tab.AutoScroll = true;
```

- [ ] **Step 2: Dimension rows in `BuildPanelGroupBox`** (after the font row; a small helper keeps the three spinners uniform):

```csharp
            var rowLabel = new Label { Text = "Row:", Left = 8, Top = 122, Width = 40 };
            var rowWidthBox = DimensionBox(52, 118, panel.RowWidth ?? VisualStyle.DefaultRowWidth,
                Settings.MinRowWidth, Settings.MaxRowWidth,
                v => panel.RowWidth = v);
            var xLabel = new Label { Text = "×", Left = 116, Top = 122, Width = 14 };
            var rowHeightBox = DimensionBox(132, 118, panel.RowHeight ?? VisualStyle.DefaultRowHeight,
                Settings.MinRowHeight, Settings.MaxRowHeight,
                v => panel.RowHeight = v);

            var radialLabel = new Label { Text = "Radial:", Left = 8, Top = 154, Width = 44 };
            var radialBox = DimensionBox(52, 150, panel.RadialSize ?? VisualStyle.DefaultRadialSize,
                Settings.MinRadialSize, Settings.MaxRadialSize,
                v => panel.RadialSize = v);

            box.Controls.Add(rowLabel);
            box.Controls.Add(rowWidthBox);
            box.Controls.Add(xLabel);
            box.Controls.Add(rowHeightBox);
            box.Controls.Add(radialLabel);
            box.Controls.Add(radialBox);
```

with the helper on the class (wire `ValueChanged` AFTER setting `Value`, or the initial set fires a save):

```csharp
        /// One numeric dimension knob: bounds are the shared Settings constants
        /// (enforced here AND in Normalize), live-applied via the rebuild-once path.
        private NumericUpDown DimensionBox(int left, int top, double value,
            double min, double max, Action<double> assign)
        {
            var box = new NumericUpDown
            {
                Left = left, Top = top, Width = 60,
                Minimum = (decimal)min, Maximum = (decimal)max,
                Value = (decimal)value
            };
            box.ValueChanged += (s, e) =>
            {
                SettingsStore.Update(_settings, () => assign((double)box.Value));
                _overlay.RefreshStyles();
            };
            return box;
        }
```

(`using System;` already present.)

- [ ] **Step 3: Core tests green.** **Step 4: Commit** — `"Plugin: per-panel numeric dimension knobs (Row WxH, Radial size), tab layout"`

---

### Task 5: Ship + live verification **[WIN]**

- [ ] **Step 1: Push; branch CI green** (`git push -u origin slice6-element-dimensions`, watch the run; publish/stamp skipped).
- [ ] **Step 2: Alex reviews** `git diff main..slice6-element-dimensions`; merge on approval → release.
- [ ] **Step 3: Live script** (includes slice 5's leftover verification per the backlog promise):
1. **Dimensions:** Panel A Row 400×40 → bars widen/thicken live, text size unchanged, window follows; Row height 16 with default font → rows stop at the text-fit floor (taller than 16); Radial 200 → big pie, LATE card scales with it; Panel B stays at defaults.
2. **Persistence:** plugin off/on → dimensions return; `settings.json` has `rowWidth`/`rowHeight`/`radialSize`, no `listScale`/`centerScale` after the first save.
3. **Slice-5 leftovers:** palette — swatch recolor live, + to 16 (row wraps, buttons visible), Reset → guild 5; font — Panel B Georgia 16 pt → label "Georgia 21", proportions hold, A untouched; unlock/move still drags and persists (and there is **no grip** on the chrome).
4. **Regression:** escalation promotes at warning; drains smooth at non-default row widths; greyscale unaffected; both panels route independently.
- [ ] **Step 4: Backlog** — slice 6 shipped note (+ slice 5 verification closed), NEXT UP re-triage with Alex.
