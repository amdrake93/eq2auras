# Parse Meter Slice 2 — Increment 5 (Edge-resize) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans (inline, owner-watched). Steps use checkbox (`- [ ]`) syntax.

**Goal:** Make an unlocked meter window resizable by dragging its right edge (width) and bottom edge (visible-row count), persisting the geometry; lock freezes it.

**Architecture:** Add `Width` + `VisibleRows` to `MeterWindowConfig`. Resize is manual (transparent edge grips + mouse-capture — native NC-hittest is unreliable on `AllowsTransparency` windows), **anchored top-left** so the window never moves during a drag (DPI-safe, `GetPosition(this)` stays stable) — the window is repositioned via the existing header drag. Width applies live through a `RowWidth` setter on the shared `BarRowVisual` (guardrail floor, like `FillOpacity`); `VisibleRows` becomes a per-window value driving `RenderSlots` (window height re-fits via `SizeToContent.Height`). Geometry persists once at drag-end. The `MeterWindow` ctor's callbacks are consolidated into a `MeterWindowCallbacks` bundle (deferred from inc-3/4).

**Scope note (recorded):** v1 resizes from the **right and bottom** edges only. Left/top-edge resize (which moves the window origin and needs device→DIP delta conversion) is **deferred** — logged in the backlog. The window is fully repositionable via the header, so this is a scope choice, not a gap.

**Tech Stack:** C# — Core `netstandard2.0` (xUnit, Mac-testable); Plugin `net472`/WPF (CI-compiled). **Runtime resize behavior is verifiable only on the Windows box** (CI compiles; the end-of-slice field-test confirms drag/snap/DPI).

## Global Constraints

_(same as Increments 1–4 — carried verbatim)_

- **Single-assembly packaging**; new files auto-globbed. **No `async` in the plugin**; no non-GAC field types. **Never** reference `System.Web.Extensions`. JSON = DCJS.
- **DCJS skips field initializers** → nullable = "unset, use default".
- **Core `netstandard2.0`, Mac-testable**; **never build the Plugin on the Mac**.
- **Branch `meter-slice2` integration branch** — build on it; **do not merge**. Checkpoint = verify-only CI green.
- **Convergence guardrail floor:** the live `RowWidth` setter on `BarRowVisual` mirrors the `FillOpacity` precedent — the width lives in the shared primitive, the meter needs it live, the timer never calls it.
- **Clone completeness** (inc-2 lesson): both new `MeterWindowConfig` fields copied in `OverlayHost.AddClonedWindow`.

## File Structure

| File | Create/Modify | Responsibility |
|---|---|---|
| `docs/SPEC.md` | Modify | §The meter window locked-axis wording → right/bottom edges (present-tense accuracy). |
| `docs/backlog.md` | Modify | Record left/top-edge resize as deferred. |
| `src/eq2auras.Core/Config/MeterWindowConfig.cs` | Modify | Add `double? Width`, `int? VisibleRows`. |
| `src/eq2auras.Core/Config/MeterSettings.cs` | Modify | Clamp `Width` / `VisibleRows` in `Normalize()`; add `Min/MaxVisibleRows`. |
| `tests/eq2auras.Core.Tests/MeterSettingsTests.cs` | Modify | Width + visible-rows clamp tests. |
| `src/eq2auras.Plugin/Overlay/MeterWindowCallbacks.cs` | **Create** | Bundled per-window callbacks. |
| `src/eq2auras.Plugin/Overlay/BarRowVisual.cs` | Modify | `SetRowWidth(double)` (settable `_rowWidth` + `_root.Width`). |
| `src/eq2auras.Plugin/Overlay/MeterRowVisual.cs` | Modify | `SetRowWidth(double)` → primitive. |
| `src/eq2auras.Plugin/Overlay/MeterWindow.cs` | Modify | Ctor → bundle + `visibleRows`; `_root`/`_visibleRows` fields; edge grips + drag + lock-gating; `SetRowWidth`/`SetVisibleRows`. |
| `src/eq2auras.Plugin/Overlay/OverlayHost.cs` | Modify | `MeterStyle` width; clone copies Width/VisibleRows; build the callbacks bundle. |

---

## Task 1: Spec/backlog wording + Core config (TDD, Mac)

