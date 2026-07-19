# Styling / Theme System — Increment 4 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the meter's raw `ContextMenu` with the themed right-click **popup** — family-column **metric** and **secondary** toggle-grids over the kit's list-item, plus a **lifecycle** cluster (Lock · New meter · Remove meter) and a corner dismiss — and land the Core support for a **cleared primary** (shows nothing) plus the secondary's relocation out of the settings window.

**Architecture:** A `MeterPopup` (WPF `Popup`, `StaysOpen=false`, mouse-placed) hosts two `MetricGrid`s (metrics grouped by `MetricDef.Category` into columns) built from a `MetricGridItem` list-item primitive (normal · muted · selected · hover). Core gains `MetricRegistry.ResolvePrimary` (null = cleared → nothing; unknown → DPS) and `MeterEngine` returns an empty frame for a cleared primary; all window-creation paths seed the DPS key so `null` only ever means a deliberate user clear.

**Tech Stack:** C# (Core netstandard2.0 TDD; Plugin net472/WPF transcribe — CI compile + code review + on-box field). `System.Windows.Controls.Primitives.Popup`.

## Global Constraints

- Single-assembly; no second DLL; no `async`; no `System.Web.Extensions`; no `Assembly.LoadFrom`.
- DCJS: enum/bool defaults at 0-value; nullable numeric null = unset. **A cleared primary is represented as `MetricKey == null`; every seed path writes a non-null key, so null is unambiguously "user cleared".**
- Semantic tokens: the selected list-item uses a new `Theme.ItemSelected` (the deferred token, now consumed) + `Theme.AccentAmber` dot + `Theme.TextPrimary`.
- **Never outlive the data:** a cleared primary renders **no rows** (empty frame), never fabricated content.

**Branch:** continues on `styling-theme-system`.

## Scope boundaries

- The **header cog position + total-caps-value** is increment 5, untouched here.
- Family header **colors** (Damage red / Healing green / Utility blue) are popup category accents defined in `MeterPopup`, not combatant colors.
- Metric families come straight from `MetricDef.Category` ("Damage"/"Healing"/"Utility"); today's 3 metrics = 3 one-item columns, scaling as the registry grows.

---

## File structure

| File | Responsibility | Change |
|---|---|---|
| `src/eq2auras.Core/Meter/MetricRegistry.cs` | metric vocabulary + resolution | **modify** — add `ResolvePrimary` |
| `src/eq2auras.Core/Meter/MeterEngine.cs` | per-poll frame | **modify** — cleared primary → empty frame |
| `src/eq2auras.Core/Config/MeterSettings.cs` | settings + Normalize | **modify** — seed DPS key on the auto-seeded default window |
| `tests/eq2auras.Core.Tests/MetricRegistryTests.cs`, `MeterEngineTests.cs`, `MeterSettingsTests.cs` | Core tests | **modify** — cleared-primary + seed coverage |
| `src/eq2auras.Plugin/Overlay/Theme.cs` | tokens | **modify** — add `ItemSelected` |
| `src/eq2auras.Plugin/Overlay/MetricGridItem.cs` | selectable list-item | **create** |
| `src/eq2auras.Plugin/Overlay/MeterPopup.cs` | the right-click popup | **create** |
| `src/eq2auras.Plugin/Overlay/MeterWindow.cs` | window | **modify** — swap ContextMenu→MeterPopup; cleared-primary render; drop eager Resolve |
| `src/eq2auras.Plugin/Overlay/OverlayHost.cs` | host | **modify** — seed DPS on New |
| `src/eq2auras.Plugin/Overlay/MeterSettingsWindow.cs` | settings | **modify** — remove secondary `ComboBox` + its ctor params |

---

### Task 1: Core — cleared-primary resolution + empty frame + seed (TDD)

**Files:**
- Modify: `MetricRegistry.cs` (add `ResolvePrimary`); `MeterEngine.cs:20` (use it, empty-frame branch); `MeterSettings.cs:72` (seed the auto default window)
- Test: `MetricRegistryTests.cs`, `MeterEngineTests.cs`, `MeterSettingsTests.cs`

