# Slice 5: Customization Knobs (Palette / Font / Scale) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task, **inline in the main session** (repo convention). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Three customization knobs — global custom color palette (variable 1–16, swatch UI), per-panel font (family + base size, native FontDialog), per-window geometry scale (corner grip in unlock mode) — per SPEC §Timer colors, §Typography, §Moving the overlay (branch `slice5-customization`, spec commits `bed84e7`/`2e50306`).

**Architecture:** Core: `Settings.PaletteArgb` (global list) + four new `PanelSettings` fields (`FontFamily`, `FontBaseSize`, `ListScale`, `CenterScale`); `ColorPolicy.Resolve` takes the palette as a parameter (constant renamed `DefaultPaletteArgb`); the palette flows **per-tick** (`OverlayEngine` → `Tick(readings, palette)`) so edits are live by construction. Plugin: a `VisualStyle` (scale + typography) parameterizes the retained visuals; windows rebuild them **once per knob change** (never per tick); the move chrome gains a resize grip with live ScaleTransform *preview* during drag and a real restyle + persist on release.

**Tech Stack:** existing (netstandard2.0 Core / net472 WPF / xUnit). DCJS only.

## Global Constraints

- Mac builds/tests **Core only**: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`. Plugin compiles only in CI (branch pushes = verify-only).
- DCJS: missing list → null → default; nullable numerics for scale/size (**null = default, never 0**); no `System.Web.Extensions`.
- **Scale is geometry-only; font is text-only** (SPEC: "scale sets how much space a window takes; font sets how readable its text is").
- **Retain-elements rule:** visuals rebuild on knob change only — never per tick.
- Reviewer plan-watch items (backlog NEXT UP) are woven in: point→DIP conversion (T5), six text roles (T3), grip `e.Handled` (T4), rebuild-once (T3/T4), every geometry constant scaled (T3), `Resolve(palette)` (T2) + `DefaultPaletteArgb` rename (T1 Step 3), no alpha handling (T5).
- Merge to `main` = release (Alex approves after branch review).

## File Structure

```
src/eq2auras.Core/
├── Config/Settings.cs               # + PaletteArgb (global list, normalize: null/empty->default, >16 truncate)
├── Config/PanelSettings.cs          # + FontFamily, FontBaseSize, ListScale, CenterScale (all null = default)
└── Timers/
    ├── ColorPolicy.cs               # PaletteArgb -> DefaultPaletteArgb; Resolve(..., palette)
    ├── EscalationTracker.cs         # Tick(readings, palette = null); WithResolvedColor threads it
    └── OverlayEngine.cs             # passes settings.PaletteArgb per tick