**Files:**
- Modify: `docs/SPEC.md`, `docs/backlog.md`
- Modify: `src/eq2auras.Core/Config/MeterWindowConfig.cs`, `src/eq2auras.Core/Config/MeterSettings.cs`
- Test: `tests/eq2auras.Core.Tests/MeterSettingsTests.cs`

**Interfaces:**
- Produces: `MeterWindowConfig.Width` (`double?`, null = 250), `VisibleRows` (`int?`, null = 10); `MeterSettings.MinVisibleRows` (1) / `MaxVisibleRows` (40); width clamp reuses `Settings.MinRowWidth` (100) / `MaxRowWidth` (800).

- [ ] **Step 1: Spec wording — right/bottom edges**

In `docs/SPEC.md` §The meter window (the **locked** axis bullet), replace:

```
**Unlocked**, a window is **moved by dragging its header** and **resized by dragging its edges** — left/right sets the width, top/bottom sets how many rows are visible (§Configuration).
```

with:

```
**Unlocked**, a window is **moved by dragging its header** and **resized by dragging its right and bottom edges** — the right edge sets the width, the bottom edge sets how many rows are visible (snap to whole rows, §Configuration); repositioning is the header's job, so the top-left stays anchored while resizing.
```

- [ ] **Step 2: Backlog — record the deferred left/top-edge resize**

In `docs/backlog.md`, in the slice-2 in-flight entry's increment list, append to increment 5's line: `Left/top-edge resize deferred (v1 anchors top-left; reposition via the header) — a later refinement (needs window-move + device→DIP delta handling).`

- [ ] **Step 3: Add the clamp/roundtrip tests**

Append inside `MeterSettingsTests` (before the closing brace):

```csharp
    [Theory]
    [InlineData(50, 100)]     // below Settings.MinRowWidth -> clamped up
    [InlineData(2000, 800)]   // above Settings.MaxRowWidth -> clamped down
    [InlineData(300, 300)]    // in range -> unchanged
    public void Window_width_clamps_to_range(double stored, double expected)
    {
        var settings = new Settings();
        settings.Meter.Enabled = true;
        settings.Meter.Windows = new List<MeterWindowConfig> { new MeterWindowConfig { Width = stored } };

        var parsed = Settings.Parse(settings.ToJson());

        Assert.Equal(expected, parsed.Meter.Windows[0].Width);
    }

    [Theory]
    [InlineData(0, 1)]        // below MinVisibleRows -> clamped up
    [InlineData(999, 40)]     // above MaxVisibleRows -> clamped down
    [InlineData(12, 12)]      // in range -> unchanged
    public void Window_visible_rows_clamps_to_range(int stored, int expected)
    {
        var settings = new Settings();
        settings.Meter.Enabled = true;
        settings.Meter.Windows = new List<MeterWindowConfig> { new MeterWindowConfig { VisibleRows = stored } };

        var parsed = Settings.Parse(settings.ToJson());

        Assert.Equal(expected, parsed.Meter.Windows[0].VisibleRows);
    }

    [Fact]
    public void Null_geometry_stays_null_meaning_default()
    {
        var json = "{\"meter\":{\"enabled\":true,\"windows\":[{\"metricKey\":\"encdps\"}]}}";

        var parsed = Settings.Parse(json);

        Assert.Null(parsed.Meter.Windows[0].Width);        // null -> VisualStyle.DefaultRowWidth (250)
        Assert.Null(parsed.Meter.Windows[0].VisibleRows);  // null -> MeterWindow.DefaultVisibleRows (10)
    }
```

- [ ] **Step 4: Run — verify FAIL**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
Expected: build failure — `MeterWindowConfig` has no `Width`/`VisibleRows`.

- [ ] **Step 5: Add the config members**

In `MeterWindowConfig.cs`, add after the `FontBaseSize` member:

```csharp
        [DataMember(Name = "width")]
        public double? Width { get; set; }             // null = VisualStyle.DefaultRowWidth (250); clamped to Settings row-width bounds

        [DataMember(Name = "visibleRows")]
        public int? VisibleRows { get; set; }          // null = MeterWindow.DefaultVisibleRows (10); clamped to [Min,Max]VisibleRows
```