**Interfaces:**
- Produces: `MetricRegistry.ResolvePrimary(string key) → MetricDef` (returns `null` when `key == null` — cleared; the DPS default when the key is non-null-but-unknown; the matching def otherwise). `MeterEngine.Tick` returns a `MeterFrame` with empty `Rows`, empty `MetricLabel`, empty `TotalText` when the primary is cleared.

- [ ] **Step 1: Write the failing `ResolvePrimary` tests** — in `MetricRegistryTests.cs`:
```csharp
    [Fact]
    public void ResolvePrimary_null_is_cleared()
        => Assert.Null(MetricRegistry.ResolvePrimary(null));   // cleared -> show nothing

    [Theory]
    [InlineData("")]                 // non-null but unknown -> DPS (forward-compat)
    [InlineData("no-such-metric")]
    public void ResolvePrimary_unknown_nonnull_key_is_dps(string key)
        => Assert.Equal("encdps", MetricRegistry.ResolvePrimary(key).Key);

    [Fact]
    public void ResolvePrimary_known_key_resolves()
        => Assert.Equal("enchps", MetricRegistry.ResolvePrimary("enchps").Key);
```

- [ ] **Step 2: Run — expect fail** (`ResolvePrimary` undefined). `dotnet test …Core.Tests… --filter ResolvePrimary` → FAIL.

- [ ] **Step 3: Implement `ResolvePrimary`** — in `MetricRegistry.cs`, after `Resolve` (`:21-22`):
```csharp
        /// The PRIMARY metric's resolver, distinct from Resolve: a *null* key is a user-cleared
        /// primary (→ null, the window shows nothing, SPEC §Configuration); a non-null but unknown
        /// key still falls back to the DPS default (forward-compat for a newer version's key). All
        /// window-creation paths seed a non-null key, so null arises only from a deliberate clear.
        public static MetricDef ResolvePrimary(string key)
            => key == null ? null : (All.FirstOrDefault(m => m.Key == key) ?? All.First(m => m.Key == DefaultKey));
```

- [ ] **Step 4: Run — expect pass.**

