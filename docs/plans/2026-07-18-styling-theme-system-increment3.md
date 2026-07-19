# Styling / Theme System — Increment 3 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the meter a **persistent dark backdrop** at its reserved row-count height (the dormant-state affordance), driven by a new **backdrop-opacity** knob that compounds with the existing (now-renamed) **window-opacity** — and decouple the window's reserved size from the current ally count so it can be sized up past the present rows.

**Architecture:** A stretch-filled `SurfaceTint` `Border` sits behind `_rowsPanel` inside a Grid whose `MinHeight` = `_visibleRows × rowHeight`; `SizeToContent.Height` then reserves that height whether or not rows are present. The backdrop's `UIElement.Opacity` is `windowOpacity × backdropOpacity` (they compound); rows/header keep window-opacity via the existing per-element mechanism (text stays full). The backdrop-opacity value threads `MeterWindowConfig.BackdropOpacity` (landed unused in increment 1) → host → window → a new settings-window `ThemeSlider`.

**Tech Stack:** C# / net472 / WPF (Plugin — **not** Mac-testable; verified by CI compile + code review + the on-box field script). No Core changes.

## Global Constraints

- Single-assembly packaging; no second DLL; no `async`; no `System.Web.Extensions`; no `Assembly.LoadFrom`.
- **The wall clock owns the visuals / retain-elements-animate-properties** — unaffected (no per-tick change; the backdrop is a static element resized only on knob/resize).
- **Never outlive the data / display what ACT reports** — unaffected (the backdrop is chrome, not data; it shows *reserved space*, never fabricated rows).
- Semantic tokens: the backdrop is `Theme.Surface(0xFF)` with dynamic `Border.Opacity`.
- **Frozen-brush discipline:** `Theme.Surface(...)` returns a *frozen* brush — never mutate its `.Opacity`. The backdrop's dynamic opacity lives on the `Border.Opacity` (a `UIElement` property), not the brush. The existing header/row backplates keep their own non-frozen `OverlayTheme.MeterBackplate` brushes unchanged (their retag to `Theme` is deferred — `MeterBackplate` and `SurfaceTint` are near-identical, so no visible seam).

**Branch:** continues on `styling-theme-system`.

## Scope boundaries