- [ ] **Step 6: Add constants + clamps**

In `MeterSettings.cs`, add after `DefaultOpacity`:

```csharp
        public const int MinVisibleRows = 1;
        public const int MaxVisibleRows = 40;
```

In `Normalize()`, extend the per-window clamp loop — after the `RowHeight` clamp, add:

```csharp
                if (window.Width.HasValue && (window.Width.Value < Settings.MinRowWidth || window.Width.Value > Settings.MaxRowWidth))
                    window.Width = Math.Min(Settings.MaxRowWidth, Math.Max(Settings.MinRowWidth, window.Width.Value));
                if (window.VisibleRows.HasValue && (window.VisibleRows.Value < MinVisibleRows || window.VisibleRows.Value > MaxVisibleRows))
                    window.VisibleRows = Math.Min(MaxVisibleRows, Math.Max(MinVisibleRows, window.VisibleRows.Value));
```

- [ ] **Step 7: Run — verify all PASS**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
Expected: PASS, green (156 + 7 new = 163).

- [ ] **Step 8: Commit**

```bash
git add docs/SPEC.md docs/backlog.md src/eq2auras.Core/Config/MeterWindowConfig.cs src/eq2auras.Core/Config/MeterSettings.cs tests/eq2auras.Core.Tests/MeterSettingsTests.cs
git commit -m "Meter slice2 inc5: Width + VisibleRows config + clamps (Core); spec/backlog scope to right+bottom edge resize"
```

---

## Task 2: Callbacks bundle + VisibleRows plumbing (Plugin, CI-verified)

**Files:**
- Create: `src/eq2auras.Plugin/Overlay/MeterWindowCallbacks.cs`
- Modify: `src/eq2auras.Plugin/Overlay/MeterWindow.cs`

**Interfaces:**
- Produces: `MeterWindowCallbacks` (public fields below); `MeterWindow(double left, double top, VisualStyle style, string metricKey, bool locked, double opacity, int visibleRows, MeterWindowCallbacks callbacks)`; `MeterWindow.DefaultVisibleRows` const.

- [ ] **Step 1: Create the callbacks bundle**

Create `src/eq2auras.Plugin/Overlay/MeterWindowCallbacks.cs`:

```csharp
using System;

namespace Eq2Auras.Plugin.Overlay
{
    /// The per-window callback set for a MeterWindow, bundled so the ctor stays legible as
    /// knobs accrue (SPEC Part III §Configuration). Assembled by OverlayHost per config;
    /// each persists through SettingsStore.Update.
    internal sealed class MeterWindowCallbacks
    {
        public Action<double, double> PersistPosition;
        public Action<string> MetricPicked;
        public Action<bool> LockChanged;
        public Action<double> OpacityChanged;
        public Action<double> RowHeightChanged;
        public Action<string, double> FontChanged;
        public Action<double, int> GeometryChanged;   // width + visible-row count, persisted at resize drag-end
        public Action NewWindow;
        public Action CloseWindow;
        public Func<bool> CanClose;
    }
}
```

- [ ] **Step 2: Replace the per-callback fields with the bundle + VisibleRows field**

In `MeterWindow.cs`, replace this run of fields:

```csharp
        private readonly Action<string> _onMetricPicked;
        private readonly Action<bool> _onLockChanged;
        private readonly Action _onNewWindow;
        private readonly Action _onCloseWindow;
        private readonly Func<bool> _canClose;
        private MenuItem _lockItem;
        private readonly Action<double> _onOpacityChanged;
        private readonly Action<double> _onRowHeightChanged;
        private readonly Action<string, double> _onFontChanged;
        private double _opacity;
```

with:

```csharp
        private readonly MeterWindowCallbacks _cb;
        private MenuItem _lockItem;
        private double _opacity;
```

Also change the `VisibleRows` const to a per-window field. Replace:

```csharp
        public const int VisibleRows = 10;   // view constant: slot count; the frame always carries every ally
```

with:

```csharp
        public const int DefaultVisibleRows = 10;   // null config -> this
        private int _visibleRows;                    // per-window slot count; the frame always carries every ally
```

- [ ] **Step 3: Rewrite the constructor head**

Replace the ctor signature + the callback/field assignments. From:

```csharp
        public MeterWindow(double left, double top, VisualStyle style, string metricKey, bool locked, double opacity,
            Action<double, double> persistPosition, Action<string> onMetricPicked, Action<bool> onLockChanged,
            Action<double> onOpacityChanged, Action<double> onRowHeightChanged, Action<string, double> onFontChanged, Action onNewWindow, Action onCloseWindow, Func<bool> canClose)
            : base(left, top, GrowDirection.Down, persistPosition, clickThroughBaseline: false)
        {
            _style = style;
            _metricKey = MetricRegistry.Resolve(metricKey).Key;   // normalize unknown -> default
            _locked = locked;
            _opacity = opacity;
            _onMetricPicked = onMetricPicked;
            _onLockChanged = onLockChanged;
            _onOpacityChanged = onOpacityChanged;
            _onRowHeightChanged = onRowHeightChanged;
            _onFontChanged = onFontChanged;
            _onNewWindow = onNewWindow;
            _onCloseWindow = onCloseWindow;
            _canClose = canClose;
```

to:

```csharp
        public MeterWindow(double left, double top, VisualStyle style, string metricKey, bool locked, double opacity, int visibleRows,
            MeterWindowCallbacks callbacks)
            : base(left, top, GrowDirection.Down, callbacks.PersistPosition, clickThroughBaseline: false)
        {
            _cb = callbacks;
            _style = style;
            _metricKey = MetricRegistry.Resolve(metricKey).Key;   // normalize unknown -> default
            _locked = locked;
            _opacity = opacity;
            _visibleRows = visibleRows;
```

- [ ] **Step 4: Repoint the callback call sites to the bundle**

In `MeterWindow.cs`, make these five replacements (each is unique):

`_onMetricPicked(key);` → `_cb.MetricPicked(key);`
`_onLockChanged(_locked);` → `_cb.LockChanged(_locked);`
`newItem.Click += (s, e) => _onNewWindow();` → `newItem.Click += (s, e) => _cb.NewWindow();`
`closeItem.Click += (s, e) => _onCloseWindow();` → `closeItem.Click += (s, e) => _cb.CloseWindow();`
`menu.Opened += (s, e) => closeItem.IsEnabled = _canClose();` → `menu.Opened += (s, e) => closeItem.IsEnabled = _cb.CanClose();`
`_onOpacityChanged(opacity);` → `_cb.OpacityChanged(opacity);`
`_onRowHeightChanged(rowHeight);` → `_cb.RowHeightChanged(rowHeight);`
`_onFontChanged(fontFamily, baseSize);` → `_cb.FontChanged(fontFamily, baseSize);`

- [ ] **Step 5: Use `_visibleRows` in `RenderSlots`**

In `RenderSlots`, replace:

```csharp
            _scrollOffset = Math.Max(0, Math.Min(_scrollOffset, total - VisibleRows));   // <= 10 allies -> always 0
            int visible = Math.Min(VisibleRows, total);
```

with:

```csharp
            _scrollOffset = Math.Max(0, Math.Min(_scrollOffset, total - _visibleRows));   // <= _visibleRows allies -> always 0
            int visible = Math.Min(_visibleRows, total);
```

- [ ] **Step 6: Commit**

```bash
git add src/eq2auras.Plugin/Overlay/MeterWindowCallbacks.cs src/eq2auras.Plugin/Overlay/MeterWindow.cs
git commit -m "Meter slice2 inc5: MeterWindowCallbacks bundle + per-window VisibleRows field"
```

---

## Task 3: Live width apply (Plugin, CI-verified)

**Files:**
- Modify: `src/eq2auras.Plugin/Overlay/BarRowVisual.cs`
- Modify: `src/eq2auras.Plugin/Overlay/MeterRowVisual.cs`
- Modify: `src/eq2auras.Plugin/Overlay/MeterWindow.cs`

**Interfaces:**
- Produces: `BarRowVisual.SetRowWidth(double)`; `MeterRowVisual.SetRowWidth(double)`; `MeterWindow.SetRowWidth(double)` + `SetVisibleRows(int)` (private — resize applies live, persistence is at drag-end).

- [ ] **Step 1: Settable width on the shared primitive**

In `BarRowVisual.cs`, change:

```csharp
        private readonly double _rowWidth;
```