- [ ] **Step 5: Write the failing `MeterEngine` cleared-primary test** — in `MeterEngineTests.cs` (match its existing construction style for `EncounterReading`/`CombatantReading`; a cleared primary yields an empty frame regardless of combatants):
```csharp
    [Fact]
    public void Cleared_primary_yields_an_empty_frame()
    {
        var frame = new MeterEngine().Tick(Live(10),
            new List<CombatantReading> { Ally("A", damage: 1000), Ally("B", damage: 500) },
            metricKey: null, Palette);

        Assert.Empty(frame.Rows);        // cleared primary -> nothing
        Assert.Equal("", frame.MetricLabel);
        Assert.Equal("", frame.TotalText);
    }
```
*(Uses the file's real helpers: `Live(double)` → an active `EncounterReading` (`MeterEngineTests.cs:9`), `Ally(name, damage:)` (`:12`), and the `Palette` field (`:7`) — same construction as the neighboring `Rates_divide_totals_by_the_live_wall_clock_while_active`. The assertion is the contract: cleared → empty rows + blank metric/total.)*

- [ ] **Step 6: Run — expect fail** (currently `Resolve(null)` → DPS → non-empty rows).

- [ ] **Step 7: Implement the cleared-primary branch** — in `MeterEngine.Tick`, replace the primary resolve line (`:20`):
```csharp
            var metric = MetricRegistry.Resolve(metricKey);
```
with:
```csharp
            var metric = MetricRegistry.ResolvePrimary(metricKey);
            if (metric == null)
            {
                // Cleared primary (SPEC §Meter display defaults): show nothing but the backdrop —
                // no rows, blank metric/total. The window still renders its header + backdrop.
                return new MeterFrame
                {
                    Rows = new List<MeterRow>(),
                    DurationText = FormatDuration(EncounterDuration(encounter)),
                    Title = encounter != null && encounter.Exists ? (encounter.Title ?? "") : "",
                    MetricLabel = "",
                    TotalText = "",
                };
            }
```
and extract the duration calc into a helper so both paths share it — replace the inline `double duration = 0; if (encounter != null …) { … }` block (`:27-33`) with a call `double duration = EncounterDuration(encounter);` and add:
```csharp
        private static double EncounterDuration(EncounterReading encounter)
        {
            if (encounter == null || !encounter.Exists) return 0;
            return Math.Max(0, encounter.Active ? encounter.LiveDurationSeconds : encounter.FinalDurationSeconds);
        }
```

- [ ] **Step 8: Run — expect pass** (new cleared-primary test + the full suite, no regressions).

- [ ] **Step 9: Seed the auto-created default window with the DPS key** — in `MeterSettings.Normalize`, the enabled-but-empty seed (`:72`):
```csharp
            if (Enabled && Windows.Count == 0) Windows.Add(new MeterWindowConfig());
```
becomes (the from-nothing seed is at `MeterSettings.cs:75`):
```csharp
            if (Enabled && Windows.Count == 0) Windows.Add(new MeterWindowConfig { MetricKey = Meter.MetricRegistry.DefaultKey });
```
Add the seed-coverage test in `MeterSettingsTests.cs` (adjust the existing `Enabled_with_no_windows_seeds_one_default`, which currently asserts `Assert.Null(window.MetricKey)`):
```csharp
        Assert.Equal("encdps", window.MetricKey);   // seeded so null only ever means user-cleared (SPEC §Settings)
```
*(Legacy migration already carries the flat `MetricKey` into `Windows[0]`, so it is non-null when present; only the from-nothing default needed the seed.)*

- [ ] **Step 10: Run the full Core suite — all green** (`dotnet test …Core.Tests…`).

- [ ] **Step 11: Commit**
```bash
git add src/eq2auras.Core/Meter/MetricRegistry.cs src/eq2auras.Core/Meter/MeterEngine.cs src/eq2auras.Core/Config/MeterSettings.cs tests/eq2auras.Core.Tests/
git commit -m "Meter: cleared-primary resolution (ResolvePrimary null->nothing) + empty frame + seed DPS default (increment 4)"
```

---

### Task 2: Plugin — `Theme.ItemSelected` + `MetricGridItem` list-item (transcribe)

**Files:** Modify `Theme.cs`; Create `MetricGridItem.cs`.

**Interfaces:**
- Produces: `Theme.ItemSelected` (frozen brush, subtle light highlight); `class MetricGridItem : Border` — ctor `(string label, bool selected)`; `event Action Toggled`; `bool Selected { get; set; }` (updates visuals).
- Consumes: `Theme.TextLabel/TextPrimary/AccentAmber/ItemSelected`.

- [ ] **Step 1: Add the token** — in `Theme.cs`, after `AccentBlue`:
```csharp
        public static readonly SolidColorBrush ItemSelected = Frozen(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF));   // subtle light highlight behind a selected item
```

- [ ] **Step 2: Create `MetricGridItem.cs`**
```csharp
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Eq2Auras.Plugin.Overlay
{
    /// A selectable metric row in the popup grid (SPEC §Configuration): normal · hover ·
    /// selected. Selected = amber dot + bright text + a subtle highlight; click TOGGLES
    /// (click a lit one to clear — no "None"). The state vocabulary the kit's list-item carries.
    internal sealed class MetricGridItem : Border
    {
        private readonly Ellipse _dot;
        private readonly TextBlock _label;
        private bool _selected;

        public event Action Toggled;

        public bool Selected
        {
            get { return _selected; }
            set { _selected = value; Apply(); }
        }

        public MetricGridItem(string label, bool selected)
        {
            _dot = new Ellipse { Width = 6, Height = 6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            _label = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(_dot);
            row.Children.Add(_label);

            Child = row;
            Padding = new Thickness(6, 4, 8, 4);
            CornerRadius = new CornerRadius(3);
            Cursor = Cursors.Hand;
            _selected = selected;
            Apply();

            MouseEnter += (s, e) => { if (!_selected) Background = Theme.ItemSelected; };
            MouseLeave += (s, e) => Apply();
            MouseLeftButtonUp += (s, e) => { if (Toggled != null) Toggled(); };
        }

        private void Apply()
        {
            Background = _selected ? Theme.ItemSelected : Brushes.Transparent;
            _label.Foreground = _selected ? Theme.TextPrimary : Theme.TextLabel;
            _label.FontWeight = _selected ? FontWeights.SemiBold : FontWeights.Normal;
            _dot.Fill = _selected ? Theme.AccentAmber : Brushes.Transparent;
        }
    }
}
```

- [ ] **Step 3: Commit**
```bash
git add src/eq2auras.Plugin/Overlay/Theme.cs src/eq2auras.Plugin/Overlay/MetricGridItem.cs
git commit -m "Theme kit: ItemSelected token + MetricGridItem selectable list-item (increment 4)"
```

---

### Task 3: Plugin — `MeterPopup` (the right-click popup) (transcribe)

**Files:** Create `MeterPopup.cs`.

**Interfaces:**
- Produces: `class MeterPopup`; ctor `(UIElement placementTarget, string primaryKey, string secondaryKey, Func<bool> canRemove, MeterPopupCallbacks cb)`; method `void Show()`. `MeterPopupCallbacks { Action<string> PrimaryToggled; Action<string> SecondaryToggled; Action Lock; Action NewMeter; Action RemoveMeter; }` (a nested class). Toggled callbacks pass the new key, or `null` on clear.
- Consumes: `MetricGridItem` (Task 2), `ThemeButton`, `Theme.*`, `MetricRegistry.All`, `MetricDef.Category`.

- [ ] **Step 1: Create `MeterPopup.cs`**
```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Eq2Auras.Core.Meter;

namespace Eq2Auras.Plugin.Overlay
{
    /// The meter's right-click popup (SPEC §Configuration): family-column PRIMARY and SECONDARY
    /// metric toggle-grids, a lifecycle cluster (Lock · New meter · Remove meter, destructive red),
    /// and a corner ✕ dismiss. Replaces the raw ContextMenu. Transient: StaysOpen=false dismisses
    /// on outside click; picking an action closes it.
    internal sealed class MeterPopup
    {
        public sealed class Callbacks
        {
            public Action<string> PrimaryToggled;    // new key, or null to clear
            public Action<string> SecondaryToggled;   // new key, or null to clear
            public Action Lock;
            public Action NewMeter;
            public Action RemoveMeter;
        }

        private static readonly Dictionary<string, Color> CategoryColor = new Dictionary<string, Color>
        {
            { "Damage",  Color.FromRgb(0xE0, 0x5A, 0x5A) },   // red
            { "Healing", Color.FromRgb(0x2F, 0xBF, 0x8F) },   // green/teal
            { "Utility", Color.FromRgb(0x56, 0xB4, 0xE9) },   // blue/sky
        };
        private static readonly Color CategoryFallback = Color.FromRgb(0x8B, 0x93, 0xA3);

        private readonly Popup _popup;
        private readonly Callbacks _cb;
        private readonly List<KeyValuePair<string, MetricGridItem>> _primaryItems = new List<KeyValuePair<string, MetricGridItem>>();
        private readonly List<KeyValuePair<string, MetricGridItem>> _secondaryItems = new List<KeyValuePair<string, MetricGridItem>>();
        private string _primaryKey;
        private string _secondaryKey;

        public MeterPopup(UIElement placementTarget, string primaryKey, string secondaryKey, Func<bool> canRemove, Callbacks cb)
        {
            _cb = cb;
            _primaryKey = primaryKey;
            _secondaryKey = secondaryKey;

            var body = new StackPanel();
            body.Children.Add(SectionLabel("Primary metric"));
            body.Children.Add(BuildGrid(isPrimary: true));
            body.Children.Add(Rule());
            body.Children.Add(SectionLabel("Secondary metric"));
            body.Children.Add(BuildGrid(isPrimary: false));
            body.Children.Add(Rule());
            body.Children.Add(BuildLifecycle(canRemove));

            var dismiss = new ThemeButton("✕")
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 6, 6, 0),
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent
            };
            dismiss.Click += () => _popup.IsOpen = false;

            var overlay = new Grid();
            overlay.Children.Add(body);
            overlay.Children.Add(dismiss);

            var shell = new Border
            {
                Background = Theme.Surface(0xF2),        // near-solid popup surface
                BorderBrush = Theme.Divider,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(2, 4, 2, 4),
                Child = overlay
            };

            _popup = new Popup
            {
                PlacementTarget = placementTarget,
                Placement = PlacementMode.Mouse,
                StaysOpen = false,       // dismiss on outside click
                AllowsTransparency = true,
                Child = shell
            };
        }

        public void Show() { _popup.IsOpen = true; }

        private UIElement BuildGrid(bool isPrimary)
        {
            var grid = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(11, 2, 11, 10) };
            foreach (var family in MetricRegistry.All.Select(m => m.Category).Distinct())
            {
                var col = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
                col.Children.Add(FamilyHeader(family));
                var items = isPrimary ? _primaryItems : _secondaryItems;
                foreach (var metric in MetricRegistry.All.Where(m => m.Category == family))
                {
                    string key = metric.Key;
                    bool selected = isPrimary ? key == _primaryKey : key == _secondaryKey;
                    var item = new MetricGridItem(metric.Label, selected);
                    items.Add(new KeyValuePair<string, MetricGridItem>(key, item));
                    item.Toggled += () => OnToggle(isPrimary, key);
                    col.Children.Add(item);
                }
                grid.Children.Add(col);
            }
            return grid;
        }

        private void OnToggle(bool isPrimary, string key)
        {
            // Toggle: clicking the lit one clears (null); clicking another switches to it.
            if (isPrimary)
            {
                _primaryKey = _primaryKey == key ? null : key;
                RefreshSection(_primaryItems, _primaryKey);
                _cb.PrimaryToggled(_primaryKey);
            }
            else
            {
                _secondaryKey = _secondaryKey == key ? null : key;
                RefreshSection(_secondaryItems, _secondaryKey);
                _cb.SecondaryToggled(_secondaryKey);
            }
        }

        // Single-selection per section: exactly the item whose key == the section's current
        // selection lights; a null selection (cleared) lights nothing.
        private static void RefreshSection(List<KeyValuePair<string, MetricGridItem>> items, string selectedKey)
        {
            foreach (var pair in items) pair.Value.Selected = pair.Key == selectedKey;
        }

        private static UIElement SectionLabel(string text) => new TextBlock
        {
            Text = text.ToUpperInvariant(),
            FontSize = 9,
            Foreground = Theme.TextMuted,
            Margin = new Thickness(13, 11, 13, 3)
        };

        private UIElement FamilyHeader(string family)
        {
            var color = CategoryColor.TryGetValue(family, out var c) ? c : CategoryFallback;
            return new TextBlock
            {
                Text = family,
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(color),
                Margin = new Thickness(6, 4, 6, 4)
            };
        }

        private static UIElement Rule() => new Border { Height = 1, Background = Theme.Divider, Margin = new Thickness(13, 0, 13, 0) };

        private UIElement BuildLifecycle(Func<bool> canRemove)
        {
            var lockBtn = new ThemeButton("Lock"); lockBtn.Click += () => { _cb.Lock(); _popup.IsOpen = false; };
            var newBtn = new ThemeButton("New meter"); newBtn.Click += () => { _cb.NewMeter(); _popup.IsOpen = false; };
            var removeBtn = new ThemeButton("Remove meter")
            {
                Background = new SolidColorBrush(Color.FromRgb(0xA2, 0x32, 0x32)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xB3, 0x40, 0x40))
            };
            removeBtn.Click += () => { if (canRemove()) { _cb.RemoveMeter(); _popup.IsOpen = false; } };

            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(11, 8, 11, 9) };
            row.Children.Add(lockBtn);
            newBtn.Margin = new Thickness(7, 0, 0, 0);
            row.Children.Add(newBtn);
            removeBtn.Margin = new Thickness(14, 0, 0, 0);
            row.Children.Add(removeBtn);
            return row;
        }
    }
}
```

- [ ] **Step 2: Commit**
```bash
git add src/eq2auras.Plugin/Overlay/MeterPopup.cs
git commit -m "Meter: MeterPopup — themed right-click popup (metric/secondary toggle grids + lifecycle) (increment 4)"
```

---

### Task 4: Plugin — wire `MeterWindow` to the popup; cleared-primary render; drop eager Resolve (transcribe)

**Files:** Modify `MeterWindow.cs` (remove `BuildMenu`/`StyleMenu`/`SyncMenuChecks`/`_menu`/`_lockItem`; right-click opens `MeterPopup`; `_metricKey` no longer eager-resolved; `Render` handles empty metric label); `OverlayHost.cs` (seed DPS on `AddNewWindow`).

**Interfaces:** Consumes `MeterPopup` (Task 3). The `MeterWindowCallbacks` already carry `MetricPicked`/`SecondaryPicked`/`LockChanged`/`NewWindow`/`CloseWindow`/`CanClose` — reused as the popup callbacks.

- [ ] **Step 1: Stop eager-resolving the primary** — `MeterWindow.cs:60`:
```csharp
            _metricKey = MetricRegistry.Resolve(metricKey).Key;   // normalize unknown -> default
```
becomes:
```csharp
            _metricKey = metricKey;   // raw: null = cleared (shows nothing); resolution is the engine's (ResolvePrimary)
```

- [ ] **Step 2: Replace the ContextMenu wiring with the popup** — delete `BuildMenu()`, `StyleMenu()`, `SyncMenuChecks()`, the `_menu`/`_lockItem` fields, and the ctor lines `_menu = BuildMenu(); SyncMenuChecks(); header.ContextMenu = _menu;`. Replace with a right-click handler on the window opening a fresh `MeterPopup`:
```csharp
            header.MouseRightButtonUp += (s, e) => OpenPopup(s as UIElement ?? this);
```
and add:
```csharp
        private void OpenPopup(UIElement target)
        {
            var popup = new MeterPopup(target, _metricKey, _secondaryKey, _cb.CanClose, new MeterPopup.Callbacks
            {
                PrimaryToggled = key => { _metricKey = key; _cb.MetricPicked(key); },
                SecondaryToggled = SetSecondary,   // reuse MeterWindow.SetSecondary (sets _secondaryKey + persists) — keeps it live
                Lock = () => { _locked = !_locked; UpdateGrips(); _cb.LockChanged(_locked); },
                NewMeter = () => _cb.NewWindow(),
                RemoveMeter = () => _cb.CloseWindow(),
            });
            popup.Show();
        }
```
*(The previous `ContextMenu` opened on WPF's built-in right-click; `MouseRightButtonUp` is the explicit replacement. `_locked` toggles directly since the popup's Lock is a fire action, not a checkbox.)*

Also update the stale ContextMenu-era **comments** the code deletion doesn't reach (they now describe a removed surface): the class doc-comment "right-click menu" → "right-click popup" (`:14`), and the "menu" wording in the comments at `:137` (the header's "drag/menu hit target" → "drag/popup hit target"), `:279`, `:284`, and `:521` → "popup". Grep `menu` in the file after Step 5 and confirm only intentional text remains.

