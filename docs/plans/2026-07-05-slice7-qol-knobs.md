# Slice 7: QoL Knobs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task, **inline in the main session** (repo convention). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Four field-driven knob changes — per-window grow direction (anchored-edge model), flat row spacing, LATE typography respecting the font, and the font label speaking points — per SPEC §Window growth, §Element dimensions, §Typography (branch `slice7-qol-knobs`, spec commits `b004dff`/`12a5491`/`5ec7afc`).

**Architecture:** Core: `GrowDirection` enum (Down=0) + `ListGrowDirection`/`CenterGrowDirection`/`RowSpacing` on `PanelSettings` with shared clamp constants. Plugin: windows learn the anchored-edge state machine — initial `Top` = the stored anchor (correct for both directions since creation height ≈ 0), `SizeChanged` compensates `Top` by the height delta when growing Up (suppressed mid-drag), drag-end/re-lock persist `AnchorY` (= `Top + ActualHeight` under Up), and a knob flip converts-and-persists from on-screen geometry so windows never move. `VisualStyle` drops the LATE boosts and carries `RowSpacing`; the tab gains two grow dropdowns + a spacing spinner per panel and the points-speaking font label.

**Tech Stack:** existing. DCJS rules honored: `GrowDirection.Down = 0`; `RowSpacing` nullable (missing → null = 4; `0` is a meaningful legal value).

## Global Constraints