to:

```csharp
        private double _rowWidth;
```

Add after the `FillOpacity` property:

```csharp
        // Meter-only, same floor-bracket as FillOpacity: the width lives here, so a
        // consumer that resizes it does so through the primitive. UsableWidth reads
        // _rowWidth, so the next AnimateToFraction lerps to the new width. Timer never calls it.
        public void SetRowWidth(double rowWidth)
        {
            _rowWidth = rowWidth;
            _root.Width = rowWidth;
        }
```

- [ ] **Step 2: Forward width on the meter row**

In `MeterRowVisual.cs`, add after `SetFont`:

```csharp
        public void SetRowWidth(double rowWidth)
        {
            _bar.SetRowWidth(rowWidth);
        }
```

- [ ] **Step 3: Live width + visible-rows on the window**

In `MeterWindow.cs`, add after `SetFont`:

```csharp
        /// Live width (right-edge drag): re-point _style, resize the root + window + every
        /// retained row in place. NOT persisted here — resize persists once at drag-end.
        private void SetRowWidth(double width)
        {
            _style = new VisualStyle
            {
                RowWidth = width,
                RowHeight = _style.RowHeight,
                RadialSize = _style.RadialSize,
                RowSpacing = _style.RowSpacing,
                Font = _style.Font,
                BaseSize = _style.BaseSize,
            };
            _root.Width = width;
            Width = width + WindowSlack;
            foreach (var slot in _slots) slot.SetRowWidth(width);
        }

        /// Live visible-row count (bottom-edge drag): re-render at the new slot count; the
        /// window height re-fits via SizeToContent. NOT persisted here — see drag-end.
        private void SetVisibleRows(int visibleRows)
        {
            _visibleRows = visibleRows;
            if (_lastFrame != null) RenderSlots();
        }
```

- [ ] **Step 4: Commit**

```bash
git add src/eq2auras.Plugin/Overlay/BarRowVisual.cs src/eq2auras.Plugin/Overlay/MeterRowVisual.cs src/eq2auras.Plugin/Overlay/MeterWindow.cs
git commit -m "Meter slice2 inc5: live width apply via BarRowVisual.SetRowWidth + SetVisibleRows"
```

---

## Task 4: Edge grips + drag + host wiring (Plugin, CI-verified)

**Files:**
- Modify: `src/eq2auras.Plugin/Overlay/MeterWindow.cs`
- Modify: `src/eq2auras.Plugin/Overlay/OverlayHost.cs`

**Interfaces:**
- Consumes: `SetRowWidth`/`SetVisibleRows` (Task 3); `MeterWindowConfig.Width`/`VisibleRows`, `MeterSettings.Min/MaxVisibleRows` (Task 1); the `MeterWindowCallbacks` bundle (Task 2).

- [ ] **Step 1: Add the grip fields + drag state**

In `MeterWindow.cs`, add fields after `private StackPanel _rowsPanel;` — first add the `_root` field there too (it becomes an instance field):

```csharp
        private StackPanel _root;
        private System.Windows.Shapes.Rectangle _rightGrip;
        private System.Windows.Shapes.Rectangle _bottomGrip;
        private bool _resizing;
        private Point _resizeStart;
        private double _startWidth;
        private int _startVisibleRows;
```

- [ ] **Step 2: Build the grips and wrap the content**

In `MeterWindow.cs`, replace the content-assembly tail:

```csharp
            _rowsPanel = new StackPanel();
            var root = new StackPanel { Width = style.RowWidth };
            root.Children.Add(header);
            root.Children.Add(_rowsPanel);
            Content = root;
```

with:

```csharp
            _rowsPanel = new StackPanel();
            _root = new StackPanel { Width = style.RowWidth };
            _root.Children.Add(header);
            _root.Children.Add(_rowsPanel);

            // Transparent edge grips (right = width, bottom = visible rows). Top-left is
            // anchored — the window never moves during resize, so GetPosition(this) is a
            // stable DIP reference (SPEC Part III §The meter window). Reposition via header.
            _rightGrip = new System.Windows.Shapes.Rectangle
            {
                Width = 6,
                Fill = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Stretch,
                Cursor = Cursors.SizeWE,
            };
            _bottomGrip = new System.Windows.Shapes.Rectangle
            {
                Height = 6,
                Fill = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Bottom,
                Cursor = Cursors.SizeNS,
            };
            WireResize(_rightGrip, horizontal: true);
            WireResize(_bottomGrip, horizontal: false);

            var contentGrid = new Grid();
            contentGrid.Children.Add(_root);
            contentGrid.Children.Add(_rightGrip);
            contentGrid.Children.Add(_bottomGrip);
            Content = contentGrid;

            UpdateGrips();   // gate on the initial lock state
```