src/eq2auras.Plugin/Overlay/
├── VisualStyle.cs                   # NEW: scale + font + six role sizes + helpers
├── TimerRowVisual.cs                # ctor(VisualStyle): geometry × scale, fonts from style
├── CenterVisuals.cs                 # PieVisual/LateVisual ctor(VisualStyle): same
├── MoveChrome.cs                    # Build returns Chrome {Root, Grip} — grip bottom-right
├── TimerListWindow.xaml(.cs)        # style field + SetStyle (rebuild-once); grip drag→scale
├── CenterZoneWindow.xaml(.cs)       # same
└── OverlayHost.cs                   # StyleFor(panel), scale persist callbacks, RefreshStyles()
src/eq2auras.Plugin/Eq2AurasPlugin.cs # palette swatch row, per-panel Font… button, layout
tests/eq2auras.Core.Tests/
├── SettingsTests.cs                 # palette/font/scale round-trip + normalization
├── ColorPolicyTests.cs              # Resolve(palette) + rename
├── EscalationTrackerTests.cs        # custom-palette resolution
└── OverlayEngineTests.cs            # live palette swap between ticks
```

---

### Task 1: Settings — global palette + panel font/scale fields (TDD, Mac)

**Files:**
- Modify: `src/eq2auras.Core/Config/Settings.cs`, `src/eq2auras.Core/Config/PanelSettings.cs`
- Test: `tests/eq2auras.Core.Tests/SettingsTests.cs`

**Interfaces:**
- Produces: `Settings.PaletteArgb : List<int>` (never null/empty after `Parse`/ctor; ≤ `Settings.MaxPaletteSize` = 16); `PanelSettings.FontFamily : string` (null = default), `FontBaseSize : double?` (WPF DIPs, null = 13), `ListScale`/`CenterScale : double?` (null = 1.0; Normalize clamps stored values to 0.5–2.5).

- [ ] **Step 1: Failing tests** — append to `SettingsTests.cs`:

```csharp
    [Fact]
    public void Roundtrips_palette_font_and_scale()
    {
        var settings = new Settings();
        settings.PaletteArgb = new System.Collections.Generic.List<int> { -65536, -16711936 };
        settings.Panels[0].FontFamily = "Comic Sans MS";
        settings.Panels[0].FontBaseSize = 16.0;
        settings.Panels[1].ListScale = 1.5;
        settings.Panels[1].CenterScale = 0.75;

        var parsed = Settings.Parse(settings.ToJson());

        Assert.Equal(new[] { -65536, -16711936 }, parsed.PaletteArgb);
        Assert.Equal("Comic Sans MS", parsed.Panels[0].FontFamily);
        Assert.Equal(16.0, parsed.Panels[0].FontBaseSize);
        Assert.Equal(1.5, parsed.Panels[1].ListScale);
        Assert.Equal(0.75, parsed.Panels[1].CenterScale);
        Assert.Null(parsed.Panels[0].ListScale);          // unset stays null — never 0
        Assert.Null(parsed.Panels[1].FontFamily);
        Assert.Null(parsed.Panels[1].FontBaseSize);
    }

    [Theory]
    [InlineData("{}")]                          // no palette key
    [InlineData("{\"paletteArgb\":[]}")]        // empty list
    public void Missing_or_empty_palette_yields_the_default_five(string json)
    {
        var parsed = Settings.Parse(json);

        Assert.Equal(Eq2Auras.Core.Timers.ColorPolicy.DefaultPaletteArgb, parsed.PaletteArgb);
    }

    [Fact]
    public void Oversized_palette_truncates_to_max()
    {
        var seventeen = string.Join(",", new int[17]);
        var parsed = Settings.Parse("{\"paletteArgb\":[" + seventeen + "]}");

        Assert.Equal(16, parsed.PaletteArgb.Count);
    }

    [Fact]
    public void Out_of_range_scales_clamp_on_parse()
    {
        var parsed = Settings.Parse("{\"panels\":[{\"listScale\":9.0},{\"centerScale\":0.1}]}");

        Assert.Equal(2.5, parsed.Panels[0].ListScale);
        Assert.Equal(0.5, parsed.Panels[1].CenterScale);
    }

    [Fact]
    public void Valid_palette_survives_normalize_untouched()
    {
        // Normalize must never rebuild a valid list: the engine reads the property per
        // tick on ACT's UI thread while saves (which call ToJson -> Normalize) can run
        // on the overlay thread — gratuitous list replacement would be a cross-thread
        // mutation of a list being enumerated.
        var settings = new Settings();
        var palette = settings.PaletteArgb;

        settings.ToJson();

        Assert.Same(palette, settings.PaletteArgb);
    }
```

- [ ] **Step 2: Run red** — `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj` → FAIL (`PaletteArgb` missing). (The `DefaultPaletteArgb` reference also fails until Task 2's rename — implement the rename's *constant* half here: see Step 3.)

- [ ] **Step 3: Implement.** In `ColorPolicy.cs`, rename the constant only (full `Resolve` change is Task 2):

```csharp
        // Guild-approved default palette: sky, amber, teal, rose, indigo. The ACTIVE
        // palette is Settings.PaletteArgb (SPEC §Timer colors); this is its default.
        public static readonly int[] DefaultPaletteArgb =
```

and fix its two existing usages (`Resolve`'s default case, tests) mechanically: `PaletteArgb` → `DefaultPaletteArgb` everywhere it appears (`grep -rn "ColorPolicy.PaletteArgb" src tests`).

In `PanelSettings.cs`, add after `CenterTop`:

```csharp
        [DataMember(Name = "fontFamily")]
        public string FontFamily { get; set; }        // null = system default

        [DataMember(Name = "fontBaseSize")]
        public double? FontBaseSize { get; set; }     // WPF DIPs; null = 13 (today's look)

        [DataMember(Name = "listScale")]
        public double? ListScale { get; set; }        // null = 1.0; geometry-only

        [DataMember(Name = "centerScale")]
        public double? CenterScale { get; set; }
```

In `Settings.cs`, add the palette member + normalization (inside the class; `using System;` for `Math`):

```csharp
        public const int MaxPaletteSize = 16;
        public const double MinScale = 0.5;
        public const double MaxScale = 2.5;

        [DataMember(Name = "paletteArgb")]
        public List<int> PaletteArgb { get; set; } = DefaultPalette();

        private static List<int> DefaultPalette()
            => new List<int>(Timers.ColorPolicy.DefaultPaletteArgb);
