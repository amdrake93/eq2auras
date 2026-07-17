# Parse Meter Slice 2 — Increment 3 (Row height knob) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans (this repo's convention — inline, owner-watched). Steps use checkbox (`- [ ]`) syntax.

**Goal:** Add a per-window row-height knob to the meter's settings window, applied live to the data rows.

**Architecture:** Add `RowHeight` to `MeterWindowConfig` (`null` = 26, today's look). Per SPEC §Configuration the header is a **fixed addend** ("window height = header + visible-row count × row height"), so the knob thickens **data rows only** — the header is decoupled to a fixed `DefaultRowHeight`. Rows resize **in place** (`BarRowVisual.RootBorder.Height`, no recreation, no fade), and the window's `SizeToContent.Height` re-fits automatically. The settings window gains a row-height slider above the opacity slider.

**Tech Stack:** C# — Core `netstandard2.0` (xUnit, Mac-testable); Plugin `net472`/WPF (CI-compiled).

## Global Constraints

_(same as Increments 1–2 — carried verbatim)_

- **Single-assembly packaging**; new files auto-globbed. **No `async` in the plugin**; no non-GAC field types. **Never** reference `System.Web.Extensions`. JSON = DCJS.
- **DCJS skips field initializers on deserialize** → nullable numerics: `null` (never `0`) = "unset, use default".
- **Core is `netstandard2.0`, Mac-testable**; **never build the Plugin on the Mac**.
- **Branch `meter-slice2` is the integration branch** — build on it; **do not merge**. Checkpoint = verify-only CI green; field-test deferred to end-of-slice.
- **Convergence guardrail**: in-place row resize goes through `BarRowVisual.RootBorder` (already public) — no new shared-primitive surface needed.
- **Clone completeness** (lesson from inc-2 review): any new `MeterWindowConfig` field must be copied in `OverlayHost.AddClonedWindow`.

## File Structure

| File | Create/Modify | Responsibility |
|---|---|---|
| `src/eq2auras.Core/Config/MeterWindowConfig.cs` | Modify | Add `double? RowHeight`. |
| `src/eq2auras.Core/Config/MeterSettings.cs` | Modify | Clamp non-null `RowHeight` to `[Settings.MinRowHeight, Settings.MaxRowHeight]` in `Normalize()`. |
| `tests/eq2auras.Core.Tests/MeterSettingsTests.cs` | Modify | Row-height clamp test. |
| `src/eq2auras.Plugin/Overlay/MeterRowVisual.cs` | Modify | `SetRowHeight(double)` → `RootBorder.Height`. |
| `src/eq2auras.Plugin/Overlay/MeterWindow.cs` | Modify | Header decoupled to `DefaultRowHeight`; `_style` mutable; `SetRowHeight`; ctor +`onRowHeightChanged`; pass row height to settings window. |
| `src/eq2auras.Plugin/Overlay/MeterSettingsWindow.cs` | Modify | Row-height slider (above opacity); ctor +2 params. |
| `src/eq2auras.Plugin/Overlay/OverlayHost.cs` | Modify | `MeterStyle(config)` sets RowHeight; `AddClonedWindow` copies RowHeight; pass `onRowHeightChanged`. |

---

## Task 1: Core — per-window row height + clamp (TDD, Mac)

**Files:**
- Modify: `src/eq2auras.Core/Config/MeterWindowConfig.cs`
- Modify: `src/eq2auras.Core/Config/MeterSettings.cs`
- Test: `tests/eq2auras.Core.Tests/MeterSettingsTests.cs`

**Interfaces:**
- Produces: `MeterWindowConfig.RowHeight` (`double?`, null = default). Clamp reuses `Settings.MinRowHeight` (16) / `Settings.MaxRowHeight` (100).

- [ ] **Step 1: Add the row-height clamp test**

Append inside `MeterSettingsTests` (before the closing brace):

```csharp
    [Theory]
    [InlineData(4, 16)]      // below Settings.MinRowHeight -> clamped up
    [InlineData(500, 100)]   // above Settings.MaxRowHeight -> clamped down
    [InlineData(40, 40)]     // in range -> unchanged
    public void Window_row_height_clamps_to_range(double stored, double expected)
    {
        var settings = new Settings();
        settings.Meter.Enabled = true;
        settings.Meter.Windows = new List<MeterWindowConfig>
        {
            new MeterWindowConfig { RowHeight = stored },
        };

        var parsed = Settings.Parse(settings.ToJson());

        Assert.Equal(expected, parsed.Meter.Windows[0].RowHeight);
    }

    [Fact]
    public void Null_row_height_stays_null_meaning_default()
    {
        var json = "{\"meter\":{\"enabled\":true,\"windows\":[{\"metricKey\":\"encdps\"}]}}";

        var parsed = Settings.Parse(json);

        Assert.Null(parsed.Meter.Windows[0].RowHeight);   // null -> host resolves to VisualStyle.DefaultRowHeight (26)
    }
```

- [ ] **Step 2: Run — verify FAIL**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
Expected: build failure — `MeterWindowConfig` has no `RowHeight`.

- [ ] **Step 3: Add `RowHeight` to `MeterWindowConfig`**

In `MeterWindowConfig.cs`, add after the `Opacity` member:

```csharp
        [DataMember(Name = "rowHeight")]
        public double? RowHeight { get; set; }   // null = VisualStyle.DefaultRowHeight (26); clamped to Settings row-height bounds
```

- [ ] **Step 4: Clamp `RowHeight` in `Normalize()`**

In `MeterSettings.cs`, extend the per-window clamp loop. Replace:

```csharp
            foreach (var window in Windows)
            {
                if (window.Opacity.HasValue && (window.Opacity.Value < MinOpacity || window.Opacity.Value > MaxOpacity))
                    window.Opacity = Math.Min(MaxOpacity, Math.Max(MinOpacity, window.Opacity.Value));
            }
```

with:

```csharp
            foreach (var window in Windows)
            {
                if (window.Opacity.HasValue && (window.Opacity.Value < MinOpacity || window.Opacity.Value > MaxOpacity))
                    window.Opacity = Math.Min(MaxOpacity, Math.Max(MinOpacity, window.Opacity.Value));
                if (window.RowHeight.HasValue && (window.RowHeight.Value < Settings.MinRowHeight || window.RowHeight.Value > Settings.MaxRowHeight))
                    window.RowHeight = Math.Min(Settings.MaxRowHeight, Math.Max(Settings.MinRowHeight, window.RowHeight.Value));
            }
```

- [ ] **Step 5: Run — verify all PASS**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
Expected: PASS, green (150 + 4 new = 154).

- [ ] **Step 6: Commit**

```bash
git add src/eq2auras.Core/Config/MeterWindowConfig.cs src/eq2auras.Core/Config/MeterSettings.cs tests/eq2auras.Core.Tests/MeterSettingsTests.cs
git commit -m "Meter slice2 inc3: per-window RowHeight knob + clamp (Core)"
```

---

## Task 2: Live row resize + header decouple (Plugin, CI-verified)

**Files:**
- Modify: `src/eq2auras.Plugin/Overlay/MeterRowVisual.cs`
- Modify: `src/eq2auras.Plugin/Overlay/MeterWindow.cs`

**Interfaces:**
- Produces: `MeterRowVisual.SetRowHeight(double)`; `MeterWindow.SetRowHeight(double)`; `MeterWindow` ctor gains `Action<double> onRowHeightChanged`.

- [ ] **Step 1: In-place row resize**

In `MeterRowVisual.cs`, add after `SetOpacity`:

```csharp
        /// Live row-height (SPEC Part III §Configuration): resize the retained row in place
        /// via the shared primitive's border — no recreation, no fade, animations intact.
        public void SetRowHeight(double rowHeight)
        {
            _bar.RootBorder.Height = rowHeight;
        }
```

- [ ] **Step 2: Decouple the header from the row-height knob**

In `MeterWindow.cs`, the header must stay a fixed addend (SPEC §Configuration). Replace:

```csharp
            double hr = style.HeightRatio;
```

with:

```csharp
            double hr = 1.0;   // header stays default-proportioned; the row-height knob thickens data rows only (SPEC Part III §Configuration)
```

And in the header `Border` construction, replace:

```csharp
                Height = style.RowHeight,
```

with:

```csharp
                Height = VisualStyle.DefaultRowHeight,
```

- [ ] **Step 3: Make `_style` mutable + add the callback field**

In `MeterWindow.cs`, change:

```csharp
        private readonly VisualStyle _style;
```

to:

```csharp
        private VisualStyle _style;
```

Add after `_onOpacityChanged`:

```csharp
        private readonly Action<double> _onRowHeightChanged;
```

- [ ] **Step 4: Extend the constructor**

Change the signature from:

```csharp
        public MeterWindow(double left, double top, VisualStyle style, string metricKey, bool locked, double opacity,
            Action<double, double> persistPosition, Action<string> onMetricPicked, Action<bool> onLockChanged,
            Action<double> onOpacityChanged, Action onNewWindow, Action onCloseWindow, Func<bool> canClose)
```

to:

```csharp
        public MeterWindow(double left, double top, VisualStyle style, string metricKey, bool locked, double opacity,
            Action<double, double> persistPosition, Action<string> onMetricPicked, Action<bool> onLockChanged,
            Action<double> onOpacityChanged, Action<double> onRowHeightChanged, Action onNewWindow, Action onCloseWindow, Func<bool> canClose)
```

And after `_onOpacityChanged = onOpacityChanged;` add:

```csharp
            _onRowHeightChanged = onRowHeightChanged;
```

- [ ] **Step 5: Add `SetRowHeight`**

In `MeterWindow.cs`, add after `SetOpacity`:

```csharp
        /// Live row-height: resize every retained row in place and re-point _style so
        /// future slots build at the new height; the window's SizeToContent re-fits. Persisted.
        public void SetRowHeight(double rowHeight)
        {
            _style = new VisualStyle
            {
                RowWidth = _style.RowWidth,
                RowHeight = rowHeight,
                RadialSize = _style.RadialSize,
                RowSpacing = _style.RowSpacing,
                Font = _style.Font,
                BaseSize = _style.BaseSize,
            };
            foreach (var slot in _slots) slot.SetRowHeight(rowHeight);
            _onRowHeightChanged(rowHeight);
        }
```

- [ ] **Step 6: Commit**

```bash
git add src/eq2auras.Plugin/Overlay/MeterRowVisual.cs src/eq2auras.Plugin/Overlay/MeterWindow.cs
git commit -m "Meter slice2 inc3: live in-place row resize + fixed-height header (row-height knob applies to data rows only)"
```

---

## Task 3: Row-height slider + settings/host wiring (Plugin, CI-verified)

**Files:**
- Modify: `src/eq2auras.Plugin/Overlay/MeterSettingsWindow.cs`
- Modify: `src/eq2auras.Plugin/Overlay/MeterWindow.cs`
- Modify: `src/eq2auras.Plugin/Overlay/OverlayHost.cs`

**Interfaces:**
- Consumes: `MeterWindow.SetRowHeight` / `MeterRowVisual.SetRowHeight` (Task 2); `config.RowHeight` (Task 1); `Settings.MinRowHeight`/`MaxRowHeight`.
- Produces: `MeterSettingsWindow(double rowHeight, Action<double> onRowHeightChanged, double opacity, Action<double> onOpacityChanged)`.

- [ ] **Step 1: Add the row-height slider to the settings window**

In `MeterSettingsWindow.cs`, change the ctor signature from:

```csharp
        public MeterSettingsWindow(double opacity, Action<double> onOpacityChanged)
        {
            _onOpacityChanged = onOpacityChanged;
```

to:

```csharp
        public MeterSettingsWindow(double rowHeight, Action<double> onRowHeightChanged, double opacity, Action<double> onOpacityChanged)
        {
            _onOpacityChanged = onOpacityChanged;
            _onRowHeightChanged = onRowHeightChanged;
```

Add the field (after `_onOpacityChanged`):

```csharp
        private readonly Action<double> _onRowHeightChanged;
        private readonly Slider _rowHeight;
        private readonly TextBlock _rowHeightValue;
```

Build the row-height row — insert this block just before the `opacityRow` construction:

```csharp
            var rowHeightLabel = new TextBlock
            {
                Text = "Row height",
                Foreground = new SolidColorBrush(Color.FromArgb(255, 0xC4, 0xCA, 0xD6)),
                VerticalAlignment = VerticalAlignment.Center,
                Width = 60
            };
            _rowHeight = new Slider
            {
                Minimum = Settings.MinRowHeight,
                Maximum = Settings.MaxRowHeight,
                Value = rowHeight,
                Width = 150,
                IsSnapToTickEnabled = true,
                TickFrequency = 1,
                VerticalAlignment = VerticalAlignment.Center
            };
            _rowHeightValue = new TextBlock
            {
                Text = Px(rowHeight),
                Foreground = new SolidColorBrush(OverlayTheme.Text),
                VerticalAlignment = VerticalAlignment.Center,
                Width = 42,
                TextAlignment = TextAlignment.Right,
                Margin = new Thickness(8, 0, 0, 0)
            };
            _rowHeight.ValueChanged += (s, e) =>
            {
                _rowHeightValue.Text = Px(_rowHeight.Value);
                _onRowHeightChanged(_rowHeight.Value);
            };
            var rowHeightRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            rowHeightRow.Children.Add(rowHeightLabel);
            rowHeightRow.Children.Add(_rowHeight);
            rowHeightRow.Children.Add(_rowHeightValue);
```

Add `rowHeightRow` above `opacityRow` in the body. Replace:

```csharp
            var body = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };
            body.Children.Add(opacityRow);
            body.Children.Add(reset);
```

with:

```csharp
            var body = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };
            body.Children.Add(rowHeightRow);
            body.Children.Add(opacityRow);
            body.Children.Add(reset);
```

Add the formatter next to `Percent`:

```csharp
        private static string Px(double rowHeight) => Math.Round(rowHeight) + " px";
```

_(Note: "Reset to defaults" resets opacity only in this increment; a full reset lands when the settings window is finalized. Leaving the row-height slider untouched by reset is acceptable and non-blocking for the slice.)_

- [ ] **Step 2: Pass row height when opening settings**

In `MeterWindow.cs`, in `OpenSettings`, change:

```csharp
            _settings = new MeterSettingsWindow(_opacity, SetOpacity)
```

to:

```csharp
            _settings = new MeterSettingsWindow(_style.RowHeight, SetRowHeight, _opacity, SetOpacity)
```

- [ ] **Step 3: Host — style from config, clone, and callback wiring**

In `OverlayHost.cs`, change `MeterStyle` to take the config. Replace:

```csharp
        private static VisualStyle MeterStyle() => new VisualStyle { RowSpacing = 0 };
```

with:

```csharp
        private static VisualStyle MeterStyle(MeterWindowConfig config)
            => new VisualStyle { RowSpacing = 0, RowHeight = config.RowHeight ?? VisualStyle.DefaultRowHeight };
```

Update its two callers. In `AddMeterWindow`, `var style = MeterStyle();` → `var style = MeterStyle(config);`. In `AddClonedWindow`, `var style = MeterStyle();` → `var style = MeterStyle(source);`.

In `AddClonedWindow`'s clone initializer, add `RowHeight = source.RowHeight,` (after `Opacity = source.Opacity,`):

```csharp
                Opacity = source.Opacity,
                RowHeight = source.RowHeight,
```

In `AddMeterWindow`, add the row-height persist callback to the `MeterWindow` construction — after the `opacity => ...` line:

```csharp
                opacity => SettingsStore.Update(_settings, () => config.Opacity = opacity),
                rowHeight => SettingsStore.Update(_settings, () => config.RowHeight = rowHeight),
```

- [ ] **Step 4: Commit**

```bash
git add src/eq2auras.Plugin/Overlay/MeterSettingsWindow.cs src/eq2auras.Plugin/Overlay/MeterWindow.cs src/eq2auras.Plugin/Overlay/OverlayHost.cs
git commit -m "Meter slice2 inc3: row-height slider in the settings window + host wiring (style-from-config, clone carries RowHeight)"
```

---

## Task 4: CI checkpoint

**Files:** none.

- [ ] **Step 1: Push + watch**

```bash
git push origin meter-slice2
gh run list --branch meter-slice2 --limit 1
gh run watch <run-id> --exit-status
```
Expected: success — Core tests (154) pass, WPF plugin compiles, artifact staged, no publish.

- [ ] **Step 2: On green, proceed to Increment 4 (font knob).** Field-test deferred to end-of-slice. Do NOT merge.

---

## Self-Review

**1. Spec coverage** (SPEC Part III §Configuration — row-height knob thickens rows, header a fixed addend; §Settings — per-window persisted row height; §Testing strategy slice 2 — row-height clamp):
- Per-window `RowHeight` + clamp → Task 1. ✓
- Header fixed, row-height affects data rows only → Task 2 Step 2 (header `DefaultRowHeight` + `hr = 1.0`). ✓
- Live in-place row resize, window re-fits → Task 2 (`SetRowHeight` + `SizeToContent.Height`). ✓
- Slider in settings window (above opacity) → Task 3 Step 1. ✓
- Row height persists per window → Task 3 Step 3 (persist callback). ✓
- New-window clone carries RowHeight (inc-2 clone lesson) → Task 3 Step 3. ✓

**2. Placeholder scan:** No TBD/TODO; every code step complete; commands have expected output. The "Reset resets opacity only" note is a stated, bounded decision, not a placeholder. ✓

**3. Type consistency:** `MeterWindowConfig.RowHeight` (`double?`) defined Task 1, consumed Task 3 (`config.RowHeight`, `source.RowHeight`); `MeterRowVisual.SetRowHeight` / `MeterWindow.SetRowHeight` defined Task 2, called Task 2/Task 3 Step 2; `MeterSettingsWindow(rowHeight, onRowHeightChanged, opacity, onOpacityChanged)` defined Task 3 Step 1 matches the `new` in Task 3 Step 2; `MeterWindow` 14-arg ctor defined Task 2 Step 4 matches the host call updated in Task 3 Step 3 (onRowHeightChanged after onOpacityChanged). `MeterStyle(MeterWindowConfig)` defined Task 3 Step 3 matches both callers. ✓

_(Ctor is now 14 positional args — a `MeterWindowCallbacks` bundle is a candidate cleanup if inc-4's font callback pushes it further; deferred, not done here.)_
