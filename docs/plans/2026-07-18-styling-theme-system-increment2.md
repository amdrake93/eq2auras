# Styling / Theme System — Increment 2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the two control-kit primitives the settings window needs — a chromed `ThemeButton` and a dark `ThemeSlider` with an editable type-in value — and rewrite `MeterSettingsWindow` to assemble from `Theme` + those primitives, replacing its hand-rolled text-links and raw light WPF sliders.

**Architecture:** Two small code-behind primitives under `src/eq2auras.Plugin/Overlay/`, both consuming the `Theme` tokens from increment 1. `ThemeButton` is a `Border`+`TextBlock` with real hover states and a `Click` event (the codebase's "build it ourselves" idiom, now with chrome). `ThemeSlider` wraps the **native** `Slider` (which owns drag/clamp/snap) under a dark `ControlTemplate` built once via `XamlReader.Parse`, paired with a bordered `TextBox` synced two-way to the slider value.

**Tech Stack:** C# / net472 / WPF (Plugin — **not** Mac-testable; verified by CI compile + code review + the on-box field script), `System.Windows.Markup.XamlReader` (runtime XAML, not the compile-time markup compiler).

## Global Constraints

- **Single-assembly packaging.** No second DLL; no non-GAC field types; no careless `async`.
- **Never reference `System.Web.Extensions`** (breaks the compile-time WPF XAML *markup compiler*). `XamlReader.Parse` is **runtime** XAML from `PresentationFramework` (GAC) and is unaffected — it is not the markup compiler.
- **Never `Assembly.LoadFrom`.**
- **Plugin is transcribe-only on the Mac.** Correctness gates: branch CI (Core tests + WPF compile + artifact) → code review → on-box field script. No local WPF runtime.
- **Semantic tokens, not literals** — consume `Theme.*` (increment 1); a genuinely-new chrome value gets a named token, not an inline literal.
- **Meaning is carried by real things** — buttons carry a border+fill+hover, not muted text; inputs are bordered boxes.

**Branch:** continues on `styling-theme-system`. Present at the owner's merge gate; never merge without the owner's call.

## Scope boundaries (deliberate, from the run's decomposition)

- The **secondary `ComboBox` stays** this increment — it relocates to the right-click popup in increment 4; removing it now would leave secondary unsettable for two increments. Increment 2 themes everything *around* it and leaves the secondary row's control untouched.
- **No backdrop-opacity slider** here — it is added in increment 3 alongside its rendering (a knob with no rendering is dead).
- **No `ThemeCheckbox`** — its only consumer is the timer settings window (a separate future effort), so it is built with that effort, not as dead code here (recorded in backlog).
- **No window-chrome primitive extracted** — the settings window is the only spawned `Window` in these increments; it is themed inline. Extraction waits for a second consumer.
- The spec's `LinkNormal`/`LinkHover` tokens are **subsumed by the chromed button** (Alex's mockup feedback turned bare text-links into real buttons): `ThemeButton` expresses rest/hover with `TextLabel`→`TextPrimary` + a surface fill + `Divider`→`TextMuted` border, so no standalone `Link*` color tokens are added. `ItemSelected` remains deferred to the list-item (increment 4).

---

## File structure

| File | Responsibility | Change |
|---|---|---|
| `src/eq2auras.Plugin/Overlay/ThemeButton.cs` | chromed hover button | **create** |
| `src/eq2auras.Plugin/Overlay/ThemeSlider.cs` | dark slider + type-in value | **create** |
| `src/eq2auras.Plugin/Overlay/MeterSettingsWindow.cs` | the settings window | **rewrite** (chrome + controls → `Theme`/kit; secondary `ComboBox` kept) |

---

### Task 1: `ThemeButton` primitive (transcribe)

**Files:**
- Create: `src/eq2auras.Plugin/Overlay/ThemeButton.cs`

**Interfaces:**
- Produces: `class ThemeButton : Border`; ctor `ThemeButton(string text)`; `event Action Click`; `string Text { set; }`.
- Consumes: `Theme.TextLabel`, `Theme.TextPrimary`, `Theme.TextMuted`, `Theme.Divider`, `Theme.Surface(byte)` (increment 1).

**Verification:** WPF — CI compile + code review + field. No unit test.

- [ ] **Step 1: Create `ThemeButton.cs`**