- [ ] **Step 3: Wire the drag + lock-gating + persistence**

In `MeterWindow.cs`, add these methods (after `SetVisibleRows`):

```csharp
        /// Right grip = width; bottom grip = visible-row count (snap to whole rows). Both
        /// anchor the top-left, so the window origin is fixed and GetPosition(this) is a
        /// stable reference. Live during drag; geometry persists once on release.
        private void WireResize(System.Windows.Shapes.Rectangle grip, bool horizontal)
        {
            grip.MouseLeftButtonDown += (s, e) =>
            {
                if (_locked) return;
                _resizing = true;
                _resizeStart = e.GetPosition(this);
                _startWidth = _style.RowWidth;
                _startVisibleRows = _visibleRows;
                grip.CaptureMouse();
                e.Handled = true;
            };
            grip.MouseMove += (s, e) =>
            {
                if (!_resizing) return;
                var p = e.GetPosition(this);
                if (horizontal)
                {
                    SetRowWidth(ClampWidth(_startWidth + (p.X - _resizeStart.X)));
                }
                else
                {
                    int rows = ClampVisibleRows(_startVisibleRows + (int)Math.Round((p.Y - _resizeStart.Y) / _style.RowHeight));
                    if (rows != _visibleRows) SetVisibleRows(rows);
                }
            };
            grip.MouseLeftButtonUp += (s, e) =>
            {
                if (!_resizing) return;
                _resizing = false;
                grip.ReleaseMouseCapture();
                _cb.GeometryChanged(_style.RowWidth, _visibleRows);   // persist both at drag-end
            };
        }

        private static double ClampWidth(double w)
            => Math.Max(Eq2Auras.Core.Config.Settings.MinRowWidth, Math.Min(Eq2Auras.Core.Config.Settings.MaxRowWidth, w));

        private static int ClampVisibleRows(int n)
            => Math.Max(Eq2Auras.Core.Config.MeterSettings.MinVisibleRows, Math.Min(Eq2Auras.Core.Config.MeterSettings.MaxVisibleRows, n));

        /// Lock freezes geometry: grips only take the mouse when unlocked (SPEC Part III —
        /// lock freezes position + size; menu/scroll/settings still work).
        private void UpdateGrips()
        {
            _rightGrip.IsHitTestVisible = !_locked;
            _bottomGrip.IsHitTestVisible = !_locked;
        }
```

And in the lock menu handler, re-gate the grips. Change:

```csharp
            _lockItem.Click += (s, e) =>
            {
                _locked = _lockItem.IsChecked;
                _cb.LockChanged(_locked);
            };
```

to:

```csharp
            _lockItem.Click += (s, e) =>
            {
                _locked = _lockItem.IsChecked;
                UpdateGrips();
                _cb.LockChanged(_locked);
            };
```

- [ ] **Step 4: Host — width in the style, clone the geometry, build the bundle**

In `OverlayHost.cs`, add width to `MeterStyle`. Change the `RowSpacing = 0,` line's block — replace:

```csharp
                RowSpacing = 0,
                RowHeight = config.RowHeight ?? VisualStyle.DefaultRowHeight,
```

with:

```csharp
                RowSpacing = 0,
                RowWidth = config.Width ?? VisualStyle.DefaultRowWidth,
                RowHeight = config.RowHeight ?? VisualStyle.DefaultRowHeight,
```

In `AddClonedWindow`'s clone initializer, add after `FontBaseSize = source.FontBaseSize,`:

```csharp
                Width = source.Width,
                VisibleRows = source.VisibleRows,
```

Replace the whole `new MeterWindow(...)` call in `AddMeterWindow` with the bundle form:

```csharp
            var window = new MeterWindow(
                config.Left ?? DefaultMeterLeft(style),
                config.Top ?? DefaultMeterTop,
                style,
                config.MetricKey,
                config.Locked,
                config.Opacity ?? MeterSettings.DefaultOpacity,
                config.VisibleRows ?? MeterWindow.DefaultVisibleRows,
                new MeterWindowCallbacks
                {
                    PersistPosition = (left, top) => SettingsStore.Update(_settings, () => { config.Left = left; config.Top = top; }),
                    MetricPicked = key => SettingsStore.Update(_settings, () => config.MetricKey = key),
                    LockChanged = locked => SettingsStore.Update(_settings, () => config.Locked = locked),
                    OpacityChanged = opacity => SettingsStore.Update(_settings, () => config.Opacity = opacity),
                    RowHeightChanged = rowHeight => SettingsStore.Update(_settings, () => config.RowHeight = rowHeight),
                    FontChanged = (family, size) => SettingsStore.Update(_settings, () => { config.FontFamily = family; config.FontBaseSize = size; }),
                    GeometryChanged = (width, rows) => SettingsStore.Update(_settings, () => { config.Width = width; config.VisibleRows = rows; }),
                    NewWindow = () => AddClonedWindow(config),
                    CloseWindow = () => CloseMeterWindow(config),
                    CanClose = () => _meterWindows.Count > 1,
                });
```

- [ ] **Step 5: Commit**

```bash
git add src/eq2auras.Plugin/Overlay/MeterWindow.cs src/eq2auras.Plugin/Overlay/OverlayHost.cs
git commit -m "Meter slice2 inc5: right/bottom edge-resize grips (lock-gated) + host geometry wiring"
```

---

## Task 5: CI checkpoint

**Files:** none.

- [ ] **Step 1: Push + watch**

```bash
git push origin meter-slice2
gh run list --branch meter-slice2 --limit 1
gh run watch <run-id> --exit-status
```
Expected: success — Core tests (163) pass, WPF plugin compiles, artifact staged, no publish.

- [ ] **Step 2: On green, slice 2 is code-complete.** Present the branch for the owner's **whole-slice field-test + merge gate** — the merge-gate live script (from SPEC §Testing strategy slice 2) plus a timer-regression sanity check. Do NOT merge; the owner owns that call.

---

## Self-Review

**1. Spec coverage** (SPEC §The meter window — edge-resize; §Settings — width/visible-rows persisted; §Testing strategy slice 2 — resize + clamps):
- Per-window `Width`/`VisibleRows` + clamp → Task 1. ✓
- Right/bottom edge resize (width + rows, snap), lock-gated, top-left anchored → Task 4 (grips + `WireResize` + `UpdateGrips`). ✓ Spec wording updated to match (Task 1 Step 1); left/top deferral recorded (Task 1 Step 2). ✓
- Live width apply → Task 3 (`BarRowVisual.SetRowWidth`, guardrail floor). ✓
- Geometry persists (drag-end) + clone carries it → Task 4 (`GeometryChanged`, clone). ✓
- Ctor legibility → Task 2 (`MeterWindowCallbacks` bundle). ✓

**2. Placeholder scan:** No TBD/TODO; complete code in every step; commands have expected output. ✓

**3. Type consistency:** `MeterWindowConfig.Width`(double?)/`VisibleRows`(int?) defined Task 1, consumed Task 4 (`config.Width`, `source.VisibleRows`); `MeterWindow` bundle ctor `(…, double opacity, int visibleRows, MeterWindowCallbacks)` defined Task 2 Step 3 matches the `new` in Task 4 Step 4; `MeterWindowCallbacks` fields (Task 2 Step 1) match the object-initializer keys (Task 4 Step 4) and the `_cb.X` call sites (Task 2 Step 4); `SetRowWidth`(BarRowVisual/MeterRowVisual/MeterWindow) + `SetVisibleRows` defined Task 3 match the `WireResize` calls Task 4 Step 3; `MeterWindow.DefaultVisibleRows` defined Task 2 Step 2 used in host Task 4 Step 4; `ClampWidth`/`ClampVisibleRows` reference `Settings.Min/MaxRowWidth` + `MeterSettings.Min/MaxVisibleRows` (Task 1). ✓