```

and extend `Normalize()` (leave valid values untouched — see the test's threading rationale):

```csharp
            if (PaletteArgb == null || PaletteArgb.Count == 0) PaletteArgb = DefaultPalette();
            if (PaletteArgb.Count > MaxPaletteSize) PaletteArgb = PaletteArgb.Take(MaxPaletteSize).ToList();

            foreach (var panel in Panels)
            {
                if (OutOfRange(panel.ListScale)) panel.ListScale = ClampScale(panel.ListScale);
                if (OutOfRange(panel.CenterScale)) panel.CenterScale = ClampScale(panel.CenterScale);
            }
```

with (assign only when actually out of range — the normalize-untouched threading rationale applies to these fields too):

```csharp
        private static bool OutOfRange(double? scale)
            => scale.HasValue && (scale.Value < MinScale || scale.Value > MaxScale);

        private static double? ClampScale(double? scale)
            => scale.HasValue ? Math.Min(MaxScale, Math.Max(MinScale, scale.Value)) : (double?)null;
```

- [ ] **Step 4: Run green** → PASS, all tests.
- [ ] **Step 5: Commit** — `git add src/eq2auras.Core/ tests/ && git commit -m "Core: global PaletteArgb knob + per-panel font/scale fields (null-default, clamped, normalize-untouched-when-valid)"`

---

### Task 2: ColorPolicy.Resolve(palette) + per-tick threading (TDD, Mac)

**Files:**
- Modify: `src/eq2auras.Core/Timers/ColorPolicy.cs`, `EscalationTracker.cs`, `OverlayEngine.cs`
- Test: `tests/eq2auras.Core.Tests/ColorPolicyTests.cs`, `EscalationTrackerTests.cs`, `OverlayEngineTests.cs`

**Interfaces:**
- Produces: `ColorPolicy.Resolve(ColorSource, int paletteIndex, int actArgb, IReadOnlyList<int> palette = null)` (null/empty palette → `DefaultPaletteArgb`); `EscalationTracker.Tick(IReadOnlyList<TimerReading>, IReadOnlyList<int> paletteArgb = null)`; `OverlayEngine` reads `_settings.PaletteArgb` **per tick**.

- [ ] **Step 1: Failing tests** — append to `ColorPolicyTests.cs`:

```csharp
    [Fact]
    public void Resolve_uses_a_custom_palette_and_cycles_its_length()
    {
        var palette = new[] { 111, 222, 333 };

        Assert.Equal(222, ColorPolicy.Resolve(ColorSource.Palette, 1, 0, palette));
        Assert.Equal(111, ColorPolicy.Resolve(ColorSource.Palette, 3, 0, palette));  // 3 % 3
    }

    [Fact]
    public void Resolve_falls_back_to_the_default_palette_when_none_given()
    {
        Assert.Equal(ColorPolicy.DefaultPaletteArgb[0], ColorPolicy.Resolve(ColorSource.Palette, 0, 0));
        Assert.Equal(ColorPolicy.DefaultPaletteArgb[0], ColorPolicy.Resolve(ColorSource.Palette, 0, 0, new int[0]));
    }

    [Fact]
    public void Greyscale_ignores_the_custom_palette()
    {
        Assert.Equal(ColorPolicy.GreyArgb[1], ColorPolicy.Resolve(ColorSource.Greyscale, 1, 0, new[] { 111 }));
    }
```

to `EscalationTrackerTests.cs`:

```csharp
    [Fact]
    public void Tick_resolves_against_a_custom_palette()
    {
        var frame = new EscalationTracker().Tick(R(Reading("boss", 25)), new[] { 424242 });

        Assert.Equal(424242, frame.ListRows[0].FillArgb);
    }
```

to `OverlayEngineTests.cs`:

```csharp
    [Fact]
    public void Palette_edits_apply_on_the_next_tick()
    {
        var settings = new Settings();
        var engine = new OverlayEngine(settings);
        var readings = new List<TimerReading> { Reading("boss", 25, inA: true) };

        var before = engine.Tick(readings);
        settings.PaletteArgb[0] = 424242;              // live tab edit: same list, in place
        var after = engine.Tick(readings);

        Assert.Equal(ColorPolicy.DefaultPaletteArgb[0], before[0].ListRows[0].FillArgb);
        Assert.Equal(424242, after[0].ListRows[0].FillArgb);
    }
```

- [ ] **Step 2: Run red** → FAIL (signatures missing).

- [ ] **Step 3: Implement.** `ColorPolicy.Resolve` (add `using System.Collections.Generic;`):

```csharp
        public static int Resolve(ColorSource source, int paletteIndex, int actArgb, IReadOnlyList<int> palette = null)
        {
            IReadOnlyList<int> colors = palette != null && palette.Count > 0 ? palette : DefaultPaletteArgb;
            switch (source)
            {
                case ColorSource.Greyscale: return GreyArgb[paletteIndex % GreyArgb.Length];
                case ColorSource.ActColor: return Soften(actArgb);
                default: return colors[paletteIndex % colors.Count];
            }
        }