```csharp
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Eq2Auras.Plugin.Overlay
{
    /// A real, chromed button (SPEC §The theme system: "a button with real chrome —
    /// border + fill + hover — never muted text pretending to be clickable"). Border +
    /// TextBlock with rest/hover states; raises Click on left-button-up. Replaces the
    /// settings window's hand-rolled TextBlock+handler "links".
    internal sealed class ThemeButton : Border
    {
        private static readonly Brush RestFill  = Theme.Surface(0x0D);   // subtle
        private static readonly Brush HoverFill = Theme.Surface(0x1A);

        private readonly TextBlock _label;

        public event Action Click;

        public string Text { set => _label.Text = value; }

        public ThemeButton(string text)
        {
            _label = new TextBlock
            {
                Text = text,
                Foreground = Theme.TextLabel,
                VerticalAlignment = VerticalAlignment.Center
            };

            Child = _label;
            Background = RestFill;
            BorderBrush = Theme.Divider;
            BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(4);
            Padding = new Thickness(11, 4, 11, 4);
            Cursor = Cursors.Hand;
            HorizontalAlignment = HorizontalAlignment.Left;

            MouseEnter += (s, e) => { Background = HoverFill; BorderBrush = Theme.TextMuted; _label.Foreground = Theme.TextPrimary; };
            MouseLeave += (s, e) => { Background = RestFill;  BorderBrush = Theme.Divider;   _label.Foreground = Theme.TextLabel; };
            MouseLeftButtonUp += (s, e) => Click?.Invoke();
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add src/eq2auras.Plugin/Overlay/ThemeButton.cs
git commit -m "Theme kit: ThemeButton — chromed hover button (increment 2)"
```

---

### Task 2: `ThemeSlider` primitive (transcribe)

**Files:**
- Create: `src/eq2auras.Plugin/Overlay/ThemeSlider.cs`

**Interfaces:**
- Produces: `class ThemeSlider : Grid`; ctor `ThemeSlider(double min, double max, double step, double value, Func<double,string> format, Func<string,double?> parse)`; `double Value { get; set; }`; `event Action<double> ValueChanged`.
- Consumes: `Theme.TextPrimary`, `Theme.Divider`, `Theme.Surface(byte)` (increment 1).