- [ ] **Step 3: Cleared-primary header render** — in `Render`, the metric-label line (`:300` region):
```csharp
            _metricText.Text = (frame.Title.Length > 0 ? " — " : "") + frame.MetricLabel;
```
becomes (no dangling "— " when the metric is cleared/blank):
```csharp
            _metricText.Text = frame.MetricLabel.Length > 0 ? (frame.Title.Length > 0 ? " — " : "") + frame.MetricLabel : "";
```
(`RenderSlots` already renders zero rows for an empty frame, so the backdrop shows — no further change.)

- [ ] **Step 4: Seed DPS on New** — `OverlayHost.AddNewWindow`'s `created` initializer, add `MetricKey`:
```csharp
                MetricKey = Meter.MetricRegistry.DefaultKey,   // seed so a New meter shows DPS, and null stays "user-cleared"
                RowHeight = source.RowHeight,
```

- [ ] **Step 5: Verify no dangling menu references** — `grep -n "BuildMenu\|StyleMenu\|SyncMenuChecks\|_menu\b\|_lockItem\|ContextMenu" src/eq2auras.Plugin/Overlay/MeterWindow.cs` → no matches.

- [ ] **Step 6: Commit**
```bash
git add src/eq2auras.Plugin/Overlay/MeterWindow.cs src/eq2auras.Plugin/Overlay/OverlayHost.cs
git commit -m "Meter window: right-click opens MeterPopup (drop ContextMenu); raw metric key (cleared=nothing); seed DPS on New (increment 4)"
```