```

`EscalationTracker`: `Tick` gains the parameter and threads it into the copy method —

```csharp
        public OverlayFrame Tick(IReadOnlyList<TimerReading> readings, IReadOnlyList<int> paletteArgb = null)
```

```csharp
                .Select(r => WithResolvedColor(r, paletteArgb))
```

```csharp
        private TimerReading WithResolvedColor(TimerReading reading, IReadOnlyList<int> paletteArgb)
        {
            ...unchanged copy...
                FillArgb = ColorPolicy.Resolve(_settings.ColorSource, _palette.IndexFor(reading.Name), reading.FillArgb, paletteArgb)
            ...
        }
```

`OverlayEngine`: keep the `Settings` reference and pass the palette each tick —

```csharp
        private readonly Settings _settings;
        private readonly List<EscalationTracker> _trackers;

        public OverlayEngine(Settings settings)
        {
            _settings = settings ?? new Settings();
            _trackers = _settings.Panels
                .Select(panel => new EscalationTracker(panel, _palette))
                .ToList();
        }

        public List<OverlayFrame> Tick(IReadOnlyList<TimerReading> readings)
        {
            return _trackers
                .Select((tracker, i) => tracker.Tick(
                    readings.Where(r => RoutesTo(i, r)).ToList(),
                    _settings.PaletteArgb))
                .ToList();
        }
```

- [ ] **Step 4: Run green** → PASS, all tests.
- [ ] **Step 5: Commit** — `"Core: palette threads per-tick — Resolve(palette), Tick(readings, palette), engine reads Settings.PaletteArgb live"`

---

### Task 3: VisualStyle + parameterized renderers [CI-only compile]

**Files:**
- Create: `src/eq2auras.Plugin/Overlay/VisualStyle.cs`
- Modify: `src/eq2auras.Plugin/Overlay/TimerRowVisual.cs`, `CenterVisuals.cs`

**Interfaces:**
- Produces: `VisualStyle { double Scale; FontFamily Font; double BaseSize; double RowText/PieName/PieSeconds/LateTag/LateName; void ApplyFont(TextBlock, double) }`; `TimerRowVisual(VisualStyle)`, `PieVisual(VisualStyle)`, `LateVisual(VisualStyle)`.

- [ ] **Step 1: VisualStyle** — create `src/eq2auras.Plugin/Overlay/VisualStyle.cs`:

```csharp
using System.Windows.Controls;
using System.Windows.Media;

namespace Eq2Auras.Plugin.Overlay
{
    /// Per-window display style resolved from PanelSettings (SPEC §Typography,
    /// §Moving the overlay): Scale multiplies GEOMETRY only; typography derives every
    /// text role proportionally from BaseSize and never scales with the window.
    internal sealed class VisualStyle
    {
        public double Scale { get; set; } = 1.0;      // clamped 0.5–2.5 upstream
        public FontFamily Font { get; set; }          // null = system default
        public double BaseSize { get; set; } = 13.0;  // WPF DIPs

        // The six text roles (measured defaults: 13, 13, 34, 13, 22, 12).
        public double RowText => BaseSize;
        public double PieName => BaseSize;
        public double PieSeconds => BaseSize * 34.0 / 13.0;
        public double LateTag => BaseSize * 22.0 / 13.0;
        public double LateName => BaseSize * 12.0 / 13.0;

