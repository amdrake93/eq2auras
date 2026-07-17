# Parse Meter Slice 2 — Increment 4 (Font knob) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans (inline, owner-watched). Steps use checkbox (`- [ ]`) syntax.

**Goal:** Add a per-window font knob (family + base size) to the meter's settings window, applied live, via the native font picker — the same idiom as the timer tab.

**Architecture:** Add `FontFamily` (string) + `FontBaseSize` (double? DIPs) to `MeterWindowConfig`, resolved into `VisualStyle.Font`/`BaseSize` by the host (same shape as `PanelSettings`). The settings window's "Choose…" opens `System.Windows.Forms.FontDialog` (points↔DIP conversion, store DIPs — verbatim the timer tab pattern). Font applies **in place** — the shared primitive already exposes `NameText`/`TrailingText`, and the header keeps its four text-block refs — so `ApplyFont` re-stamps existing `TextBlock`s (no recreation), and new slots read the live `_style`.

**Tech Stack:** C# — Core `netstandard2.0` (xUnit, Mac-testable); Plugin `net472`/WPF (CI-compiled); WinForms `FontDialog` (plugin already references WinForms).

## Global Constraints

_(same as Increments 1–3 — carried verbatim)_

- **Single-assembly packaging**; new files auto-globbed. **No `async` in the plugin**; no non-GAC field types. **Never** reference `System.Web.Extensions`. JSON = DCJS.
- **DCJS skips field initializers** → nullable = "unset, use default"; a string family `null` = system default.
- **FontDialog stores DIPs:** convert points→DIPs as `SizeInPoints * 96.0 / 72.0` and DIPs→points as `* 72.0 / 96.0` (verbatim from the timer tab's font button in `Eq2AurasPlugin.cs`).
- **Core `netstandard2.0`, Mac-testable**; **never build the Plugin on the Mac**.
- **Branch `meter-slice2` integration branch** — build on it; **do not merge**. Checkpoint = verify-only CI green.
- **Clone completeness** (inc-2 lesson): both new `MeterWindowConfig` fields must be copied in `OverlayHost.AddClonedWindow`.
- **Ctor arg count:** `MeterWindow` reaches 15 positional args here; `onFontChanged` is `Action<string,double>` — a distinct type from the adjacent `Action<double>` callbacks, so no silent-swap risk. The `MeterWindowCallbacks` bundle remains a deferred cleanup (revisit at inc-5 if edge-resize adds same-typed callbacks).

## File Structure

| File | Create/Modify | Responsibility |
|---|---|---|
| `src/eq2auras.Core/Config/MeterWindowConfig.cs` | Modify | Add `string FontFamily`, `double? FontBaseSize`. |
| `tests/eq2auras.Core.Tests/MeterSettingsTests.cs` | Modify | Font family+size roundtrip test. |
| `src/eq2auras.Plugin/Overlay/MeterRowVisual.cs` | Modify | `SetFont(VisualStyle)` → re-stamp name/value/percent. |
| `src/eq2auras.Plugin/Overlay/MeterWindow.cs` | Modify | `_affordance` field; `SetFont`; `ApplyHeaderFont`; ctor +`onFontChanged`. |
| `src/eq2auras.Plugin/Overlay/MeterSettingsWindow.cs` | Modify | Font row ("Choose…" → `FontDialog`); ctor +3 params. |
| `src/eq2auras.Plugin/Overlay/OverlayHost.cs` | Modify | `MeterStyle(config)` sets Font/BaseSize; clone copies both; pass `onFontChanged`. |

---

## Task 1: Core — per-window font family + size (TDD, Mac)

**Files:**
- Modify: `src/eq2auras.Core/Config/MeterWindowConfig.cs`
- Test: `tests/eq2auras.Core.Tests/MeterSettingsTests.cs`

**Interfaces:**
- Produces: `MeterWindowConfig.FontFamily` (`string`, null = system default); `MeterWindowConfig.FontBaseSize` (`double?` DIPs, null = 13). No clamp (matches `PanelSettings`, which the FontDialog bounds).

- [ ] **Step 1: Add the font roundtrip test**

Append inside `MeterSettingsTests` (before the closing brace):

```csharp
    [Fact]
    public void Window_font_roundtrips()
    {
        var settings = new Settings();
        settings.Meter.Enabled = true;
        settings.Meter.Windows = new List<MeterWindowConfig>
        {
            new MeterWindowConfig { FontFamily = "Consolas", FontBaseSize = 18.0 },
        };

        var parsed = Settings.Parse(settings.ToJson());

        Assert.Equal("Consolas", parsed.Meter.Windows[0].FontFamily);
        Assert.Equal(18.0, parsed.Meter.Windows[0].FontBaseSize);
    }

    [Fact]
    public void Null_font_stays_null_meaning_default()
    {
        var json = "{\"meter\":{\"enabled\":true,\"windows\":[{\"metricKey\":\"encdps\"}]}}";

        var parsed = Settings.Parse(json);

        Assert.Null(parsed.Meter.Windows[0].FontFamily);     // null -> system default
        Assert.Null(parsed.Meter.Windows[0].FontBaseSize);   // null -> 13 DIPs
    }
```

- [ ] **Step 2: Run — verify FAIL**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
Expected: build failure — `MeterWindowConfig` has no `FontFamily`/`FontBaseSize`.

- [ ] **Step 3: Add the font members**

In `MeterWindowConfig.cs`, add after the `RowHeight` member:

```csharp
        [DataMember(Name = "fontFamily")]
        public string FontFamily { get; set; }        // null = system default

        [DataMember(Name = "fontBaseSize")]
        public double? FontBaseSize { get; set; }      // WPF DIPs; null = 13
```

- [ ] **Step 4: Run — verify all PASS**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
Expected: PASS, green (154 + 2 new = 156).

- [ ] **Step 5: Commit**

```bash
git add src/eq2auras.Core/Config/MeterWindowConfig.cs tests/eq2auras.Core.Tests/MeterSettingsTests.cs
git commit -m "Meter slice2 inc4: per-window FontFamily + FontBaseSize (Core)"
```

---

## Task 2: Live font apply — row + header (Plugin, CI-verified)

**Files:**
- Modify: `src/eq2auras.Plugin/Overlay/MeterRowVisual.cs`
- Modify: `src/eq2auras.Plugin/Overlay/MeterWindow.cs`

**Interfaces:**
- Produces: `MeterRowVisual.SetFont(VisualStyle)`; `MeterWindow.SetFont(string, double)`; `MeterWindow` ctor gains `Action<string, double> onFontChanged`.

- [ ] **Step 1: Re-stamp the row's fonts**

In `MeterRowVisual.cs`, add after `SetRowHeight`:

```csharp
        /// Live font (SPEC Part III §Configuration): re-stamp the retained row's text via
        /// ApplyFont — the shared primitive exposes NameText/TrailingText; percent stays the
        /// dimmer, slightly-smaller role. No recreation.
        public void SetFont(VisualStyle style)
        {
            style.ApplyFont(_bar.NameText, style.RowText);
            style.ApplyFont(_bar.TrailingText, style.RowText);
            style.ApplyFont(_percent, style.RowText * 11.0 / 13.0);
        }
```

- [ ] **Step 2: Keep a ref to the cog block + the font callback field**

In `MeterWindow.cs`, add fields — after `private SolidColorBrush _headerBackplate;`:

```csharp
        private TextBlock _affordance;
```

And after `private readonly Action<double> _onRowHeightChanged;`:

```csharp
        private readonly Action<string, double> _onFontChanged;
```

- [ ] **Step 3: Capture the affordance into the field**

In `MeterWindow.cs`, change:

```csharp
            var affordance = HeaderBlock(style, dim: true);
            affordance.Text = " ⚙";   // ⚙ — opens the settings window (SPEC Part III §Header)
            affordance.Cursor = System.Windows.Input.Cursors.Hand;
            affordance.MouseLeftButtonDown += (s, e) =>
```

to (assign the field, then keep using the local for the rest):

```csharp
            _affordance = HeaderBlock(style, dim: true);
            var affordance = _affordance;
            affordance.Text = " ⚙";   // ⚙ — opens the settings window (SPEC Part III §Header)
            affordance.Cursor = System.Windows.Input.Cursors.Hand;
            affordance.MouseLeftButtonDown += (s, e) =>
```

- [ ] **Step 4: Extend the constructor**

Change the signature from:

```csharp
            Action<double> onOpacityChanged, Action<double> onRowHeightChanged, Action onNewWindow, Action onCloseWindow, Func<bool> canClose)
```

to:

```csharp
            Action<double> onOpacityChanged, Action<double> onRowHeightChanged, Action<string, double> onFontChanged, Action onNewWindow, Action onCloseWindow, Func<bool> canClose)
```

And after `_onRowHeightChanged = onRowHeightChanged;` add:

```csharp
            _onFontChanged = onFontChanged;
```

- [ ] **Step 5: Add `SetFont` + `ApplyHeaderFont`**

In `MeterWindow.cs`, add after `SetRowHeight`:

```csharp
        /// Live font: re-point _style (family + base size), re-stamp the header text and
        /// every retained row in place; new slots read the live _style. Persisted.
        public void SetFont(string fontFamily, double baseSize)
        {
            _style = new VisualStyle
            {
                RowWidth = _style.RowWidth,
                RowHeight = _style.RowHeight,
                RadialSize = _style.RadialSize,
                RowSpacing = _style.RowSpacing,
                Font = fontFamily != null ? new FontFamily(fontFamily) : null,
                BaseSize = baseSize,
            };
            ApplyHeaderFont();
            foreach (var slot in _slots) slot.SetFont(_style);
            _onFontChanged(fontFamily, baseSize);
        }

        private void ApplyHeaderFont()
        {
            _style.ApplyFont(_durationText, _style.RowText);
            _style.ApplyFont(_titleText, _style.RowText);
            _style.ApplyFont(_metricText, _style.RowText);
            _style.ApplyFont(_totalText, _style.RowText);
            _style.ApplyFont(_affordance, _style.RowText);
        }
```

_(`FontFamily` resolves via MeterWindow's `using System.Windows.Media;` — already present.)_

- [ ] **Step 6: Commit**

```bash
git add src/eq2auras.Plugin/Overlay/MeterRowVisual.cs src/eq2auras.Plugin/Overlay/MeterWindow.cs
git commit -m "Meter slice2 inc4: live in-place font apply (header text + rows via ApplyFont)"
```

---

## Task 3: Font picker + settings/host wiring (Plugin, CI-verified)

**Files:**
- Modify: `src/eq2auras.Plugin/Overlay/MeterSettingsWindow.cs`
- Modify: `src/eq2auras.Plugin/Overlay/MeterWindow.cs`
- Modify: `src/eq2auras.Plugin/Overlay/OverlayHost.cs`

**Interfaces:**
- Consumes: `MeterWindow.SetFont` (Task 2); `config.FontFamily`/`config.FontBaseSize` (Task 1).
- Produces: `MeterSettingsWindow(double rowHeight, Action<double> onRowHeightChanged, double opacity, Action<double> onOpacityChanged, string fontFamily, double fontBaseSize, Action<string, double> onFontChanged)`.

- [ ] **Step 1: Add the font row to the settings window**

In `MeterSettingsWindow.cs`, change the ctor signature from:

```csharp
        public MeterSettingsWindow(double rowHeight, Action<double> onRowHeightChanged, double opacity, Action<double> onOpacityChanged)
        {
            _onOpacityChanged = onOpacityChanged;
            _onRowHeightChanged = onRowHeightChanged;
```

to:

```csharp
        public MeterSettingsWindow(double rowHeight, Action<double> onRowHeightChanged, double opacity, Action<double> onOpacityChanged,
            string fontFamily, double fontBaseSize, Action<string, double> onFontChanged)
        {
            _onOpacityChanged = onOpacityChanged;
            _onRowHeightChanged = onRowHeightChanged;
            _onFontChanged = onFontChanged;
            _fontFamily = fontFamily;
            _fontBaseSize = fontBaseSize;
```

Add the fields (after `_rowHeightValue`):

```csharp
        private readonly Action<string, double> _onFontChanged;
        private string _fontFamily;
        private double _fontBaseSize;
```

Build the font row — insert just before the `opacityLabel` block:

```csharp
            var fontLabel = new TextBlock
            {
                Text = "Font",
                Foreground = new SolidColorBrush(Color.FromArgb(255, 0xC4, 0xCA, 0xD6)),
                VerticalAlignment = VerticalAlignment.Center,
                Width = 60
            };
            var fontValue = new TextBlock
            {
                Text = FontLabel(fontFamily, fontBaseSize),
                Foreground = new SolidColorBrush(OverlayTheme.Text),
                VerticalAlignment = VerticalAlignment.Center
            };
            var choose = new TextBlock
            {
                Text = "  Choose…",
                Foreground = new SolidColorBrush(Color.FromArgb(255, 0x8B, 0x93, 0xA3)),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };
            choose.MouseLeftButtonDown += (s, e) =>
            {
                using (var dialog = new System.Windows.Forms.FontDialog())
                {
                    var currentFamily = _fontFamily ?? System.Drawing.SystemFonts.MessageBoxFont.Name;
                    dialog.Font = new System.Drawing.Font(currentFamily, (float)(_fontBaseSize * 72.0 / 96.0));
                    if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                    _fontFamily = dialog.Font.Name;
                    _fontBaseSize = dialog.Font.SizeInPoints * 96.0 / 72.0;   // points -> DIPs
                    fontValue.Text = FontLabel(_fontFamily, _fontBaseSize);
                    _onFontChanged(_fontFamily, _fontBaseSize);
                }
            };
            var fontRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            fontRow.Children.Add(fontLabel);
            fontRow.Children.Add(fontValue);
            fontRow.Children.Add(choose);
```

Add `fontRow` to the body between the row-height and opacity rows. Replace:

```csharp
            var body = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };
            body.Children.Add(rowHeightRow);
            body.Children.Add(opacityRow);
            body.Children.Add(reset);
```

with:

```csharp
            var body = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };
            body.Children.Add(rowHeightRow);
            body.Children.Add(fontRow);
            body.Children.Add(opacityRow);
            body.Children.Add(reset);
```

Add the label formatter next to `Px`:

```csharp
        private static string FontLabel(string family, double dip)
            => (family ?? "default") + " · " + Math.Round(dip * 72.0 / 96.0) + " pt";
```

Also update the now-inaccurate class-doc (row height and font have both landed). Replace:

```csharp
    /// dark and custom-chromed, modeless and live-applying. Increment 2 carries one knob
    /// (opacity); row height (inc 3) and font (inc 4) land here next.
```

with:

```csharp
    /// dark and custom-chromed, modeless and live-applying: row height, font, and opacity,
    /// per window.
```

- [ ] **Step 2: Pass font when opening settings**

In `MeterWindow.cs`, in `OpenSettings`, change:

```csharp
            _settings = new MeterSettingsWindow(_style.RowHeight, SetRowHeight, _opacity, SetOpacity)
```

to:

```csharp
            _settings = new MeterSettingsWindow(_style.RowHeight, SetRowHeight, _opacity, SetOpacity,
                _style.Font?.Source, _style.BaseSize, SetFont)
```

_(`FontFamily.Source` is the family name string; null when unset — the settings window resolves null to the system default for the dialog seed.)_

- [ ] **Step 3: Host — style from config, clone, callback**

In `OverlayHost.cs`, change `MeterStyle` to carry font. Replace the method **and its now-stale comment** (the "increment 1 uses baked defaults" line went stale in inc-3 when RowHeight became config-driven):

```csharp
        // Meter rows touch (SPEC Part III §Meter display defaults); per-window size/font/
        // opacity knobs arrive in later increments, so increment 1 uses baked defaults.
        private static VisualStyle MeterStyle(MeterWindowConfig config)
            => new VisualStyle { RowSpacing = 0, RowHeight = config.RowHeight ?? VisualStyle.DefaultRowHeight };
```

with:

```csharp
        // Per-window style resolved from the config: zero row spacing (meter rows touch —
        // SPEC Part III §Meter display defaults) plus the configurable row height and font;
        // width stays default until the edge-resize increment.
        private static VisualStyle MeterStyle(MeterWindowConfig config)
            => new VisualStyle
            {
                RowSpacing = 0,
                RowHeight = config.RowHeight ?? VisualStyle.DefaultRowHeight,
                Font = config.FontFamily != null ? new System.Windows.Media.FontFamily(config.FontFamily) : null,
                BaseSize = config.FontBaseSize ?? 13.0,
            };
```

In `AddClonedWindow`'s clone initializer, add after `RowHeight = source.RowHeight,`:

```csharp
                FontFamily = source.FontFamily,
                FontBaseSize = source.FontBaseSize,
```

In `AddMeterWindow`, add the font persist callback after the `rowHeight => ...` line:

```csharp
                rowHeight => SettingsStore.Update(_settings, () => config.RowHeight = rowHeight),
                (family, size) => SettingsStore.Update(_settings, () => { config.FontFamily = family; config.FontBaseSize = size; }),
```

- [ ] **Step 4: Commit**

```bash
git add src/eq2auras.Plugin/Overlay/MeterSettingsWindow.cs src/eq2auras.Plugin/Overlay/MeterWindow.cs src/eq2auras.Plugin/Overlay/OverlayHost.cs
git commit -m "Meter slice2 inc4: font picker (native FontDialog) in the settings window + host wiring"
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
Expected: success — Core tests (156) pass, WPF plugin compiles (FontDialog interop included), artifact staged, no publish.

- [ ] **Step 2: On green, proceed to Increment 5 (edge-resize).** Field-test deferred to end-of-slice. Do NOT merge.

---

## Self-Review

**1. Spec coverage** (SPEC Part III §Configuration — font via native picker stored in DIPs; §Settings — per-window persisted font):
- Per-window `FontFamily`/`FontBaseSize` → Task 1. ✓
- Native picker, DIP storage (points↔DIP) → Task 3 Step 1 (`FontDialog`, `* 96/72` / `* 72/96`). ✓
- Live font apply to header + rows → Task 2 (`SetFont`/`ApplyHeaderFont`/`MeterRowVisual.SetFont`). ✓
- Font persists per window → Task 3 Step 3 (persist callback). ✓
- New-window clone carries font (clone lesson) → Task 3 Step 3. ✓

**2. Placeholder scan:** No TBD/TODO; complete code in every step; commands have expected output. ✓

**3. Type consistency:** `MeterWindowConfig.FontFamily`(string)/`FontBaseSize`(double?) defined Task 1, consumed Task 3 (`config.FontFamily`, `source.FontFamily`); `MeterRowVisual.SetFont(VisualStyle)` / `MeterWindow.SetFont(string,double)` defined Task 2, called Task 2/Task 3 Step 2; `MeterSettingsWindow(...7 args...)` defined Task 3 Step 1 matches the `new` in Task 3 Step 2 (`_style.Font?.Source, _style.BaseSize, SetFont`); `MeterWindow` 15-arg ctor defined Task 2 Step 4 matches the host call updated in Task 3 Step 3 (`onFontChanged` after `onRowHeightChanged`, distinct `Action<string,double>` type). `MeterStyle` returns a `VisualStyle` with `Font`/`BaseSize` consumed by both `MeterWindow` and the settings-open seed. ✓