- **Header/row backplates keep `OverlayTheme.MeterBackplate`** this increment — the persistent backdrop uses `SurfaceTint`; the two tints are within ~4/channel, so no seam. Fully unifying `MeterBackplate` into `Theme` is a later cosmetic cleanup, not this increment (it would collide with the backplate brushes' mutated `.Opacity`).
- **`DefaultBackdropOpacity` stays 1.0** (increment 1's value) — a fully-solid default backdrop is the no-surprise choice; the faint-dormant look is a user dial. Retuning the shipped default is a field decision, not a code change here.

---

## File structure

| File | Responsibility | Change |
|---|---|---|
| `src/eq2auras.Plugin/Overlay/MeterWindowCallbacks.cs` | per-window callbacks | **modify** — add `BackdropOpacityChanged` |
| `src/eq2auras.Plugin/Overlay/OverlayHost.cs` | window construction + persistence | **modify** — pass backdrop-opacity + callback; inherit it on New |
| `src/eq2auras.Plugin/Overlay/MeterWindow.cs` | the meter window | **modify** — backdrop element, reserved height, opacity split, resize decouple, settings args |
| `src/eq2auras.Plugin/Overlay/MeterSettingsWindow.cs` | settings window | **modify** — 2 new ctor params + a Backdrop-opacity `ThemeSlider` |

---

### Task 1: Callbacks + host wiring (transcribe)

**Files:**
- Modify: `MeterWindowCallbacks.cs:14` (after `OpacityChanged`)
- Modify: `OverlayHost.cs:110` (ctor arg), `:118` (callback), `:137-145` (New inherits)

**Interfaces:**
- Produces: `MeterWindowCallbacks.BackdropOpacityChanged` (`Action<double>`). The `MeterWindow` ctor gains a `double backdropOpacity` parameter (Task 2) — the host passes `config.BackdropOpacity ?? MeterSettings.DefaultBackdropOpacity`.

- [ ] **Step 1: Add the callback field**

`MeterWindowCallbacks.cs`, after `public Action<double> OpacityChanged;` (`:14`):
```csharp
        public Action<double> BackdropOpacityChanged;
```

- [ ] **Step 2: Pass the value + wire persistence in `AddMeterWindow`**

`OverlayHost.cs`: change the ctor call's opacity argument line (`:110`) from
```csharp
                config.Opacity ?? MeterSettings.DefaultOpacity,
```
to (insert the backdrop value right after — it becomes the ctor's new param, Task 2):
```csharp
                config.Opacity ?? MeterSettings.DefaultOpacity,
                config.BackdropOpacity ?? MeterSettings.DefaultBackdropOpacity,
```
and add the persistence callback after `OpacityChanged` (`:118`):
```csharp
                    BackdropOpacityChanged = v => SettingsStore.Update(_settings, () => config.BackdropOpacity = v),
```

- [ ] **Step 3: New window inherits backdrop opacity (an appearance knob)**

`OverlayHost.AddNewWindow`, in the `created` initializer (`:137-145`), after `Opacity = source.Opacity,`:
```csharp
                BackdropOpacity = source.BackdropOpacity,
```

- [ ] **Step 4: Commit**

```bash
git add src/eq2auras.Plugin/Overlay/MeterWindowCallbacks.cs src/eq2auras.Plugin/Overlay/OverlayHost.cs
git commit -m "Meter: thread backdrop-opacity config through host + callbacks; New inherits it (increment 3)"
```

---

### Task 2: `MeterWindow` — persistent backdrop, reserved height, opacity split, resize decouple (transcribe)

**Files:**
- Modify: `MeterWindow.cs` — fields (`:30-31`), ctor signature (`:51`), ctor body (`:60-61`, `:148-151`), `OpenSettings` (`:326-327`), `SetOpacity` (`:338-344`), `SetRowHeight`, `SetVisibleRows` (`:433-437`), resize anchor (`:454-456`).

**Interfaces:**
- Consumes: `MeterWindowCallbacks.BackdropOpacityChanged` (Task 1), `Theme.Surface(byte)`.
- Produces: `public void SetBackdropOpacity(double)`; ctor param `double backdropOpacity` (after `double opacity`).

- [ ] **Step 1: Add fields** — after `private double _opacity;` (`:30`):
```csharp
        private double _backdropOpacity;
        private Border _backdrop;
        private Grid _rowsContainer;
```

- [ ] **Step 2: Ctor signature** — add the param after `double opacity` (`:51`):
```csharp
        public MeterWindow(double left, double top, VisualStyle style, string metricKey, string secondaryKey, bool locked, double opacity, double backdropOpacity, int visibleRows,
            MeterWindowCallbacks callbacks)
```
and store it after `_opacity = opacity;` (`:60`):
```csharp
            _backdropOpacity = backdropOpacity;
```

- [ ] **Step 3: Build the backdrop behind the rows** — replace the `_rowsPanel`/`_root` assembly (`:148-151`):
```csharp
            _rowsPanel = new StackPanel();
            _backdrop = new Border
            {
                Background = Theme.Surface(0xFF),                 // SurfaceTint; opacity via Border.Opacity below
                Opacity = _opacity * _backdropOpacity,            // window × backdrop (they compound, SPEC §Meter display defaults)
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            _rowsContainer = new Grid { MinHeight = ReservedRowsHeight() };
            _rowsContainer.Children.Add(_backdrop);              // behind
            _rowsContainer.Children.Add(_rowsPanel);            // on top; empty area shows the backdrop
            _root = new StackPanel { Width = style.RowWidth };
            _root.Children.Add(header);
            _root.Children.Add(_rowsContainer);
```

- [ ] **Step 4: Add the reserved-height helper** — near `RenderSlots` (after `:317`):
```csharp
        /// The window reserves its configured row count as a persistent backdrop regardless of
        /// how many allies are present (SPEC §Configuration): the dark region is always this tall,
        /// so an empty meter shows its size and can be sized up past the present rows.
        private double ReservedRowsHeight() => _visibleRows * _style.RowHeight;
```

- [ ] **Step 5: Opacity split** — replace `SetOpacity` (`:338-344`):
```csharp
        /// Window opacity (SPEC §Meter display defaults): the whole window's fill/backplates —
        /// header + rows + the backdrop (which also takes backdrop opacity, compounded). Text
        /// stays full. Persisted.
        public void SetOpacity(double opacity)
        {
            _opacity = opacity;
            _headerBackplate.Opacity = opacity;
            foreach (var slot in _slots) slot.SetOpacity(opacity);
            _backdrop.Opacity = _opacity * _backdropOpacity;
            _cb.OpacityChanged(opacity);
        }

        /// Backdrop opacity (SPEC §Meter display defaults): scales just the persistent backdrop,
        /// compounded with window opacity — faint backdrop + vivid bars is low here, high there.
        public void SetBackdropOpacity(double backdropOpacity)
        {
            _backdropOpacity = backdropOpacity;
            _backdrop.Opacity = _opacity * _backdropOpacity;
            _cb.BackdropOpacityChanged(backdropOpacity);
        }
```

- [ ] **Step 6: Keep the reserved height in sync with the row-count and row-height knobs.**

In `SetVisibleRows` (`:433-437`), after `_visibleRows = visibleRows;`:
```csharp
            _rowsContainer.MinHeight = ReservedRowsHeight();
```
In `SetRowHeight` (`:348-361`), after its `_style = new VisualStyle {…}` block re-points `_style` to the new row height (that block ends at `:358`), add:
```csharp
            _rowsContainer.MinHeight = ReservedRowsHeight();
```

- [ ] **Step 7: Decouple the resize anchor from the ally count** — replace the `_startVisibleRows` assignment **together with its now-stale rationale comment** (`:450-456` — the `:450-453` comment describes the removed `min(cap, allies)` collapse and must go with the code it explains, or the file ships two contradictory rationale blocks):
```csharp
                // The window reserves the full visible-row count as a backdrop (persistent,
                // §Configuration), so the bottom drag anchors to the raw cap — a size-up past the
                // present allies now works (previously it anchored to min(cap, allies) and snapped).
                _startVisibleRows = _visibleRows;
```

- [ ] **Step 8: Pass the backdrop knob + callback to the settings window** — in `OpenSettings` (`:326-327`), change the `new MeterSettingsWindow(...)` call to insert the two new args after `SetOpacity`:
```csharp
            _settings = new MeterSettingsWindow(_style.RowHeight, SetRowHeight, _opacity, SetOpacity,
                _backdropOpacity, SetBackdropOpacity,
                _style.Font?.Source, _style.BaseSize, SetFont, _secondaryKey, SetSecondary)
```

- [ ] **Step 9: Commit**

```bash
git add src/eq2auras.Plugin/Overlay/MeterWindow.cs
git commit -m "Meter window: persistent backdrop at reserved row-count height; window/backdrop opacity split; resize decouple (increment 3)"
```

---

### Task 3: `MeterSettingsWindow` — the Backdrop-opacity slider (transcribe)

**Files:**
- Modify: `MeterSettingsWindow.cs` — ctor signature + fields; a new body row after Window opacity; Reset.

**Interfaces:**
- Consumes: `ThemeSlider`, `MeterSettings.MinBackdropOpacity/MaxBackdropOpacity/DefaultBackdropOpacity`.
- Produces: nothing new (ctor gains `double backdropOpacity, Action<double> onBackdropOpacityChanged` after the window-opacity pair).

- [ ] **Step 1: Ctor signature + field** — add the field after `_onOpacityChanged` and the params after the opacity pair:

Field (after `private readonly Action<double> _onOpacityChanged;`):
```csharp
        private readonly Action<double> _onBackdropOpacityChanged;
```
Ctor params — change the signature to insert after `Action<double> onOpacityChanged,`:
```csharp
        public MeterSettingsWindow(double rowHeight, Action<double> onRowHeightChanged, double opacity, Action<double> onOpacityChanged,
            double backdropOpacity, Action<double> onBackdropOpacityChanged,
            string fontFamily, double fontBaseSize, Action<string, double> onFontChanged,
            string secondaryKey, Action<string> onSecondaryChanged)
```
Assign after `_onOpacityChanged = onOpacityChanged;`:
```csharp
            _onBackdropOpacityChanged = onBackdropOpacityChanged;
```

- [ ] **Step 2: Build the Backdrop-opacity row** — after the `opacityRow` block (the Window-opacity slider), add:
```csharp
            var backdropLabel = new TextBlock
            {
                Text = "Backdrop opacity",
                Foreground = Theme.TextLabel,
                VerticalAlignment = VerticalAlignment.Center,
                Width = 112
            };
            var backdropSlider = new ThemeSlider(
                MeterSettings.MinBackdropOpacity, MeterSettings.MaxBackdropOpacity, 0.01, backdropOpacity,
                v => Math.Round(v * 100) + "%",
                t => TryParseNumber(t.Replace("%", ""), out double pct) ? (double?)(pct / 100.0) : null);
            backdropSlider.ValueChanged += v => _onBackdropOpacityChanged(v);
            var backdropRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
            backdropRow.Children.Add(backdropLabel);
            backdropRow.Children.Add(backdropSlider);
```

- [ ] **Step 3: Insert the row into the body** — in the `body.Children.Add(...)` sequence, add it after `opacityRow`:
```csharp
            body.Children.Add(opacityRow);
            body.Children.Add(backdropRow);
```

- [ ] **Step 4: Reset also restores backdrop opacity** — in the `reset.Click` lambda, after `opacitySlider.Value = MeterSettings.DefaultOpacity;`:
```csharp
                backdropSlider.Value = MeterSettings.DefaultBackdropOpacity;
```

- [ ] **Step 5: Commit**

```bash
git add src/eq2auras.Plugin/Overlay/MeterSettingsWindow.cs
git commit -m "Meter settings window: add Backdrop opacity slider (increment 3)"
```

---

## Testing strategy

**Core (Mac):** no Core changes — run `dotnet test …Core.Tests…` once, confirm the 184 hold.

**Plugin (CI):** push; the WPF plugin must compile with the new ctor params (host callsite `OverlayHost.cs` and `MeterWindow.OpenSettings` both updated to match) and the backdrop layout.

**On-box field script (merge-gate — the backdrop is a visible behavior change):**
1. Update on `dev-latest`, enable the meter, open a window with combat.
2. **Persistent backdrop:** with fewer allies than the row count, the window shows a **dark rectangle below the last row** down to the reserved height (not collapsed to the rows). An empty/dormant meter is a dark rectangle of the configured height.
3. **Reserved-size resize (the field fix):** with **one** combatant present, drag the bottom edge **down** — the window grows to reserve more rows (previously it snapped to the single row and couldn't grow). Drag up reduces the reserved count; the value clamps to whole rows; overflow still wheel-scrolls.
4. **Backdrop opacity:** ⚙ → the settings window now has a **Backdrop opacity** slider under Window opacity. Lower it → the empty backdrop region **fades** while the bars stay vivid (faint-backdrop/vivid-bars). Raise it → solid. Type-in works. Persists across reload.
5. **Window opacity** still scales the whole window (bars + backdrop together); with backdrop opacity high and window opacity low, everything fades; with window high and backdrop low, only the empty backdrop fades.
6. **Reset** returns row-height, window-opacity, **and backdrop-opacity** to defaults.
7. **Timers unregressed** (glance).

## Self-review

**Spec coverage (increment 3):** §Meter display defaults' "persistent backdrop … reserved height", the two-slider "window opacity / backdrop opacity … compound", and the faint-backdrop/vivid-bars behavior → Tasks 2-3; §Configuration's "dragging the bottom edge sets the count and it holds … size a meter up … even when only one ally is showing" (the resize decouple) → Task 2 Step 7. Plan-watch #3 (the second opacity axis + independent feed) and #4 (persistent reserved-height + resize-snap fix) land here. `SurfaceTint` unification for the row/header backplate is explicitly deferred (Scope boundaries), not a gap.

**Placeholder scan:** none.

**Type consistency:** `SetBackdropOpacity(double)` matches the settings callback `Action<double>` and the callbacks' `BackdropOpacityChanged` (`Action<double>`). The `MeterWindow` ctor's new `double backdropOpacity` sits between `double opacity` and `int visibleRows`; the sole caller `OverlayHost.cs:103-125` is updated in Task 1 Step 2 to pass it in that position. `MeterSettingsWindow`'s new params sit between the opacity pair and the font args; its sole caller `MeterWindow.OpenSettings` is updated in Task 2 Step 8 to match. `Theme.Surface(0xFF)` returns a frozen `SolidColorBrush` used as `Border.Background`; dynamic opacity is `Border.Opacity` (never the frozen brush's).

## Plan-watch items

Lands #3 (second opacity axis: field already existed from inc 1; this adds the slider + independent feed into the backdrop vs the bar primitive) and #4 (persistent reserved-height backdrop + the resize-snap fix). #1/#2/#5 remain for increment 4; #6's kit is done.
