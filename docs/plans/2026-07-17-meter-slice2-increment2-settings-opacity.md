# Parse Meter Slice 2 — Increment 2 (Settings window + opacity) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans (this repo's convention — inline, owner-watched) to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Give each meter window a ⚙ cog that opens a per-window dark settings window, whose first knob is a live opacity slider.

**Architecture:** Add a per-window `Opacity` to `MeterWindowConfig` (a 0.3–1.0 multiplier over the baked alphas; `null` = 1.0 = today's look). The ⚙ affordance in the meter header opens a modeless, dark, custom-chrome `MeterSettingsWindow` owned by that meter window; its opacity slider calls back into `MeterWindow.SetOpacity`, which applies the value live (mutating the fill element's opacity + the header/row backplate brushes' opacity — smooth, no rebuild) and persists it. Opacity reaches the fill through a one-line `FillOpacity` setter on the shared `BarRowVisual`.

**Tech Stack:** C# — Core `netstandard2.0` (xUnit, Mac-testable); Plugin `net472`/WPF (CI-compiled).

## Global Constraints

_(same as Increment 1 — carried verbatim)_

- **Single-assembly packaging:** Core sources are globbed into the plugin (`..\eq2auras.Core\**\*.cs`); new Core/plugin files auto-include, no packaging step.
- **No `async` in the plugin;** no non-GAC field types. **Never** reference `System.Web.Extensions`. JSON = DCJS.
- **DCJS skips field initializers on deserialize** → enum/bool defaults are the 0-value; nullable numerics — `null` (never `0`) means "unset, use default".
- **Core is `netstandard2.0`, Mac-testable** via `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`. **Never build the Plugin on the Mac.**
- **Branch `meter-slice2` is the slice integration branch** — build on it; **do not merge to `main`**. Increment checkpoint = verify-only branch CI green (field-test deferred to end-of-slice, owner decision 2026-07-17).
- **Convergence guardrail** (SPEC Part III §The shared rendering substrate): extract only what's concretely needed (ceiling); take the shared component when reachable, burden of proof on *not* sharing (floor). Exposing `BarRowVisual.FillOpacity` is the floor bracket applied — the fill is in the shared primitive and the meter concretely needs to adjust it; the timer ignores the setter.

## File Structure

| File | Create/Modify | Responsibility |
|---|---|---|
| `src/eq2auras.Core/Config/MeterWindowConfig.cs` | Modify | Add `double? Opacity`. |
| `src/eq2auras.Core/Config/MeterSettings.cs` | Modify | Opacity clamp constants + clamp non-null out-of-range in `Normalize()`. |
| `tests/eq2auras.Core.Tests/MeterSettingsTests.cs` | Modify | Opacity clamp + roundtrip tests. |
| `src/eq2auras.Plugin/Overlay/BarRowVisual.cs` | Modify | One-line `FillOpacity` setter (shared-primitive, floor-bracket). |
| `src/eq2auras.Plugin/Overlay/MeterRowVisual.cs` | Modify | Ctor takes opacity; own the backplate brush; `SetOpacity`. |
| `src/eq2auras.Plugin/Overlay/MeterSettingsWindow.cs` | **Create** | Dark modeless custom-chrome settings window; opacity slider + reset. |
| `src/eq2auras.Plugin/Overlay/MeterWindow.cs` | Modify | ⚙ cog opens settings; `_opacity` + header backplate brush; `SetOpacity`; ctor +2 args. |
| `src/eq2auras.Plugin/Overlay/OverlayHost.cs` | Modify | `AddMeterWindow` passes opacity + persist callback; `AddClonedWindow` copies `Opacity`. |

---

## Task 1: Core — per-window opacity + clamp (TDD, Mac)

**Files:**
- Modify: `src/eq2auras.Core/Config/MeterWindowConfig.cs`
- Modify: `src/eq2auras.Core/Config/MeterSettings.cs`
- Test: `tests/eq2auras.Core.Tests/MeterSettingsTests.cs`

**Interfaces:**
- Produces: `MeterWindowConfig.Opacity` (`double?`, null = default); `MeterSettings.MinOpacity`/`MaxOpacity` (`double` consts, 0.3 / 1.0). Task 3's plugin code reads `config.Opacity` and the consts.

- [ ] **Step 1: Add the opacity clamp tests**

Append these two tests inside `MeterSettingsTests` (before the closing brace) in `tests/eq2auras.Core.Tests/MeterSettingsTests.cs`:

```csharp
    [Theory]
    [InlineData(0.1, 0.3)]    // below floor -> clamped up to MinOpacity
    [InlineData(2.0, 1.0)]    // above ceiling -> clamped down to MaxOpacity
    [InlineData(0.6, 0.6)]    // in range -> unchanged
    public void Window_opacity_clamps_to_range(double stored, double expected)
    {
        var settings = new Settings();
        settings.Meter.Enabled = true;
        settings.Meter.Windows = new List<MeterWindowConfig>
        {
            new MeterWindowConfig { Opacity = stored },
        };

        var parsed = Settings.Parse(settings.ToJson());

        Assert.Equal(expected, parsed.Meter.Windows[0].Opacity);
    }

    [Fact]
    public void Null_opacity_stays_null_meaning_default()
    {
        var json = "{\"meter\":{\"enabled\":true,\"windows\":[{\"metricKey\":\"encdps\"}]}}";

        var parsed = Settings.Parse(json);

        Assert.Null(parsed.Meter.Windows[0].Opacity);   // null -> host resolves to 1.0 (today's look)
    }
```

- [ ] **Step 2: Run the tests — verify they FAIL**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
Expected: **build failure** — `MeterWindowConfig` has no `Opacity`.

- [ ] **Step 3: Add `Opacity` to `MeterWindowConfig`**

In `src/eq2auras.Core/Config/MeterWindowConfig.cs`, add after the `Locked` member:

```csharp
        [DataMember(Name = "opacity")]
        public double? Opacity { get; set; }   // 0.3..1.0 multiplier over the baked alphas; null = 1.0 (today's look)
```

- [ ] **Step 4: Add the clamp constants + clamp in `Normalize()`**

In `src/eq2auras.Core/Config/MeterSettings.cs`, add the constants inside the class (after the `Windows` member):

```csharp
        public const double MinOpacity = 0.3;
        public const double MaxOpacity = 1.0;
        public const double DefaultOpacity = 1.0;   // null Opacity resolves here — today's baked look
```

_(The clone path in `OverlayHost.AddClonedWindow` — inc-1 code — must also carry the new `Opacity`; see Task 3 Step 7.)_

Then, in `Normalize()`, replace the final line:

```csharp
            if (Enabled && Windows.Count == 0) Windows.Add(new MeterWindowConfig());
```

with:

```csharp
            if (Enabled && Windows.Count == 0) Windows.Add(new MeterWindowConfig());

            // Clamp only when set (null = "use the default"); a valid value is never
            // rewritten — the overlay thread reads it live. Mirrors Settings.Normalize's
            // per-panel dimension clamps.
            foreach (var window in Windows)
            {
                if (window.Opacity.HasValue && (window.Opacity.Value < MinOpacity || window.Opacity.Value > MaxOpacity))
                    window.Opacity = System.Math.Min(MaxOpacity, System.Math.Max(MinOpacity, window.Opacity.Value));
            }
```

- [ ] **Step 5: Run the tests — verify all PASS**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
Expected: **PASS**, all green (146 + 4 new = 150).

- [ ] **Step 6: Commit**

```bash
git add src/eq2auras.Core/Config/MeterWindowConfig.cs src/eq2auras.Core/Config/MeterSettings.cs tests/eq2auras.Core.Tests/MeterSettingsTests.cs
git commit -m "Meter slice2 inc2: per-window Opacity knob + clamp (Core)"
```

---

## Task 2: Shared primitive + meter row — live fill/backplate opacity (Plugin, CI-verified)

**Files:**
- Modify: `src/eq2auras.Plugin/Overlay/BarRowVisual.cs`
- Modify: `src/eq2auras.Plugin/Overlay/MeterRowVisual.cs`

**Interfaces:**
- Produces: `BarRowVisual.FillOpacity` (`double` set-only-style property); `MeterRowVisual(VisualStyle style, double opacity)` ctor; `MeterRowVisual.SetOpacity(double)`.

- [ ] **Step 1: Expose fill opacity on the shared primitive**

In `src/eq2auras.Plugin/Overlay/BarRowVisual.cs`, add after the `CurrentFillWidth` property (~line 34):

```csharp
        // Meter-only, floor-bracket of the convergence guardrail: the fill lives here, so
        // a consumer that needs to dim it does so through the primitive. Element opacity
        // (not the brush) so it survives SetFillColor's per-poll brush rebuild and never
        // touches the text. The timer never sets it (stays 1.0).
        public double FillOpacity { get => _fill.Opacity; set => _fill.Opacity = value; }
```

- [ ] **Step 2: Thread opacity through the meter row**

In `src/eq2auras.Plugin/Overlay/MeterRowVisual.cs`, add a backplate-brush field after `_percent`:

```csharp
        private readonly SolidColorBrush _backplate;
```

Change the constructor signature from:

```csharp
        public MeterRowVisual(VisualStyle style)
        {
            _bar = new BarRowVisual(style, spark: false, fillAlpha: FillAlpha);
            _bar.RootBorder.Background = new SolidColorBrush(OverlayTheme.MeterBackplate);
            _bar.RootBorder.BorderBrush = new SolidColorBrush(OverlayTheme.CalmBorder);
```

to:

```csharp
        public MeterRowVisual(VisualStyle style, double opacity)
        {
            _bar = new BarRowVisual(style, spark: false, fillAlpha: FillAlpha);
            _backplate = new SolidColorBrush(OverlayTheme.MeterBackplate);
            _bar.RootBorder.Background = _backplate;
            _bar.RootBorder.BorderBrush = new SolidColorBrush(OverlayTheme.CalmBorder);
```

Then, at the end of the constructor (after the `_bar.TrailingPanel.Children.Add(_percent);` line), add:

```csharp
            SetOpacity(opacity);
```

And add the method (after `Update`):

```csharp
        /// One knob scales the fill and the backplate together (SPEC Part III
        /// §Meter display defaults). Element/brush opacity multiplies the baked alphas,
        /// so 1.0 = today's look; text is left at full opacity, always readable.
        public void SetOpacity(double opacity)
        {
            _bar.FillOpacity = opacity;
            _backplate.Opacity = opacity;
        }
```

- [ ] **Step 3: Commit**

```bash
git add src/eq2auras.Plugin/Overlay/BarRowVisual.cs src/eq2auras.Plugin/Overlay/MeterRowVisual.cs
git commit -m "Meter slice2 inc2: live fill/backplate opacity on the meter row (BarRowVisual.FillOpacity)"
```

---

## Task 3: Settings window + cog + host wiring (Plugin, CI-verified)

**Files:**
- Create: `src/eq2auras.Plugin/Overlay/MeterSettingsWindow.cs`
- Modify: `src/eq2auras.Plugin/Overlay/MeterWindow.cs`
- Modify: `src/eq2auras.Plugin/Overlay/OverlayHost.cs`

**Interfaces:**
- Consumes: `MeterRowVisual(style, opacity)` / `.SetOpacity` (Task 2); `MeterSettings.MinOpacity`/`MaxOpacity`, `config.Opacity` (Task 1).
- Produces: `MeterWindow` ctor gains `double opacity, Action<double> onOpacityChanged`; `MeterSettingsWindow(double opacity, Action<double> onOpacityChanged)`.

- [ ] **Step 1: Create the settings window**

Create `src/eq2auras.Plugin/Overlay/MeterSettingsWindow.cs`:

```csharp
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Eq2Auras.Core.Config;

namespace Eq2Auras.Plugin.Overlay
{
    /// Per-window meter settings (SPEC Part III §Configuration) — Details' options window,
    /// dark and custom-chromed, modeless and live-applying. Increment 2 carries one knob
    /// (opacity); row height (inc 3) and font (inc 4) land here next.
    internal sealed class MeterSettingsWindow : Window
    {
        private const double DefaultOpacity = 1.0;

        private readonly Action<double> _onOpacityChanged;
        private readonly Slider _opacity;
        private readonly TextBlock _opacityValue;

        public MeterSettingsWindow(double opacity, Action<double> onOpacityChanged)
        {
            _onOpacityChanged = onOpacityChanged;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;

            var title = new TextBlock
            {
                Text = "Meter window · Settings",
                Foreground = new SolidColorBrush(OverlayTheme.Text),
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0)
            };
            var close = new TextBlock
            {
                Text = "✕",
                Foreground = new SolidColorBrush(Color.FromArgb(255, 0x8B, 0x93, 0xA3)),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            };
            close.MouseLeftButtonDown += (s, e) => Close();

            var titleBar = new DockPanel { Height = 34, Background = Brushes.Transparent };
            DockPanel.SetDock(close, Dock.Right);
            titleBar.Children.Add(close);
            titleBar.Children.Add(title);
            titleBar.MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };

            var opacityLabel = new TextBlock
            {
                Text = "Opacity",
                Foreground = new SolidColorBrush(Color.FromArgb(255, 0xC4, 0xCA, 0xD6)),
                VerticalAlignment = VerticalAlignment.Center,
                Width = 60
            };
            _opacity = new Slider
            {
                Minimum = MeterSettings.MinOpacity,
                Maximum = MeterSettings.MaxOpacity,
                Value = opacity,
                Width = 150,
                VerticalAlignment = VerticalAlignment.Center
            };
            _opacityValue = new TextBlock
            {
                Text = Percent(opacity),
                Foreground = new SolidColorBrush(OverlayTheme.Text),
                VerticalAlignment = VerticalAlignment.Center,
                Width = 42,
                TextAlignment = TextAlignment.Right,
                Margin = new Thickness(8, 0, 0, 0)
            };
            _opacity.ValueChanged += (s, e) =>
            {
                _opacityValue.Text = Percent(_opacity.Value);
                _onOpacityChanged(_opacity.Value);
            };
            var opacityRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            opacityRow.Children.Add(opacityLabel);
            opacityRow.Children.Add(_opacity);
            opacityRow.Children.Add(_opacityValue);

            var reset = new TextBlock
            {
                Text = "Reset to defaults",
                Foreground = new SolidColorBrush(Color.FromArgb(255, 0x8B, 0x93, 0xA3)),
                Cursor = Cursors.Hand
            };
            reset.MouseLeftButtonDown += (s, e) => _opacity.Value = DefaultOpacity;   // fires ValueChanged -> applies + persists

            var body = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };
            body.Children.Add(opacityRow);
            body.Children.Add(reset);

            var stack = new StackPanel();
            stack.Children.Add(titleBar);
            stack.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(OverlayTheme.CalmBorder) });
            stack.Children.Add(body);

            Content = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(252, 20, 23, 29)),
                BorderBrush = new SolidColorBrush(OverlayTheme.CalmBorder),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Child = stack
            };
        }

        private static string Percent(double opacity) => Math.Round(opacity * 100) + "%";
    }
}
```

- [ ] **Step 2: Add the opacity state + settings-open plumbing to `MeterWindow`**

In `MeterWindow.cs`, add fields (after `private MenuItem _lockItem;`):

```csharp
        private readonly Action<double> _onOpacityChanged;
        private double _opacity;
        private SolidColorBrush _headerBackplate;
        private MeterSettingsWindow _settings;
```

- [ ] **Step 3: Extend the constructor for opacity**

Change the constructor signature from:

```csharp
        public MeterWindow(double left, double top, VisualStyle style, string metricKey, bool locked,
            Action<double, double> persistPosition, Action<string> onMetricPicked, Action<bool> onLockChanged,
            Action onNewWindow, Action onCloseWindow, Func<bool> canClose)
            : base(left, top, GrowDirection.Down, persistPosition, clickThroughBaseline: false)
```

to:

```csharp
        public MeterWindow(double left, double top, VisualStyle style, string metricKey, bool locked, double opacity,
            Action<double, double> persistPosition, Action<string> onMetricPicked, Action<bool> onLockChanged,
            Action<double> onOpacityChanged, Action onNewWindow, Action onCloseWindow, Func<bool> canClose)
            : base(left, top, GrowDirection.Down, persistPosition, clickThroughBaseline: false)
```

In the constructor body, after `_locked = locked;`, add:

```csharp
            _opacity = opacity;
```

And after `_onLockChanged = onLockChanged;`, add:

```csharp
            _onOpacityChanged = onOpacityChanged;
```

- [ ] **Step 4: Route the header backplate through a stored brush + make the cog open settings**

In `MeterWindow.cs`, in the header `Border` construction, replace:

```csharp
                Background = new SolidColorBrush(OverlayTheme.MeterBackplate),
```

with (assign the field, then apply opacity just below where `header` is created — see next):

```csharp
                Background = _headerBackplate = new SolidColorBrush(OverlayTheme.MeterBackplate),
```

Replace the affordance block:

```csharp
            var affordance = HeaderBlock(style, dim: true);
            affordance.Text = " ⋯";   // ⋯ — hints the right-click menu (SPEC Part III §Header)
```

with:

```csharp
            var affordance = HeaderBlock(style, dim: true);
            affordance.Text = " ⚙";   // ⚙ — opens the settings window (SPEC Part III §Header)
            affordance.Cursor = System.Windows.Input.Cursors.Hand;
            affordance.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;   // don't let the header drag fire under the cog
                OpenSettings();
            };
```

After the header `Border` is fully constructed (immediately after the `header.ContextMenu = _menu;` line), apply the initial opacity:

```csharp
            _headerBackplate.Opacity = _opacity;
```

- [ ] **Step 5: Add `OpenSettings` + `SetOpacity`, and pass opacity to new rows**

In `MeterWindow.cs`, in `RenderSlots()`, change:

```csharp
                var slot = new MeterRowVisual(_style);
```

to:

```csharp
                var slot = new MeterRowVisual(_style, _opacity);
```

Add these two methods (e.g., after `RenderSlots`):

```csharp
        private void OpenSettings()
        {
            if (_settings != null)
            {
                _settings.Activate();
                return;
            }
            _settings = new MeterSettingsWindow(_opacity, SetOpacity)
            {
                Left = Left + 20,
                Top = Top + 20,
            };
            _settings.Closed += (s, e) => _settings = null;
            _settings.Show();
        }

        /// Live opacity (SPEC Part III §Meter display defaults): applied to the header and
        /// every retained row, and persisted. Text stays at full opacity — always readable.
        public void SetOpacity(double opacity)
        {
            _opacity = opacity;
            _headerBackplate.Opacity = opacity;
            foreach (var slot in _slots) slot.SetOpacity(opacity);
            _onOpacityChanged(opacity);
        }
```

- [ ] **Step 6: Close the settings window when the meter window closes**

The settings window is `Topmost` and independent, so close it with its owner. In `MeterWindow.cs`, override `OnClosed` (add the method):

```csharp
        protected override void OnClosed(EventArgs e)
        {
            _settings?.Close();
            base.OnClosed(e);
        }
```

_(`EventArgs` needs `using System;` — already present in MeterWindow.cs.)_

- [ ] **Step 7: Wire opacity through the host**

In `OverlayHost.cs`, in `AddMeterWindow`, change the `MeterWindow` construction to pass opacity + its persist callback. Replace:

```csharp
            var window = new MeterWindow(
                config.Left ?? DefaultMeterLeft(style),
                config.Top ?? DefaultMeterTop,
                style,
                config.MetricKey,
                config.Locked,
                (left, top) => SettingsStore.Update(_settings, () => { config.Left = left; config.Top = top; }),
                key => SettingsStore.Update(_settings, () => config.MetricKey = key),
                locked => SettingsStore.Update(_settings, () => config.Locked = locked),
                () => AddClonedWindow(config),
                () => CloseMeterWindow(config),
                () => _meterWindows.Count > 1);
```

with:

```csharp
            var window = new MeterWindow(
                config.Left ?? DefaultMeterLeft(style),
                config.Top ?? DefaultMeterTop,
                style,
                config.MetricKey,
                config.Locked,
                config.Opacity ?? 1.0,
                (left, top) => SettingsStore.Update(_settings, () => { config.Left = left; config.Top = top; }),
                key => SettingsStore.Update(_settings, () => config.MetricKey = key),
                locked => SettingsStore.Update(_settings, () => config.Locked = locked),
                opacity => SettingsStore.Update(_settings, () => config.Opacity = opacity),
                () => AddClonedWindow(config),
                () => CloseMeterWindow(config),
                () => _meterWindows.Count > 1);
```

- [ ] **Step 7b: Carry opacity through the clone path**

`AddClonedWindow` (inc-1 code) predates `Opacity`, so it must be extended to copy it — else "New meter window" resets a dimmed window to default, contradicting SPEC:330. In `OverlayHost.cs`, in the `AddClonedWindow` clone initializer, add `Opacity = source.Opacity,`:

```csharp
            var clone = new MeterWindowConfig
            {
                MetricKey = source.MetricKey,
                Locked = source.Locked,
                Opacity = source.Opacity,
                Left = ClampMeterX(baseLeft + MeterCascadeOffset, style),
                Top = ClampMeterY(baseTop + MeterCascadeOffset),
            };
```

- [ ] **Step 8: Commit**

```bash
git add src/eq2auras.Plugin/Overlay/MeterSettingsWindow.cs src/eq2auras.Plugin/Overlay/MeterWindow.cs src/eq2auras.Plugin/Overlay/OverlayHost.cs
git commit -m "Meter slice2 inc2: cog opens per-window dark settings window with a live opacity slider"
```

---

## Task 4: CI checkpoint

**Files:** none.

- [ ] **Step 1: Push**

```bash
git push origin meter-slice2
```

- [ ] **Step 2: Watch verify-only CI to green**

```bash
gh run list --branch meter-slice2 --limit 1
gh run watch <run-id> --exit-status
```
Expected: **success** — Core tests (150) pass, WPF plugin compiles (first compile check of the settings window + opacity threading), artifact staged, no publish. Red compile → fix and re-push.

- [ ] **Step 3: On green, proceed to Increment 3 (row height knob).** Field-test is deferred to end-of-slice (owner). Do NOT merge.

---

## Self-Review

**1. Spec coverage** (SPEC Part III §Configuration — settings window; §Meter display defaults — opacity knob; §Settings — per-window persisted opacity; §Testing strategy slice 2 — opacity clamp):
- Per-window `Opacity` + clamp → Task 1. ✓
- ⚙ cog opens a per-window dark settings window → Task 3 (cog + `MeterSettingsWindow`). ✓
- Single opacity knob scaling fill + backplate together, live-apply → Task 2 (`SetOpacity`/`FillOpacity`) + Task 3 (slider). ✓
- Opacity persists per window → Task 3 Step 7 (persist callback). ✓
- New-window clone carries the source's opacity (SPEC:330 "clones the config") → Task 3 Step 7 (`AddClonedWindow` copies `Opacity`). ✓
- Text stays readable (opacity never touches text) → Task 2 (element/brush opacity on fill + backplate only). ✓
- **Deferred to later increments:** row height (inc 3), font (inc 4), edge-resize (inc 5) — the settings window is scaffolded to grow. ✓

**2. Placeholder scan:** No TBD/TODO; every code step is complete; commands have expected output. ✓

**3. Type consistency:** `MeterWindowConfig.Opacity` (`double?`) defined Task 1, consumed Task 3 Step 7 (`config.Opacity ?? 1.0`); `MeterRowVisual(VisualStyle, double)` + `.SetOpacity(double)` defined Task 2, called Task 3 Steps 5/5; `BarRowVisual.FillOpacity` defined Task 2 Step 1, used Task 2 Step 2 (`_bar.FillOpacity`); `MeterWindow` 13-arg ctor defined Task 3 Step 3 matches the call in Task 3 Step 7 arg-for-arg (opacity after locked; onOpacityChanged after onLockChanged); `MeterSettingsWindow(double, Action<double>)` defined Task 3 Step 1 matches the `new` in Step 5. ✓