        public void ApplyFont(TextBlock text, double size)
        {
            if (Font != null) text.FontFamily = Font;
            text.FontSize = size;
        }
    }
}
```

- [ ] **Step 2: TimerRowVisual takes the style.** Every geometry constant multiplies by `Scale`; both text blocks go through `ApplyFont`. The changed parts (rest of the file unchanged):

```csharp
        private readonly VisualStyle _style;
        private readonly double _rowWidth;

        public TimerRowVisual(VisualStyle style)
        {
            _style = style;
            _rowWidth = RowWidth * style.Scale;

            _fill = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                CornerRadius = new CornerRadius(3 * style.Scale),
                BorderThickness = new Thickness(0, 0, 3 * style.Scale, 0)
            };
            _name = new TextBlock
            {
                Foreground = new SolidColorBrush(OverlayTheme.Text),
                Margin = new Thickness(8 * style.Scale, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            style.ApplyFont(_name, style.RowText);
            _time = new TextBlock
            {
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 8 * style.Scale, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            style.ApplyFont(_time, style.RowText);
            ...
            _root = new Border
            {
                Width = _rowWidth,
                Height = RowHeight * style.Scale,
                Margin = new Thickness(0, 0, 0, 4 * style.Scale),
                CornerRadius = new CornerRadius(4 * style.Scale),
                ...
            };
```

and the drain math swaps `RowWidth` for `_rowWidth`:

```csharp
            if (row.TotalSeconds <= 0) return;
            double pxPerSecond = (_rowWidth - 2) / row.TotalSeconds;
            double desired = Math.Max(0, Math.Min(1, row.PreciseTimeLeft / row.TotalSeconds)) * (_rowWidth - 2);
```

(`RowHeight`/`RowWidth` stay as the unscaled base constants; the `FontSize = 13` literals are deleted in favor of `ApplyFont`.)

- [ ] **Step 3: CenterVisuals take the style.** Pulses unchanged; the changed ctor lines:

```csharp
        public PieVisual(VisualStyle style)
        {
            double diameter = PieDiameter * style.Scale;
            _ring = new Ellipse { Width = diameter, Height = diameter, /* fill/stroke unchanged */ };
            _slice = new PieSlice { Width = diameter, Height = diameter };
            _seconds = new TextBlock { FontWeight = FontWeights.Bold, /* fg/alignment unchanged */ };
            style.ApplyFont(_seconds, style.PieSeconds);              // was FontSize = 34
            _name = new TextBlock { /* fg/alignment/trimming unchanged */ MaxWidth = 190 * style.Scale };
            style.ApplyFont(_name, style.PieName);                    // was FontSize = 13
            _root = new StackPanel { Margin = new Thickness(0, 0, 0, 10 * style.Scale), ... };
            ...
        }

        public LateVisual(VisualStyle style)
        {
            _late = new TextBlock { FontWeight = FontWeights.Bold, /* fg/alignment unchanged */ };
            style.ApplyFont(_late, style.LateTag);                    // was FontSize = 22
            _name = new TextBlock { /* fg/alignment/trimming unchanged */ };
            style.ApplyFont(_name, style.LateName);                   // was FontSize = 12
            _root = new Border
            {
                Width = 170 * style.Scale,
                Margin = new Thickness(0, 0, 0, 10 * style.Scale),
                Padding = new Thickness(10 * style.Scale, 6 * style.Scale, 10 * style.Scale, 6 * style.Scale),
                CornerRadius = new CornerRadius(6 * style.Scale),
                /* brushes/border unchanged */
            };
            ...
        }
```

- [ ] **Step 4: Core tests still green** (`dotnet test …`) → PASS (plugin not compiled here).
- [ ] **Step 5: Commit** — `"Plugin: VisualStyle — six text roles from base font, geometry × scale in all retained visuals"`

---

### Task 4: Grip + windows + host restyle [CI-only compile]

**Files:**
- Modify: `src/eq2auras.Plugin/Overlay/MoveChrome.cs`, `TimerListWindow.xaml.cs`, `CenterZoneWindow.xaml.cs`, `OverlayHost.cs`

**Interfaces:**
- Produces: `MoveChrome.Chrome { Grid Root; Border Grip; }` from `MoveChrome.Build(label)`; windows gain `SetStyle(VisualStyle)` (rebuild-once) and ctor param `(…, VisualStyle style, Action<double> persistScale)`; `OverlayHost.RefreshStyles()`.

- [ ] **Step 1: MoveChrome returns a grip.** Replace `Build`'s return with a small holder and add the grip (bottom-right, resize cursor):

```csharp
        internal sealed class Chrome
        {
            public Grid Root;
            public Border Grip;
        }

        public static Chrome Build(string label)
        {
            ...outline and chip as today...
            var grip = new Border
            {
                Width = 16, Height = 16,
                Background = new SolidColorBrush(Color.FromArgb(230, 86, 180, 233)),
                CornerRadius = new CornerRadius(3),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 2, 2),
                Cursor = System.Windows.Input.Cursors.SizeNWSE
            };

            var root = new Grid { MinHeight = 60, Visibility = Visibility.Collapsed };
            root.Children.Add(outline);
            root.Children.Add(chip);
            root.Children.Add(grip);
            return new Chrome { Root = root, Grip = grip };
        }
```

- [ ] **Step 2: Windows — style, restyle, grip drag.** Both windows get the same additions (shown once; apply to both, with `_rows`/`_elements` as the respective retained dictionary and `RowsPanel`/`ElementsPanel` as the content panel). Ctor signature becomes:

```csharp
        public TimerListWindow(string moveLabel, double left, double top, VisualStyle style,
            Action<double, double> persistPosition, Action<double> persistScale)
```

Fields/ctor body:

```csharp
        private VisualStyle _style;
        private readonly Action<double> _persistScale;
        private readonly MoveChrome.Chrome _chrome;
        private Point _gripStart;
        private double _dragStartScale;
        private bool _scaling;

        // in ctor:
            _style = style;
            Width = BaseWindowWidth * style.Scale;    // persisted scale applies at creation
            _persistScale = persistScale;
            _chrome = MoveChrome.Build(moveLabel);
            RootGrid.Children.Add(_chrome.Root);
            _chrome.Grip.MouseLeftButtonDown += OnGripDown;
            _chrome.Grip.MouseMove += OnGripMove;
            _chrome.Grip.MouseLeftButtonUp += OnGripUp;
```

(references to `_moveChrome` become `_chrome.Root`.) Rebuild-once restyle — the ONLY place visuals are rebuilt; ticks keep retaining:

```csharp
        private const double BaseWindowWidth = 260;   // CenterZoneWindow: 200

        /// Knob change (font/scale): drop the retained visuals once; the next tick
        /// recreates them under the new style. Pulses/drains restart once — accepted.
        public void SetStyle(VisualStyle style)
        {
            _style = style;
            Width = BaseWindowWidth * style.Scale;    // the WINDOW scales too, or content clips
            _rows.Clear();
            RowsPanel.Children.Clear();
        }
```

Visual creation in `RenderRows`/`RenderElements` passes the style: `new TimerRowVisual(_style)` / `new PieVisual(_style)` / `new LateVisual(_style)`. Grip drag — live *preview* via `LayoutTransform` (cheap), real restyle + persist on release; `e.Handled = true` keeps the window's `DragMove` handler out of it:

```csharp
        private void OnGripDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _scaling = true;
            _dragStartScale = _style.Scale;
            _gripStart = PointToScreen(e.GetPosition(this));
            _chrome.Grip.CaptureMouse();
            e.Handled = true;                       // never fall through to DragMove
        }

        private void OnGripMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_scaling) return;
            var now = PointToScreen(e.GetPosition(this));
            double factor = ProposedScale(now) / _style.Scale;
            RootGrid.LayoutTransform = new ScaleTransform(factor, factor);   // preview only
        }

        private void OnGripUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!_scaling) return;
            _scaling = false;
            _chrome.Grip.ReleaseMouseCapture();
            RootGrid.LayoutTransform = null;
            double newScale = ProposedScale(PointToScreen(e.GetPosition(this)));
            SetStyle(new VisualStyle { Scale = newScale, Font = _style.Font, BaseSize = _style.BaseSize });
            _persistScale(newScale);
            e.Handled = true;
        }

        private double ProposedScale(Point now)
        {
            double delta = ((now.X - _gripStart.X) + (now.Y - _gripStart.Y)) / 2.0;
            return Math.Min(Settings.MaxScale, Math.Max(Settings.MinScale, _dragStartScale + delta / 250.0));
        }