- Mac tests Core only; plugin compiles in branch CI (verify-only). Merge to `main` = release (Alex's gate).
- **The knob changes how a window grows, never where it is** — flips convert-and-persist the anchored edge from actual on-screen geometry, even from null.
- Plan-watch items (backlog NEXT UP) land: anchored-edge persistence at the named call sites (T3), mid-drag suppression + drag-end reconciliation (T3), DCJS rules (T1), `LateName` as changed behavior (T2 — **no plugin test harness exists**, so the reviewer's "test expects base" adapts to: the property change is explicit in T2 and the live check eyeballs the whole LATE card).
- Configured-wins: `RowSpacing` is flat DIPs; the `4 × height-ratio` derivation is deleted, not bypassed.

## File Structure

```
src/eq2auras.Core/Config/
├── Settings.cs                  # + GrowDirection enum, Min/MaxRowSpacing, clamp in Normalize
└── PanelSettings.cs             # + ListGrowDirection, CenterGrowDirection, RowSpacing
src/eq2auras.Plugin/Overlay/
├── VisualStyle.cs               # + RowSpacing (resolved); LateTag/LateName -> BaseSize
├── TimerRowVisual.cs            # bottom margin = style.RowSpacing (flat)
├── TimerListWindow.xaml.cs      # grow-direction state machine (shown once; mirror in both windows)
├── CenterZoneWindow.xaml.cs     # same state machine
└── OverlayHost.cs               # grow at creation, AnchorY persistence, ApplyGrowDirections()
src/eq2auras.Plugin/Eq2AurasPlugin.cs  # grow dropdowns, spacing spinner, font label in points, layout
tests/eq2auras.Core.Tests/SettingsTests.cs  # round-trip, defaults, clamps, zero-vs-null
```

---

### Task 1: Core settings — grow directions + row spacing (TDD, Mac)

**Files:**
- Modify: `src/eq2auras.Core/Config/Settings.cs`, `src/eq2auras.Core/Config/PanelSettings.cs`
- Test: `tests/eq2auras.Core.Tests/SettingsTests.cs`

**Interfaces:**
- Produces: `enum GrowDirection { Down = 0, Up = 1 }`; `PanelSettings.ListGrowDirection/CenterGrowDirection : GrowDirection`; `PanelSettings.RowSpacing : double?` (null = 4); `Settings.MinRowSpacing = 0, MaxRowSpacing = 50`.

- [ ] **Step 1: Failing tests** — append to `SettingsTests.cs`:

```csharp
    [Fact]
    public void Roundtrips_grow_directions_and_spacing()
    {
        var settings = new Settings();
        settings.Panels[0].ListGrowDirection = GrowDirection.Up;
        settings.Panels[0].RowSpacing = 0.0;               // zero is MEANINGFUL (touching)
        settings.Panels[1].CenterGrowDirection = GrowDirection.Up;

        var parsed = Settings.Parse(settings.ToJson());

        Assert.Equal(GrowDirection.Up, parsed.Panels[0].ListGrowDirection);
        Assert.Equal(GrowDirection.Down, parsed.Panels[0].CenterGrowDirection);
        Assert.Equal(0.0, parsed.Panels[0].RowSpacing);    // survives as 0, not null
        Assert.Equal(GrowDirection.Up, parsed.Panels[1].CenterGrowDirection);
        Assert.Null(parsed.Panels[1].RowSpacing);          // unset stays null (= default 4)
        Assert.Equal(GrowDirection.Down, parsed.Panels[1].ListGrowDirection);
    }

    [Fact]
    public void Missing_grow_and_spacing_fields_read_as_defaults()
    {
        // DCJS skips initializers: missing enum -> 0 (must mean Down); missing
        // nullable -> null (must mean "default 4", never a legal-looking 0).
        var parsed = Settings.Parse("{\"panels\":[{},{}]}");

        Assert.Equal(GrowDirection.Down, parsed.Panels[0].ListGrowDirection);
        Assert.Equal(GrowDirection.Down, parsed.Panels[0].CenterGrowDirection);
        Assert.Null(parsed.Panels[0].RowSpacing);
    }

    [Fact]
    public void Out_of_range_spacing_clamps_on_parse()
    {
        var parsed = Settings.Parse("{\"panels\":[{\"rowSpacing\":99},{\"rowSpacing\":-3}]}");

        Assert.Equal(50.0, parsed.Panels[0].RowSpacing);
        Assert.Equal(0.0, parsed.Panels[1].RowSpacing);
    }
```

- [ ] **Step 2: Run red** — `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj` → FAIL (`GrowDirection` missing).

- [ ] **Step 3: Implement.** In `Settings.cs`, next to the other knob enums (the DCJS 0-default warning comment covers it):

```csharp
    public enum GrowDirection { Down = 0, Up = 1 }
```

constants (with the other Min/Max pairs):

```csharp
        public const double MinRowSpacing = 0, MaxRowSpacing = 50;
```

and in `Normalize`'s per-panel loop, alongside the dimension clamps:

```csharp
                if (OutOfRange(panel.RowSpacing, MinRowSpacing, MaxRowSpacing))
                    panel.RowSpacing = Math.Min(MaxRowSpacing, Math.Max(MinRowSpacing, panel.RowSpacing.Value));
```

In `PanelSettings.cs`, after `RadialSize`:

```csharp
        [DataMember(Name = "listGrowDirection")]
        public GrowDirection ListGrowDirection { get; set; }     // Down = 0 = default

        [DataMember(Name = "centerGrowDirection")]
        public GrowDirection CenterGrowDirection { get; set; }

        [DataMember(Name = "rowSpacing")]
        public double? RowSpacing { get; set; }       // null = 4; 0 = touching (meaningful)
```

- [ ] **Step 4: Run green** → PASS, all tests. **Step 5: Commit** — `"Core: GrowDirection per window + flat RowSpacing (DCJS 0-default enum, nullable spacing, clamps)"`

---

### Task 2: VisualStyle + rows — spacing knob, LATE respects the font [CI-only compile]

**Files:**
- Modify: `src/eq2auras.Plugin/Overlay/VisualStyle.cs`, `TimerRowVisual.cs`, `OverlayHost.cs` (StyleFor only)

**Interfaces:**
- Produces: `VisualStyle.RowSpacing : double` (resolved, default 4); `LateTag => BaseSize`, `LateName => BaseSize` (**changed behavior**: LateTag drops 22/13→1×, LateName rises 12/13→1×).

- [ ] **Step 1: VisualStyle.** Add the property and change the two LATE roles:

```csharp
        public double RowSpacing { get; set; } = 4.0;   // flat DIPs — never derived (SPEC §Element dimensions)
```

```csharp
        // LATE respects the font as-is (field verdict, SPEC §Typography): both roles at
        // base. Only the radial's seconds keep a boost — the escalation focal glyph.
        public double LateTag => BaseSize;
        public double LateName => BaseSize;
```

(The roles comment — currently "six text roles… 13, 13, 34, 13, 22, 12", an existing off-by-one against the five actual properties — lands correct: "The five text roles (13, 13, 34, 13, 13 — row, pie name, pie seconds, LATE tag, LATE name).")

- [ ] **Step 2: TimerRowVisual** — the row's bottom margin becomes the flat knob (the `4 * hr` derivation is deleted):

```csharp
                Margin = new Thickness(0, 0, 0, style.RowSpacing),
```

- [ ] **Step 3: OverlayHost.StyleFor** gains the resolution line:

```csharp
                RowSpacing = panel.RowSpacing ?? 4.0,
```

- [ ] **Step 4: Core tests green; commit** — `"Plugin: flat RowSpacing in VisualStyle/rows; LATE roles render at base (field verdict)"`

---

### Task 3: Windows + host — the grow-direction state machine [CI-only compile]

**Files:**
- Modify: `src/eq2auras.Plugin/Overlay/TimerListWindow.xaml.cs`, `CenterZoneWindow.xaml.cs`, `OverlayHost.cs`

**Interfaces:**
- Produces (both windows, identical shape): ctor gains `GrowDirection grow` (after `style`); `double AnchorY { get; }`; `void SetGrowDirection(GrowDirection)` (convert-and-persist, never moves); `OverlayHost.ApplyGrowDirections()`.

- [ ] **Step 1: Both windows — state machine.** Shown for `TimerListWindow`; mirror in `CenterZoneWindow` (same fields, same handlers). Add `using Eq2Auras.Core.Config;`. New fields + ctor param:

```csharp
        private GrowDirection _growDirection;
        private bool _dragging;

        public TimerListWindow(string moveLabel, double left, double top, VisualStyle style,
            GrowDirection grow, Action<double, double> persistPosition)
        {
            ...
            _growDirection = grow;
            // Initial Top = the stored ANCHOR for both directions: at creation the
            // content height is ~0, and for Up the SizeChanged compensation walks the
            // top edge upward as content appears, keeping the bottom at the anchor.
            Left = left;
            Top = top;
            ...
            SizeChanged += OnSizeChanged;
```

The anchored edge, the compensation, and the drag suppression:

```csharp
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
```

`OnDragStart` gains the suppression flag and persists the anchor (this is the drag-end call site the review named):

```csharp
        private void OnDragStart(object sender, MouseButtonEventArgs e)
        {
            if (_moveChrome.Visibility != Visibility.Visible) return;
            _dragging = true;
            DragMove();                          // blocks until the button is released
            _dragging = false;
            _persistPosition(Left, AnchorY);     // anchored edge, not raw Top
        }
```

- [ ] **Step 2: OverlayHost.** `CreatePanelWindows` passes the directions (`panel.ListGrowDirection` / `panel.CenterGrowDirection` after the style argument). `SaveAllPositions` — the re-lock call site the review named — persists anchors:

```csharp
                    panel.ListTop = _listWindows[i].AnchorY;
                    ...
                    panel.CenterTop = _centerWindows[i].AnchorY;
```

(`ListLeft`/`CenterLeft` unchanged.) New method, same shape as `RefreshStyles`:

```csharp
        /// Tab knob changed: each window converts-and-persists via SetGrowDirection.
        public void ApplyGrowDirections()
        {
            var dispatcher = _dispatcher;
            if (dispatcher == null) return;
            dispatcher.BeginInvoke((Action)(() =>
            {
                for (int i = 0; i < _settings.Panels.Count && i < _listWindows.Count; i++)
                {
                    _listWindows[i].SetGrowDirection(_settings.Panels[i].ListGrowDirection);
                    _centerWindows[i].SetGrowDirection(_settings.Panels[i].CenterGrowDirection);
                }
            }));
        }
```

- [ ] **Step 3: Core tests green; commit** — `"Plugin: grow-direction state machine — anchor-initial Top, height-delta compensation (drag-suppressed), AnchorY persistence at drag-end + re-lock, flip converts in place"`

---

### Task 4: Tab — grow dropdowns, spacing spinner, points label [CI-only compile]

**Files:**
- Modify: `src/eq2auras.Plugin/Eq2AurasPlugin.cs`

- [ ] **Step 1: Font label speaks points** (storage unchanged — DIPs):

```csharp
        private static string FontLabelText(PanelSettings panel)
            => (panel.FontFamily ?? "default") + " "
               + Math.Round((panel.FontBaseSize ?? 13.0) * 72.0 / 96.0) + " pt";
```

- [ ] **Step 2: New rows in `BuildPanelGroupBox`** (box `Height`: 186 → 250, +64 for the two new rows; after the radial row):

```csharp
            var spacingLabel = new Label { Text = "Spacing:", Left = 8, Top = 186, Width = 44 };
            var spacingBox = DimensionBox(52, 182, panel.RowSpacing ?? 4.0,
                Settings.MinRowSpacing, Settings.MaxRowSpacing,
                v => panel.RowSpacing = v);

            var growLabel = new Label { Text = "Grow:", Left = 8, Top = 218, Width = 40 };
            var listGrowBox = GrowBox(52, 214, panel.ListGrowDirection,
                d => panel.ListGrowDirection = d);
            var centerGrowLabel = new Label { Text = "ctr", Left = 136, Top = 218, Width = 24 };
            var centerGrowBox = GrowBox(162, 214, panel.CenterGrowDirection,
                d => panel.CenterGrowDirection = d);

            box.Controls.Add(spacingLabel);
            box.Controls.Add(spacingBox);
            box.Controls.Add(growLabel);
            box.Controls.Add(listGrowBox);
            box.Controls.Add(centerGrowLabel);
            box.Controls.Add(centerGrowBox);
```

with the helper (mirrors `DimensionBox`; wire after set; the flip machinery runs in `ApplyGrowDirections`):

```csharp
        private ComboBox GrowBox(int left, int top, GrowDirection value, Action<GrowDirection> assign)
        {
            var box = new ComboBox
            {
                Left = left, Top = top, Width = 80,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            box.Items.AddRange(new object[] { "Down", "Up" });
            box.SelectedIndex = (int)value;
            box.SelectedIndexChanged += (s, e) =>
            {
                SettingsStore.Update(_settings, () => assign((GrowDirection)box.SelectedIndex));
                _overlay.ApplyGrowDirections();
            };
            return box;
        }
```

- [ ] **Step 3: Layout shift** for the taller boxes: Panel B `Top = 272` → `336`; palette label/row `Top = 474/468` → `602/596`; moveBox `Top = 546` → `674` (AutoScroll already covers short windows).

- [ ] **Step 4: Core tests green; commit** — `"Plugin: grow dropdowns + spacing spinner per panel; font label speaks points; layout"`

---

### Task 5: Ship + live verification **[WIN]**

- [ ] **Step 1: Push; branch CI green** (`git push -u origin slice7-qol-knobs`).
- [ ] **Step 2: Alex reviews** `git diff main..slice7-qol-knobs`; merge on approval → release.
- [ ] **Step 3: Live script:**
1. **Grow up:** set Panel A's list to Up → window stays put at flip (no jump — the invariant). Fire timers → rows stack **upward**, bottom edge pinned; let them expire → shrinks upward from the top. Center grow Up on Panel B → escalation stack grows up too, independently.
2. **Drag under Up:** unlock, drag the grow-up list while a timer expires mid-drag → drag wins, no fighting; drop it → position sticks, re-lock, plugin off/on → **bottom edge** returns exactly where dropped.
3. **Spacing:** set 0 → rows touch; set 20 → clear gaps; rows never overlap. At non-default row height, spacing stays flat (no scaling).
4. **LATE card:** trigger a linger timer → the LATE tag renders at your font size (no more 1.7× shout); eyeball the whole card — the name line grew slightly (12/13 → 1×) by design.
5. **Font label:** pick 16 pt in the dialog → label reads "… 16 pt".
6. **Regression:** grow-down (default) behavior unchanged; positions/dimensions persist; escalation, palette, greyscale all normal.
- [ ] **Step 4: Backlog** — slice 7 → shipped note.