**Design:** the native `Slider` owns value/drag/clamp/snap (proven, not re-implemented). A dark `ControlTemplate`, built once via `XamlReader.Parse`, restyles it (dark track + grey round thumb; track-click is inert without repeat-buttons — precise setting is the type-in box's job, coarse setting is the drag). The bordered `TextBox` shows the formatted value and, on Enter/blur, parses and writes it back through the slider (which clamps/snaps), then reflects the resolved value. A `_syncing` flag breaks the slider↔box feedback loop.

**Verification:** WPF — CI compile + code review + field. No unit test.

- [ ] **Step 1: Create `ThemeSlider.cs`**

```csharp
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;

namespace Eq2Auras.Plugin.Overlay
{
    /// A dark slider with an editable type-in value (SPEC §The theme system: "a slider
    /// with an editable type-in value — drag for coarse, keyboard for precise; a bordered
    /// input box, not a bare number"). Wraps the native Slider (owns drag/clamp/snap) under
    /// a dark ControlTemplate, paired with a bordered TextBox synced two-way to the value.
    internal sealed class ThemeSlider : Grid
    {
        private static readonly ControlTemplate DarkTemplate = BuildTemplate();

        private readonly Slider _slider;
        private readonly TextBox _box;
        private readonly Func<double, string> _format;
        private readonly Func<string, double?> _parse;
        private bool _syncing;

        public event Action<double> ValueChanged;

        public double Value
        {
            get { return _slider.Value; }
            set { _slider.Value = value; }
        }

        public ThemeSlider(double min, double max, double step, double value,
            Func<double, string> format, Func<string, double?> parse)
        {
            _format = format;
            _parse = parse;

            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _slider = new Slider
            {
                Minimum = min,
                Maximum = max,
                Value = value,
                SmallChange = step,
                LargeChange = step,
                IsSnapToTickEnabled = true,
                TickFrequency = step,
                Template = DarkTemplate,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            SetColumn(_slider, 0);
            Children.Add(_slider);

            _box = new TextBox
            {
                Width = 58,
                Text = format(value),
                Foreground = Theme.TextPrimary,
                Background = Theme.Surface(0xFF),
                CaretBrush = Theme.TextPrimary,
                BorderBrush = Theme.Divider,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 2, 6, 2),
                TextAlignment = TextAlignment.Right,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            SetColumn(_box, 1);
            Children.Add(_box);

            _slider.ValueChanged += (s, e) =>
            {
                if (_syncing) return;
                _syncing = true;
                _box.Text = _format(_slider.Value);
                _syncing = false;
                ValueChanged?.Invoke(_slider.Value);
            };

            _box.LostFocus += (s, e) => CommitBox();
            _box.KeyDown += (s, e) => { if (e.Key == Key.Enter) CommitBox(); };
        }

        private void CommitBox()
        {
            if (_syncing) return;
            double? parsed = _parse(_box.Text);
            _syncing = true;
            if (parsed.HasValue) _slider.Value = parsed.Value;   // native Slider clamps + snaps
            _box.Text = _format(_slider.Value);                  // reflect the resolved value
            _syncing = false;
            if (parsed.HasValue) ValueChanged?.Invoke(_slider.Value);
        }

        private static ControlTemplate BuildTemplate()
        {
            const string xaml =
                "<ControlTemplate TargetType=\"{x:Type Slider}\" " +
                "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                "<Grid VerticalAlignment=\"Center\" Height=\"18\" Background=\"Transparent\">" +
                "<Border Height=\"4\" VerticalAlignment=\"Center\" Background=\"#FF2A303B\" CornerRadius=\"2\"/>" +
                "<Track x:Name=\"PART_Track\">" +
                "<Track.Thumb>" +
                "<Thumb>" +
                "<Thumb.Template>" +
                "<ControlTemplate TargetType=\"{x:Type Thumb}\">" +
                "<Border Width=\"12\" Height=\"12\" CornerRadius=\"6\" Background=\"#FFC4CAD6\"/>" +
                "</ControlTemplate>" +
                "</Thumb.Template>" +
                "</Thumb>" +
                "</Track.Thumb>" +
                "</Track>" +
                "</Grid>" +
                "</ControlTemplate>";
            return (ControlTemplate)XamlReader.Parse(xaml);
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add src/eq2auras.Plugin/Overlay/ThemeSlider.cs
git commit -m "Theme kit: ThemeSlider — native slider under a dark template + editable type-in value (increment 2)"
```

---

### Task 3: Rewrite `MeterSettingsWindow` on `Theme` + the kit (transcribe)

Replace the two raw light `Slider`s (row height, opacity) with `ThemeSlider`, the three hand-rolled `TextBlock` "links" (✕, Choose…, Reset) with `ThemeButton`, and every hardcoded chrome literal with a `Theme` token. **Leave the `Secondary` `ComboBox` and its row exactly as-is** (removed in increment 4). Do not add a backdrop-opacity slider (increment 3). The window's structure, drag, sizing, and the callback signatures the ctor takes are unchanged.

**Files:**
- Rewrite: `src/eq2auras.Plugin/Overlay/MeterSettingsWindow.cs`

**Interfaces:**
- Consumes: `ThemeButton` (Task 1), `ThemeSlider` (Task 2), `Theme.*` (increment 1). The public ctor signature is unchanged, so its **sole construction site — `MeterWindow.cs:326`** (`new MeterSettingsWindow(_style.RowHeight, SetRowHeight, _opacity, SetOpacity, _style.Font?.Source, _style.BaseSize, SetFont, _secondaryKey, SetSecondary)`) — compiles unmodified.
- Produces: nothing new.

**Verification:** WPF — CI compile + code review + field. No unit test.

- [ ] **Step 1: Replace the row-height row (currently `MeterSettingsWindow.cs:69-103`) with a `ThemeSlider`**

Replace the `rowHeightLabel` + `_rowHeight` (`Slider`) + `_rowHeightValue` + `rowHeightRow` block with:

```csharp
            var rowHeightLabel = new TextBlock
            {
                Text = "Row height",
                Foreground = Theme.TextLabel,
                VerticalAlignment = VerticalAlignment.Center,
                Width = 112
            };
            var rowHeightSlider = new ThemeSlider(
                Settings.MinRowHeight, Settings.MaxRowHeight, 1, rowHeight,
                v => Math.Round(v) + " px",
                t => TryParseNumber(t.Replace("px", ""), out double px) ? (double?)px : null);
            rowHeightSlider.ValueChanged += v => _onRowHeightChanged(v);
            var rowHeightRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
            rowHeightRow.Children.Add(rowHeightLabel);
            rowHeightRow.Children.Add(rowHeightSlider);
```

(The label column is 112 wide and the row margin 16, matching the mockup's wrap-safe rhythm; `ThemeSlider` fills the rest via its star column. Remove the `_rowHeight`/`_rowHeightValue` fields — no longer used.)

- [ ] **Step 2: Replace the opacity row (currently `:167-206`) with a `ThemeSlider`**

```csharp
            var opacityLabel = new TextBlock
            {
                Text = "Window opacity",
                Foreground = Theme.TextLabel,
                VerticalAlignment = VerticalAlignment.Center,
                Width = 112
            };
            var opacitySlider = new ThemeSlider(
                MeterSettings.MinOpacity, MeterSettings.MaxOpacity, 0.01, opacity,
                v => Math.Round(v * 100) + "%",
                t => TryParseNumber(t.Replace("%", ""), out double pct) ? (double?)(pct / 100.0) : null);
            opacitySlider.ValueChanged += v => _onOpacityChanged(v);
            var opacityRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
            opacityRow.Children.Add(opacityLabel);
            opacityRow.Children.Add(opacitySlider);
```

(Label "Window opacity" per SPEC §Meter display defaults — it becomes one of two axes in increment 3. Remove `_opacity`/`_opacityValue` fields.)

- [ ] **Step 3: Replace the three text-link buttons (✕ `:53-61`, Choose… `:118-124`, Reset `:208-213`) with `ThemeButton`**

Close button:
```csharp
            var close = new ThemeButton("✕");
            close.Click += Close;
```
Choose button (in the font row, keep `fontValue` label as-is but token its color):
```csharp
            var choose = new ThemeButton("Choose…") { Margin = new Thickness(10, 0, 0, 0) };   // gap from fontValue (the old "  " indent is gone)
            choose.Click += () =>
            {
                using (var dialog = new System.Windows.Forms.FontDialog())
                {
                    var currentFamily = _fontFamily ?? System.Drawing.SystemFonts.MessageBoxFont.Name;
                    dialog.Font = new System.Drawing.Font(currentFamily, (float)(_fontBaseSize * 72.0 / 96.0));
                    if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                    _fontFamily = dialog.Font.Name;
                    _fontBaseSize = dialog.Font.SizeInPoints * 96.0 / 72.0;
                    fontValue.Text = FontLabel(_fontFamily, _fontBaseSize);
                    _onFontChanged(_fontFamily, _fontBaseSize);
                }
            };
```
Reset button:
```csharp
            var reset = new ThemeButton("Reset to defaults");
            reset.Click += () =>
            {
                rowHeightSlider.Value = VisualStyle.DefaultRowHeight;
                opacitySlider.Value = MeterSettings.DefaultOpacity;
                _fontFamily = null;
                _fontBaseSize = 13.0;
                fontValue.Text = FontLabel(_fontFamily, _fontBaseSize);
                _onFontChanged(_fontFamily, _fontBaseSize);
            };
```
(`rowHeightSlider`/`opacitySlider` must be in scope where `reset` is built — declare all three controls before `reset`. The slider `.Value` setter fires `ValueChanged`, which re-invokes the change callbacks, preserving today's reset-applies-and-persists behavior.)

- [ ] **Step 4: Token the remaining chrome literals**

- `title` foreground `new SolidColorBrush(OverlayTheme.Text)` → `Theme.TextPrimary`; the title's own left inset `Thickness(12,0,0,0)` is unchanged.
- `fontValue` foreground `new SolidColorBrush(OverlayTheme.Text)` → `Theme.TextPrimary`.
- the font-row label + secondary-row label foregrounds `Color.FromArgb(255,0xC4,0xCA,0xD6)` → `Theme.TextLabel`; label `Width` 60 → 112.
- the title-bar divider `Border { Background = new SolidColorBrush(OverlayTheme.CalmBorder) }` → `Theme.Divider`.
- the outer `Border` (`:238-245`): `Background = new SolidColorBrush(Color.FromArgb(252,20,23,29))` → `Theme.Surface(0xFC)`; `BorderBrush = new SolidColorBrush(OverlayTheme.CalmBorder)` → `Theme.Divider`; `CornerRadius`/`BorderThickness` unchanged.
- **Row rhythm:** set the `fontRow` (`:138`) and `secondaryRow` (`:163`) `StackPanel` margins from `Thickness(0,0,0,10)` to `Thickness(0,0,0,16)`, so all four body rows share the wrap-safe 16 rhythm (Tasks 1-2 build the row-height and opacity rows at 16). The `secondaryRow` margin is the only edit to that otherwise-untouched row.
- **Leave** the `secondary` `ComboBox` (`:143-165`) and its `secondaryRow`'s *contents* untouched — removed in increment 4 (only its container margin changes per the rhythm bullet above).

- [ ] **Step 5: Add the `TryParseNumber` helper**

At the bottom of the class, alongside the existing `Percent`/`Px`/`FontLabel` statics:
```csharp
        private static bool TryParseNumber(string text, out double value)
            => double.TryParse((text ?? "").Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value);
```
Remove the now-unused `Percent`/`Px` helpers if nothing references them (the `ThemeSlider` format lambdas inline that formatting); keep `FontLabel`.

- [ ] **Step 6: Delete the now-unused fields**

Remove `_opacity`, `_opacityValue`, `_rowHeight`, `_rowHeightValue` field declarations (`:15-20`) — the sliders are locals now, their callbacks captured in the `ValueChanged` lambdas.

- [ ] **Step 7: Verify no dangling references**

Run: `grep -n "_rowHeight\b\|_rowHeightValue\|_opacity\b\|_opacityValue\|OverlayTheme\|FromArgb(2" src/eq2auras.Plugin/Overlay/MeterSettingsWindow.cs`
Expected: no matches except inside the untouched `secondary` `ComboBox` block (which uses none of these) — i.e. no `OverlayTheme`, no raw `FromArgb`, no deleted fields referenced.

- [ ] **Step 8: Commit**

```bash
git add src/eq2auras.Plugin/Overlay/MeterSettingsWindow.cs
git commit -m "Meter settings window: rewrite on Theme + ThemeButton/ThemeSlider; window opacity label; secondary ComboBox kept for inc 4 (increment 2)"
```

---

## Testing strategy

**Core (Mac):** no Core changes this increment — run `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj` once to confirm the 184 still pass (nothing should move).

**Plugin (CI):** push `styling-theme-system`; the verify-only CI must compile the WPF plugin with `ThemeButton`, `ThemeSlider` (including the `XamlReader.Parse` template — a compile-clean string), and the rewritten settings window. A green compile is the transcription gate.

**On-box field script (merge-gate — first visibly-themed surface):**
1. Update on `dev-latest`, open a meter's ⚙ settings window.
2. **Chrome:** window is dark (the `SurfaceTint` at near-solid opacity, `Theme.Surface(0xFC)`), bordered, rounded; labels read light-grey, values light.
3. **Buttons:** ✕, Choose…, and Reset are bordered buttons that **brighten on hover**; Choose… opens the font dialog; ✕ closes; Reset returns row-height + opacity to defaults and re-applies live.
4. **Sliders:** row-height and window-opacity sliders are **dark** (dark track, grey round thumb); dragging changes the value live; the **value box is editable** — click it, type a number, press Enter → the row/opacity updates and the box reflects the clamped/snapped value; an out-of-range or garbage entry snaps back to the last good value.
5. **Secondary:** the `Secondary` dropdown still works (unchanged — it relocates next increments).
6. **Timers unregressed:** a glance — no shared bar-primitive change this increment.

## Self-review

**Spec coverage (increment 2 scope):** §The theme system's "chromed button" → Task 1; "slider with an editable type-in value" → Task 2; "no raw ACT chrome … the sliders, buttons, and the window itself are the kit's" (§Configuration) → Task 3. `LinkNormal`/`LinkHover` subsumed by the chromed button (documented in Scope boundaries); `ItemSelected`, popup, backdrop knob, header, `MetricRegistry` split, checkbox → later increments/efforts, not gaps.

**Placeholder scan:** none — full code and exact edits throughout.

**Type consistency:** `ThemeButton.Click` is `event Action`; `close.Click += Close` matches (`Window.Close()` is `Action`-compatible — parameterless void). `ThemeSlider.ValueChanged` is `event Action<double>`; `+= v => _onRowHeightChanged(v)` matches the ctor's `Action<double> onRowHeightChanged`. `ThemeSlider.Value` is `double`. `TryParseNumber` is `(string, out double) → bool`. The `format`/`parse` lambdas match `Func<double,string>` / `Func<string,double?>`. The `MeterSettingsWindow` ctor signature is unchanged, so its sole caller (`MeterWindow.cs:326`) compiles unmodified.

## Plan-watch items carried forward

Increment 2 lands the rest of plan-watch **#6** it can (the button + slider kit primitives; the popup panel + list-item are increment 4, the checkbox is deferred to the timer effort). **#5** (popup replaces the `ContextMenu` *and* the secondary `ComboBox`) is **partially touched**: the `ComboBox` is deliberately retained here and removed in increment 4 — noted so the plan review does not read its retention as a miss. **#1/#2/#3/#4** belong to increments 3-4.