```

(`using Eq2Auras.Core.Config;` for the shared clamp constants — one source of truth with T1.)

**Preview trade-off (deliberate, transient):** during the drag, the `LayoutTransform` preview scales *everything including text*; on release the transform drops and the real geometry-only restyle applies, so **text visibly snaps back to its own size**. This transiently violates "text never scales with the window" — accepted because the alternatives (restyle-per-mousemove rebuild storms, or transforming only non-text elements mid-drag) are worse. The live script calls it out so nobody files the snap as a bug.

(add `using System.Windows.Media;` + `using System.Windows.Input;` as needed; `Math` via `System`.)

- [ ] **Step 3: OverlayHost builds styles and exposes restyle.**

```csharp
        private VisualStyle StyleFor(PanelSettings panel, bool isCenter)
        {
            return new VisualStyle
            {
                Scale = (isCenter ? panel.CenterScale : panel.ListScale) ?? 1.0,
                Font = panel.FontFamily != null ? new System.Windows.Media.FontFamily(panel.FontFamily) : null,
                BaseSize = panel.FontBaseSize ?? 13.0
            };
        }
```

`CreatePanelWindows` passes `StyleFor(panel, false)` / `StyleFor(panel, true)` plus persist-scale callbacks mirroring the position ones:

```csharp
                (scale) => SettingsStore.Update(_settings, () => panel.ListScale = scale));
                ...
                (scale) => SettingsStore.Update(_settings, () => panel.CenterScale = scale));
```

Both windows expose their live scale for the re-lock save (`public double CurrentScale => _style.Scale;`), and `SaveAllPositions` persists scales alongside positions — SPEC: "persisted exactly like positions (drag-end + re-lock)":

```csharp
                    panel.ListLeft = _listWindows[i].Left;
                    panel.ListTop = _listWindows[i].Top;
                    panel.ListScale = _listWindows[i].CurrentScale;
                    panel.CenterLeft = _centerWindows[i].Left;
                    panel.CenterTop = _centerWindows[i].Top;
                    panel.CenterScale = _centerWindows[i].CurrentScale;