---

### Task 5: Plugin — remove the secondary `ComboBox` from the settings window (transcribe)

The secondary now lives in the popup, so the settings window drops its `Secondary` row and the two ctor params (`secondaryKey`, `onSecondaryChanged`); `MeterWindow.OpenSettings` stops passing them.

**Files:** Modify `MeterSettingsWindow.cs`, `MeterWindow.cs` (the `OpenSettings` call).

- [ ] **Step 1: Remove the ctor params + field** — drop `string secondaryKey, Action<string> onSecondaryChanged` from the ctor signature, `_onSecondaryChanged` field, and its assignment.
- [ ] **Step 2: Delete the secondary row** — remove `secondaryLabel`, `secondary` (`ComboBox`), `secondaryRow`, and `body.Children.Add(secondaryRow);`. Remove the now-unused `using`/reference to `Eq2Auras.Core.Meter.MetricRegistry` if nothing else uses it.
- [ ] **Step 3: Update the caller** — `MeterWindow.OpenSettings`'s `new MeterSettingsWindow(...)` drops the trailing `_secondaryKey, SetSecondary` args.
- [ ] **Step 4: Verify** — `grep -n "secondary\|Secondary\|ComboBox" src/eq2auras.Plugin/Overlay/MeterSettingsWindow.cs` → no matches. `SetSecondary` on `MeterWindow` stays (it is the popup's `SecondaryPicked` target now via the callback, and `OverlayHost` still wires `SecondaryPicked`).
- [ ] **Step 5: Commit**
```bash
git add src/eq2auras.Plugin/Overlay/MeterSettingsWindow.cs src/eq2auras.Plugin/Overlay/MeterWindow.cs
git commit -m "Meter settings window: remove secondary ComboBox (relocated to the popup) (increment 4)"
```

---

## Testing strategy

**Core (Mac, gating):** `dotnet test …Core.Tests…` — the new `ResolvePrimary`, cleared-primary empty-frame, and DPS-seed tests plus the full suite green.

**Plugin (CI):** push; the WPF plugin must compile with `MeterPopup`, `MetricGridItem`, the `Theme.ItemSelected` token, the `MeterWindow` popup wiring (no `ContextMenu` left), and the trimmed settings-window ctor.

**On-box field script (merge-gate — the popup is the marquee surface):**
1. Right-click the meter header → the **dark popup** opens at the cursor: Primary grid (family columns: Damage red / Healing green / Utility blue) over Secondary grid, then Lock · New meter · **Remove meter** (red) and a corner ✕.
2. **Toggle primary:** click a metric → it lights (amber dot + bright); click it again → clears → the meter shows **only the backdrop** (no rows, blank metric/total in the header). Click another → switches.
3. **Toggle secondary:** independent; clearing removes the secondary column; same-as-primary allowed.
4. **Lifecycle:** Lock toggles freeze; New meter spawns another; Remove meter deletes (blocked at the last window); corner ✕ and click-away both dismiss without side effects.
5. **Settings window:** ⚙ → **no** Secondary dropdown remains (it moved to the popup).
6. **Persistence:** primary/secondary/cleared survive reload; a New meter opens showing DPS.
7. Timers unregressed.

## Self-review

**Spec coverage (increment 4):** §Configuration's right-click popup (family-column metric grids, primary over secondary, toggle/no-None, lifecycle cluster + corner ✕, "meter" noun, Remove-blocked-at-last), the cleared-primary → nothing behavior (§Configuration + §Meter display defaults), and the secondary's relocation out of the settings window → Tasks 1-5. Plan-watch #1 (null-vs-unknown split + cleared render), #2 (seed DPS on new/migrated — the New path here; the migrated path was seeded in Task 1 Step 9), and #5 (popup replaces the ContextMenu *and* the secondary ComboBox) all land. #6's `ItemSelected` token lands (Task 2).

**Placeholder scan:** none — `MeterPopup`'s single-selection is complete via `RefreshSection` over the captured per-section item lists; every step is full code.

**Type consistency:** `MeterPopup.Callbacks` fields are `Action<string>` (toggles, nullable key) / `Action` (lifecycle), matching `MeterWindowCallbacks.MetricPicked`/`SecondaryPicked` (`Action<string>`) and `LockChanged`(bool)/`NewWindow`/`CloseWindow`. `ResolvePrimary` returns `MetricDef` (nullable); `MeterEngine` null-checks it. `MetricGridItem.Toggled` is `Action`; `.Selected` is `bool`. `Theme.ItemSelected` is `SolidColorBrush`.

## Plan-watch items

Lands #1, #2 (both seed paths — migrated in Task 1, New in Task 4), #5, and #6's `ItemSelected`. Only increment 5 (header cog/total) remains after this.