``` New public method for tab-driven font changes:

```csharp
        /// Re-resolves every window's style from PanelSettings (font knob changed).
        public void RefreshStyles()
        {
            var dispatcher = _dispatcher;
            if (dispatcher == null) return;
            dispatcher.BeginInvoke((Action)(() =>
            {
                for (int i = 0; i < _settings.Panels.Count && i < _listWindows.Count; i++)
                {
                    _listWindows[i].SetStyle(StyleFor(_settings.Panels[i], false));
                    _centerWindows[i].SetStyle(StyleFor(_settings.Panels[i], true));
                }
            }));
        }
```

- [ ] **Step 4: Core tests still green** → PASS.
- [ ] **Step 5: Commit** — `"Plugin: resize grip (preview transform, restyle+persist on release), SetStyle rebuild-once, host StyleFor/RefreshStyles"`

---

### Task 5: Tab — palette swatches + per-panel Font button [CI-only compile]

**Files:**
- Modify: `src/eq2auras.Plugin/Eq2AurasPlugin.cs`

**Interfaces:**
- Consumes: `Settings.PaletteArgb`/`MaxPaletteSize` (T1), `ColorPolicy.DefaultPaletteArgb` (T1), `OverlayHost.RefreshStyles()` (T4), `SettingsStore.Update` (existing).

- [ ] **Step 1: Layout shift.** Group boxes grow to hold the Font row (`Height = 122`); Panel B moves to `Top = 208`; below them the palette row (`Top = 340`) and the move checkbox (`Top = 380`):

```csharp
            var panelABox = BuildPanelGroupBox("Panel A", _settings.Panels[0], 78);
            var panelBBox = BuildPanelGroupBox("Panel B", _settings.Panels[1], 208);

            var paletteLabel = new Label { Text = "Palette:", Left = 10, Top = 344, Width = 70 };
            // Wrap room for the max case: 16 swatches + 3 buttons flow onto two rows.
            _paletteRow = new FlowLayoutPanel { Left = 82, Top = 338, Width = 400, Height = 68 };
            RebuildPaletteRow();

            var moveBox = new CheckBox { Text = "Move overlay windows", Left = 10, Top = 416, Width = 200 };
            moveBox.CheckedChanged += (s, e) => _overlay.SetMoveMode(moveBox.Checked);

            tab.Controls.Add(tokenBox);
            tab.Controls.Add(saveTokenButton);
            tab.Controls.Add(updateButton);
            tab.Controls.Add(panelABox);
            tab.Controls.Add(panelBBox);
            tab.Controls.Add(paletteLabel);
            tab.Controls.Add(_paletteRow);
            tab.Controls.Add(moveBox);
```

with the field `private FlowLayoutPanel _paletteRow;`.

- [ ] **Step 2: Font row inside `BuildPanelGroupBox`** (box `Height = 122`; add after the escalation controls; native dialog speaks **points**, settings store **DIPs** — convert both ways):

```csharp
            var fontButton = new Button { Text = "Font…", Left = 8, Top = 86, Width = 70 };
            var fontLabel = new Label { Left = 82, Top = 90, Width = 160, Text = FontLabelText(panel) };
            fontButton.Click += (s, e) =>
            {
                using (var dialog = new FontDialog())
                {
                    var currentDip = panel.FontBaseSize ?? 13.0;
                    var currentFamily = panel.FontFamily ?? System.Drawing.SystemFonts.MessageBoxFont.Name;
                    dialog.Font = new System.Drawing.Font(currentFamily, (float)(currentDip * 72.0 / 96.0));
                    if (dialog.ShowDialog() != DialogResult.OK) return;

                    SettingsStore.Update(_settings, () =>
                    {
                        panel.FontFamily = dialog.Font.Name;
                        panel.FontBaseSize = dialog.Font.SizeInPoints * 96.0 / 72.0;   // points -> DIPs
                    });
                    fontLabel.Text = FontLabelText(panel);
                    _overlay.RefreshStyles();
                }
            };

            box.Controls.Add(fontButton);
            box.Controls.Add(fontLabel);
```

and the label helper (shows DIPs, matching the spec's "Segoe UI 13" example):

```csharp
        private static string FontLabelText(PanelSettings panel)
            => (panel.FontFamily ?? "default") + " " + Math.Round(panel.FontBaseSize ?? 13.0);
```

(`using System;` for `Math` if not present.)

- [ ] **Step 3: Palette row.** Swatch per color (click → `ColorDialog`), add/remove within 1–16, reset. `ColorDialog` has no alpha — colors arrive `0xFF`, matching the built-ins; **no alpha handling anywhere** (reviewer item 7). Edits mutate the existing list in place; color changes are live next tick (engine reads the palette per tick), so no `RefreshStyles` call here:

```csharp
        private void RebuildPaletteRow()
        {
            _paletteRow.Controls.Clear();

            for (int i = 0; i < _settings.PaletteArgb.Count; i++)
            {
                int index = i;
                var swatch = new Button
                {
                    Width = 30, Height = 26, FlatStyle = FlatStyle.Flat,
                    BackColor = System.Drawing.Color.FromArgb(_settings.PaletteArgb[index])
                };
                swatch.Click += (s, e) =>
                {
                    using (var dialog = new ColorDialog { Color = swatch.BackColor, FullOpen = true })
                    {
                        if (dialog.ShowDialog() != DialogResult.OK) return;
                        SettingsStore.Update(_settings, () => _settings.PaletteArgb[index] = dialog.Color.ToArgb());
                        swatch.BackColor = dialog.Color;
                    }
                };
                _paletteRow.Controls.Add(swatch);
            }

            var add = new Button { Text = "+", Width = 26, Height = 26, Enabled = _settings.PaletteArgb.Count < Settings.MaxPaletteSize };
            add.Click += (s, e) =>
            {
                SettingsStore.Update(_settings, () => _settings.PaletteArgb.Add(unchecked((int)0xFF808080)));
                RebuildPaletteRow();
            };

            var remove = new Button { Text = "−", Width = 26, Height = 26, Enabled = _settings.PaletteArgb.Count > 1 };
            remove.Click += (s, e) =>
            {
                SettingsStore.Update(_settings, () => _settings.PaletteArgb.RemoveAt(_settings.PaletteArgb.Count - 1));
                RebuildPaletteRow();
            };

            var reset = new Button { Text = "Reset", Width = 52, Height = 26 };
            reset.Click += (s, e) =>
            {
                SettingsStore.Update(_settings, () =>
                {
                    _settings.PaletteArgb.Clear();
                    _settings.PaletteArgb.AddRange(ColorPolicy.DefaultPaletteArgb);
                });
                RebuildPaletteRow();
            };

            _paletteRow.Controls.Add(add);
            _paletteRow.Controls.Add(remove);
            _paletteRow.Controls.Add(reset);
        }
```

(`using Eq2Auras.Core.Timers;` already present for `OverlayEngine`.)

- [ ] **Step 4: Core tests still green** → PASS. **Step 5: Commit** — `"Plugin: palette swatch row (+/−/reset, ColorDialog), per-panel Font button (FontDialog, points->DIPs), tab layout"`

---

### Task 6: Ship + live verification **[WIN]**

- [ ] **Step 1: Push branch; verify-only CI green** (publish/stamp skipped):

```bash
git push -u origin slice5-customization
gh run watch $(gh run list --branch slice5-customization --limit 1 --json databaseId --jq '.[0].databaseId') --exit-status
```

- [ ] **Step 2: Alex reviews** `git diff main..slice5-customization`; merge on approval → release → self-update in ACT.

- [ ] **Step 3: Live script (guild as jury):**
1. **Palette:** tab shows the 5 guild swatches. Click swatch 1 → pick bright red → a slot-0 timer turns red **live within a tick** (both panels if dual-flagged). **+** to 6 colors → sixth name takes the new color; **+** all the way to 16 → swatches wrap to a second row and the +/−/Reset buttons stay visible; **−** back; **Reset** → guild 5 return.
2. **Font:** Panel B "Font…" → pick something obvious (e.g. Georgia 16) → B's rows *and* B's center pies change family/size proportionally (pie seconds visibly larger than row text); Panel A untouched. Label reads "Georgia 21" (DIPs — 16 pt × 4/3).
3. **Scale:** unlock → each window's chrome shows the corner grip. Drag B-list's grip outward → everything grows smoothly **including text — that's the preview**; on release **text snaps back to its own size** while bars/heights stay bigger. The snap is correct behavior, not a bug (geometry-only scale applies at release). Drag A-center smaller. Body-drag still moves windows (grip didn't break `DragMove`).
4. **Persistence:** re-lock, toggle plugin off/on → custom palette, fonts, scales, positions all return. `settings.json` shows `paletteArgb`, `fontFamily`, `fontBaseSize`, `listScale`, `centerScale`.
5. **Regression sweep:** escalation still promotes at warning; drains stay smooth at non-1.0 scales (drain math scaled); LATE cards render at scale; greyscale mode unaffected by the custom palette.

- [ ] **Step 4: Backlog** — NEXT UP → shipped note with guild verdicts; branch deleted.
