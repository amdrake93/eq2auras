# Parse Meter Slice 2 — Increment 1 (Multi-window) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. **This repo's convention is INLINE execution (`superpowers:executing-plans`), watched by the owner — not subagent-driven.**

**Goal:** Turn the single-window Parse Meter into a multi-window meter — each window over one shared pipeline with its own metric/position/lock — so a DPS window and an HPS window can run side by side.

**Architecture:** Reshape `MeterSettings` from a single window into a per-window `MeterWindowConfig` list (migrating the slice-1 flat file), the way `PanelSettings` already gives each timer group its own knobs. `OverlayHost` holds a live map of windows and, each poll, fans the one shared ACT snapshot to every window's metric through one shared `MeterEngine`/palette (so an ally reads the same color everywhere). New/Close window live on each window's right-click menu; the tab keeps only the enable checkbox.

**Tech Stack:** C# — Core `netstandard2.0` (Mac-testable, xUnit), Plugin `net472`/WPF (not Mac-buildable; verified by CI compile + live script). DCJS for settings persistence.

## Global Constraints

_(from `docs/SPEC.md` Parts I/III, IV §Development & test cycle, and CLAUDE.md — every task's requirements implicitly include these)_

- **Single-assembly packaging:** Core sources are `<Compile Include="..\eq2auras.Core\**\*.cs">`d into the plugin — both csprojs glob, so new Core files auto-include (no packaging step). Never reference a second DLL.
- **No `async` in the plugin project;** no non-GAC types in fields (ACT's pre-`InitPlugin` type scan).
- **Never reference `System.Web.Extensions`.** JSON = `DataContractJsonSerializer` (DCJS).
- **DCJS skips field initializers on deserialize** → a field missing from an old `settings.json` comes back as its 0-value, which must mean "the default": enum/bool defaults are the 0-value; **nullable positions/dimensions — `null`, never `0`, means "unset, use default"** (0 is a real screen edge).
- **Core is `netstandard2.0`, Mac-testable via** `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`. **Never build the Plugin/solution on the Mac** (net472+WPF).
- **Branch `meter-slice2` is the slice integration branch** (owner decision, 2026-07-17): all five increments build on it; **do not merge to `main`**. Each increment's checkpoint is verify-only branch CI (Core tests + WPF compile + artifact, no publish) + an owner branch-artifact field-test. `main` gets the merge once (spec + full impl) at slice completion.
- The spec review (2 rounds, closed at commit d6cc907) emitted **no plan-watch items** — none to land here.

## File Structure

| File | Create/Modify | Responsibility |
|---|---|---|
| `src/eq2auras.Core/Config/MeterWindowConfig.cs` | **Create** | One meter window's persisted config (metric/position/lock; DCJS-nullable positions). |
| `src/eq2auras.Core/Config/MeterSettings.cs` | Modify | `{ Enabled; Windows[] }` + `Normalize()` (legacy migration + drop-nulls + seed-one-when-enabled). Legacy flat fields retained only to migrate, then cleared. |
| `src/eq2auras.Core/Config/Settings.cs` | Modify (`Normalize`, ~87) | Call `Meter.Normalize()`. |
| `tests/eq2auras.Core.Tests/MeterSettingsTests.cs` | Modify (rewrite) | Migration, seed, drop-nulls, multi-window roundtrip. |
| `src/eq2auras.Plugin/Overlay/MeterWindow.cs` | Modify | New/Close menu items (+ `canClose` on open), lock-item refactor, quick dark menu styling. |
| `src/eq2auras.Plugin/Overlay/OverlayHost.cs` | Modify | `List`→map of windows; own `MeterEngine`; create-from-configs / clone / close / enable-disable; per-poll fan-out. |
| `src/eq2auras.Plugin/Eq2AurasPlugin.cs` | Modify | Move `MeterEngine` ownership to the host; rewire the probe callback to `UpdateMeterSample`. |

---

## Task 1: Core — per-window settings model + migration (TDD, Mac)

**Files:**
- Create: `src/eq2auras.Core/Config/MeterWindowConfig.cs`
- Modify: `src/eq2auras.Core/Config/MeterSettings.cs`
- Modify: `src/eq2auras.Core/Config/Settings.cs` (`Normalize`, ~line 87)
- Test: `tests/eq2auras.Core.Tests/MeterSettingsTests.cs`

**Interfaces:**
- Produces: `MeterWindowConfig { string MetricKey; double? Left; double? Top; bool Locked }`; `MeterSettings { bool Enabled; List<MeterWindowConfig> Windows; void Normalize() }`. The plugin (Tasks 2–3) consumes `_settings.Meter.Windows` (the config list) and each `MeterWindowConfig`'s fields.

- [ ] **Step 1: Rewrite the settings tests for the new contract**

Replace the entire contents of `tests/eq2auras.Core.Tests/MeterSettingsTests.cs`:

```csharp
using System.Collections.Generic;
using Eq2Auras.Core.Config;
using Xunit;

public class MeterSettingsTests
{
    [Theory]
    [InlineData("")]                        // empty file
    [InlineData("{}")]                      // old file with no meter section
    [InlineData("{\"meter\":null}")]        // explicit null section
    public void Missing_meter_section_yields_empty_disabled(string json)
    {
        var parsed = Settings.Parse(json);

        Assert.NotNull(parsed.Meter);
        Assert.False(parsed.Meter.Enabled);      // default OFF — opt-in (SPEC Part III §Settings)
        Assert.NotNull(parsed.Meter.Windows);
        Assert.Empty(parsed.Meter.Windows);      // no window exists until the meter is enabled
    }

    [Fact]
    public void Legacy_single_window_file_migrates_into_one_config()
    {
        // Slice-1 shape: the single window's config sat in flat meter fields, no "windows" key.
        var json = "{\"meter\":{\"enabled\":true,\"metricKey\":\"enchps\",\"left\":100.0,\"top\":200.5,\"locked\":true}}";

        var parsed = Settings.Parse(json);

        Assert.True(parsed.Meter.Enabled);
        var window = Assert.Single(parsed.Meter.Windows);
        Assert.Equal("enchps", window.MetricKey);
        Assert.Equal(100.0, window.Left);
        Assert.Equal(200.5, window.Top);
        Assert.True(window.Locked);
    }

    [Fact]
    public void Enabled_with_no_windows_seeds_one_default()
    {
        // An enabled meter always has at least one window (SPEC Part III §Multiple windows).
        var json = "{\"meter\":{\"enabled\":true,\"windows\":[]}}";

        var parsed = Settings.Parse(json);

        var window = Assert.Single(parsed.Meter.Windows);
        Assert.Null(window.MetricKey);   // null -> DPS at resolve time
        Assert.Null(window.Left);        // null -> host default placement
        Assert.Null(window.Top);
        Assert.False(window.Locked);
    }

    [Fact]
    public void Disabled_with_no_windows_stays_empty()
    {
        var json = "{\"meter\":{\"enabled\":false,\"windows\":[]}}";

        var parsed = Settings.Parse(json);

        Assert.Empty(parsed.Meter.Windows);   // nothing to show while hidden
    }

    [Fact]
    public void Null_window_entries_are_dropped()
    {
        var json = "{\"meter\":{\"enabled\":true,\"windows\":[null,{\"metricKey\":\"encdps\"}]}}";

        var parsed = Settings.Parse(json);

        var window = Assert.Single(parsed.Meter.Windows);
        Assert.Equal("encdps", window.MetricKey);
    }

    [Fact]
    public void Multi_window_roundtrip_preserves_each_config()
    {
        var settings = new Settings();
        settings.Meter.Enabled = true;
        settings.Meter.Windows = new List<MeterWindowConfig>
        {
            new MeterWindowConfig { MetricKey = "encdps", Left = 0, Top = 300, Locked = false },   // 0 is a REAL position
            new MeterWindowConfig { MetricKey = "enchps", Left = 640.5, Top = 300, Locked = true },
        };

        var parsed = Settings.Parse(settings.ToJson());

        Assert.Equal(2, parsed.Meter.Windows.Count);
        Assert.Equal("encdps", parsed.Meter.Windows[0].MetricKey);
        Assert.Equal(0.0, parsed.Meter.Windows[0].Left);
        Assert.False(parsed.Meter.Windows[0].Locked);
        Assert.Equal("enchps", parsed.Meter.Windows[1].MetricKey);
        Assert.Equal(640.5, parsed.Meter.Windows[1].Left);
        Assert.True(parsed.Meter.Windows[1].Locked);
    }
}
```

- [ ] **Step 2: Run the tests — verify they FAIL (compile error)**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
Expected: **build failure** — `MeterWindowConfig` does not exist, `MeterSettings` has no `Windows`.

- [ ] **Step 3: Create `MeterWindowConfig`**

Create `src/eq2auras.Core/Config/MeterWindowConfig.cs`:

```csharp
using System.Runtime.Serialization;

namespace Eq2Auras.Core.Config
{
    /// One meter window's persisted config (SPEC Part III §Multiple windows, §Settings).
    /// Positions nullable on purpose — DCJS materializes a missing numeric as 0, a real
    /// screen corner, so null (never zero) means "unset, use the default placement",
    /// same convention as PanelSettings. Increment 1 carries metric/position/lock; the
    /// appearance knobs (row height, font, opacity) and explicit size (width, visible
    /// rows) arrive with their own increments.
    [DataContract]
    public sealed class MeterWindowConfig
    {
        [DataMember(Name = "metricKey")]
        public string MetricKey { get; set; }   // null/unknown -> registry default at resolve time

        [DataMember(Name = "left")]
        public double? Left { get; set; }

        [DataMember(Name = "top")]
        public double? Top { get; set; }

        [DataMember(Name = "locked")]
        public bool Locked { get; set; }
    }
}
```

- [ ] **Step 4: Reshape `MeterSettings`**

Replace the entire contents of `src/eq2auras.Core/Config/MeterSettings.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace Eq2Auras.Core.Config
{
    /// Parse Meter module settings (SPEC Part III §Settings). Enabled defaults false
    /// (0-value rule): the meter is opt-in. Enabled is a show/hide toggle over the
    /// persisted Windows list, which persists independently — disabling keeps the
    /// configs, enabling restores exactly them.
    [DataContract]
    public sealed class MeterSettings
    {
        [DataMember(Name = "enabled")]
        public bool Enabled { get; set; }

        // Legacy slice-1 single-window fields. Retained only to migrate a pre-multi-window
        // settings.json into Windows[0] (same shape as Settings' flat->panels migration);
        // Normalize consumes then clears them, so new files carry them null/false.
        [DataMember(Name = "left")]
        public double? Left { get; set; }
        [DataMember(Name = "top")]
        public double? Top { get; set; }
        [DataMember(Name = "metricKey")]
        public string MetricKey { get; set; }
        [DataMember(Name = "locked")]
        public bool Locked { get; set; }

        [DataMember(Name = "windows")]
        public List<MeterWindowConfig> Windows { get; set; }

        /// DCJS skips initializers, so Windows may be null. Migrates a legacy single-window
        /// file into one config, drops null entries, and seeds one default window when the
        /// meter is enabled but has none (SPEC Part III §Multiple windows — an enabled meter
        /// always has at least one window; the first-ever enable seeds a default).
        public void Normalize()
        {
            if (Windows == null)
            {
                Windows = new List<MeterWindowConfig>();
                if (Left.HasValue || Top.HasValue || MetricKey != null || Locked)
                {
                    Windows.Add(new MeterWindowConfig
                    {
                        Left = Left,
                        Top = Top,
                        MetricKey = MetricKey,
                        Locked = Locked,
                    });
                }
            }

            // Legacy flat fields are consumed into Windows; keep new files clean.
            Left = null;
            Top = null;
            MetricKey = null;
            Locked = false;

            Windows = Windows.Where(w => w != null).ToList();

            if (Enabled && Windows.Count == 0) Windows.Add(new MeterWindowConfig());
        }
    }
}
```

- [ ] **Step 5: Wire `Settings.Normalize()` to normalize the meter**

In `src/eq2auras.Core/Config/Settings.cs`, in `Normalize()`, replace the single line:

```csharp
            if (Meter == null) Meter = new MeterSettings();   // DCJS skips initializers
```

with:

```csharp
            if (Meter == null) Meter = new MeterSettings();   // DCJS skips initializers
            Meter.Normalize();
```

- [ ] **Step 6: Run the tests — verify they PASS**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
Expected: **PASS**, all green (the 6 MeterSettings tests plus the existing suite). No pre-existing test regresses (only `MeterSettingsTests` referenced the old flat contract).

- [ ] **Step 7: Commit**

```bash
git add src/eq2auras.Core/Config/MeterWindowConfig.cs src/eq2auras.Core/Config/MeterSettings.cs src/eq2auras.Core/Config/Settings.cs tests/eq2auras.Core.Tests/MeterSettingsTests.cs
git commit -m "Meter slice2 inc1: per-window MeterWindowConfig list + legacy migration (Core)"
```

---

## Task 2: MeterWindow — New/Close menu + quick dark styling (Plugin, CI-verified)

**Files:**
- Modify: `src/eq2auras.Plugin/Overlay/MeterWindow.cs`

**Interfaces:**
- Consumes: nothing new from Task 1 at compile time.
- Produces: `MeterWindow` constructor gains three trailing parameters — `Action onNewWindow, Action onCloseWindow, Func<bool> canClose` — that Task 3 supplies.

> Plugin file: not Mac-buildable. Transcribe exactly; the compile gate is the Task 4 CI checkpoint.

- [ ] **Step 1: Add the callback fields + lock-item reference**

In `MeterWindow.cs`, in the field block (after `private readonly Action<bool> _onLockChanged;`), add:

```csharp
        private readonly Action _onNewWindow;
        private readonly Action _onCloseWindow;
        private readonly Func<bool> _canClose;
        private MenuItem _lockItem;
```

- [ ] **Step 2: Extend the constructor signature + assignments**

Change the constructor signature from:

```csharp
        public MeterWindow(double left, double top, VisualStyle style, string metricKey, bool locked,
            Action<double, double> persistPosition, Action<string> onMetricPicked, Action<bool> onLockChanged)
            : base(left, top, GrowDirection.Down, persistPosition, clickThroughBaseline: false)
```

to:

```csharp
        public MeterWindow(double left, double top, VisualStyle style, string metricKey, bool locked,
            Action<double, double> persistPosition, Action<string> onMetricPicked, Action<bool> onLockChanged,
            Action onNewWindow, Action onCloseWindow, Func<bool> canClose)
            : base(left, top, GrowDirection.Down, persistPosition, clickThroughBaseline: false)
```

And in the constructor body, after `_onLockChanged = onLockChanged;`, add:

```csharp
            _onNewWindow = onNewWindow;
            _onCloseWindow = onCloseWindow;
            _canClose = canClose;
```

- [ ] **Step 3: Rebuild the menu with Lock + New/Close + styling**

Replace the whole `BuildMenu()` method:

```csharp
        private ContextMenu BuildMenu()
        {
            var menu = new ContextMenu();
            foreach (var metric in MetricRegistry.All)
            {
                var item = new MenuItem { Header = metric.Label, Tag = metric.Key, IsCheckable = true };
                item.Click += (s, e) =>
                {
                    var key = (string)((MenuItem)s).Tag;
                    _metricKey = key;
                    SyncMenuChecks();
                    _onMetricPicked(key);
                };
                menu.Items.Add(item);
            }

            menu.Items.Add(new Separator());
            _lockItem = new MenuItem { Header = "Lock window", IsCheckable = true };
            _lockItem.Click += (s, e) =>
            {
                _locked = _lockItem.IsChecked;
                _onLockChanged(_locked);
            };
            menu.Items.Add(_lockItem);

            menu.Items.Add(new Separator());
            var newItem = new MenuItem { Header = "New meter window" };
            newItem.Click += (s, e) => _onNewWindow();
            menu.Items.Add(newItem);
            var closeItem = new MenuItem { Header = "Close this window" };
            closeItem.Click += (s, e) => _onCloseWindow();
            menu.Items.Add(closeItem);

            // The last window can't close (SPEC Part III §Multiple windows) — the tab
            // toggle is the master off-switch. Evaluated on open so it tracks the live count.
            menu.Opened += (s, e) => closeItem.IsEnabled = _canClose();

            StyleMenu(menu);
            return menu;   // no sync here — _menu is still null until the ctor assigns it
        }

        /// Quick, iterate-able dark pass over the raw WPF ContextMenu (SPEC Part III
        /// §Configuration — "no raw ACT chrome"). Fuller MenuItem re-templating (hover
        /// highlight) is the deferred styling item in the backlog.
        private static void StyleMenu(ContextMenu menu)
        {
            menu.Background = new SolidColorBrush(Color.FromArgb(250, 24, 27, 34));
            menu.BorderBrush = new SolidColorBrush(OverlayTheme.CalmBorder);
            menu.Foreground = new SolidColorBrush(OverlayTheme.Text);

            var itemStyle = new Style(typeof(MenuItem));
            itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(OverlayTheme.Text)));
            itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
            menu.ItemContainerStyle = itemStyle;
        }
```

- [ ] **Step 4: Fix `SyncMenuChecks` to use the lock-item reference**

The old `SyncMenuChecks` found the lock item by `Tag == null`, which now also matches New/Close. Replace the whole method:

```csharp
        private void SyncMenuChecks()
        {
            foreach (var entry in _menu.Items)
            {
                if (entry is MenuItem item && item.Tag is string key) item.IsChecked = key == _metricKey;
            }
            _lockItem.IsChecked = _locked;
        }
```

- [ ] **Step 5: Commit**

```bash
git add src/eq2auras.Plugin/Overlay/MeterWindow.cs
git commit -m "Meter slice2 inc1: New/Close window menu items + quick dark menu styling"
```

---

## Task 3: OverlayHost + plugin wiring — multi-window management & per-poll fan-out (Plugin, CI-verified)

**Files:**
- Modify: `src/eq2auras.Plugin/Overlay/OverlayHost.cs`
- Modify: `src/eq2auras.Plugin/Eq2AurasPlugin.cs`

**Interfaces:**
- Consumes: `MeterWindow(..., Action onNewWindow, Action onCloseWindow, Func<bool> canClose)` (Task 2); `MeterSettings.Windows` / `MeterWindowConfig` (Task 1).
- Produces: `OverlayHost.UpdateMeterSample(EncounterReading, List<CombatantReading>, IReadOnlyList<int>)`; `SetMeterEnabled(bool)` unchanged in signature.

- [ ] **Step 1: Replace the meter field with the engine + window map**

In `OverlayHost.cs`, replace the field:

```csharp
        private MeterWindow _meterWindow;
```

with:

```csharp
        private readonly MeterEngine _meterEngine = new MeterEngine();
        private readonly Dictionary<MeterWindowConfig, MeterWindow> _meterWindows =
            new Dictionary<MeterWindowConfig, MeterWindow>();
```

- [ ] **Step 2: Create windows from the config list in `Start()`**

In `Start()`, replace:

```csharp
                if (_settings.Meter.Enabled) CreateMeterWindow();
```

with:

```csharp
                if (_settings.Meter.Enabled) CreateMeterWindows();
```

- [ ] **Step 3: Replace `CreateMeterWindow()` with the multi-window methods**

Replace the entire `CreateMeterWindow()` method with:

```csharp
        private void CreateMeterWindows()
        {
            foreach (var config in _settings.Meter.Windows) AddMeterWindow(config);
        }

        private void AddMeterWindow(MeterWindowConfig config)
        {
            var style = MeterStyle();
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
            _meterWindows[config] = window;
            window.Show();
        }

        /// New meter window: clone the invoked window's config, offset + clamped on-screen
        /// (SPEC Part III §Multiple windows). Re-pointed at another metric from its own menu.
        private void AddClonedWindow(MeterWindowConfig source)
        {
            var style = MeterStyle();
            double baseLeft = source.Left ?? DefaultMeterLeft(style);
            double baseTop = source.Top ?? DefaultMeterTop;
            var clone = new MeterWindowConfig
            {
                MetricKey = source.MetricKey,
                Locked = source.Locked,
                Left = ClampMeterX(baseLeft + MeterCascadeOffset, style),
                Top = ClampMeterY(baseTop + MeterCascadeOffset),
            };
            SettingsStore.Update(_settings, () => _settings.Meter.Windows.Add(clone));
            AddMeterWindow(clone);
        }

        /// The last window can't close — the tab toggle is the master off-switch (SPEC Part III).
        private void CloseMeterWindow(MeterWindowConfig config)
        {
            if (_meterWindows.Count <= 1) return;
            if (_meterWindows.TryGetValue(config, out var window))
            {
                window.Close();
                _meterWindows.Remove(config);
            }
            SettingsStore.Update(_settings, () => _settings.Meter.Windows.Remove(config));
        }

        // Meter rows touch (SPEC Part III §Meter display defaults); per-window size/font/
        // opacity knobs arrive in later increments, so increment 1 uses baked defaults.
        private static VisualStyle MeterStyle() => new VisualStyle { RowSpacing = 0 };

        private const double MeterCascadeOffset = 30;
        private const double MeterWindowSlack = 10;   // matches MeterWindow's window slack
        private const double DefaultMeterTop = 320;

        private static double DefaultMeterLeft(VisualStyle style)
            => SystemParameters.PrimaryScreenWidth - style.RowWidth - 60;

        private static double ClampMeterX(double x, VisualStyle style)
            => Math.Max(0, Math.Min(x, SystemParameters.PrimaryScreenWidth - (style.RowWidth + MeterWindowSlack)));

        private static double ClampMeterY(double y)
            => Math.Max(0, Math.Min(y, SystemParameters.PrimaryScreenHeight - 100));
```

- [ ] **Step 4: Enable/disable creates or closes all windows**

Replace the body of `SetMeterEnabled(bool enabled)` (keep the doc comment above it) with:

```csharp
        public void SetMeterEnabled(bool enabled)
        {
            var dispatcher = _dispatcher;
            if (dispatcher == null) return;
            dispatcher.BeginInvoke((Action)(() =>
            {
                // The tab's SettingsStore.Update(enabled = true) has already run Normalize,
                // which seeds one default window into Meter.Windows if the list was empty.
                if (enabled && _meterWindows.Count == 0) CreateMeterWindows();
                else if (!enabled && _meterWindows.Count > 0)
                {
                    foreach (var window in _meterWindows.Values) window.Close();
                    _meterWindows.Clear();   // configs persist in Meter.Windows for the next enable
                }
            }));
        }
```

- [ ] **Step 5: Replace `UpdateMeterFrame` with the per-poll fan-out**

Replace the entire `UpdateMeterFrame(MeterFrame frame)` method with:

```csharp
        /// Callable from any thread (the sample runs on ACT's UI thread). Fans the one
        /// shared snapshot to each window's metric through the one shared engine/palette —
        /// an ally reads the same color in every window (SPEC Part III §Multiple windows).
        public void UpdateMeterSample(EncounterReading encounter, List<CombatantReading> combatants,
            IReadOnlyList<int> paletteArgb)
        {
            var dispatcher = _dispatcher;
            if (dispatcher == null) return;
            dispatcher.BeginInvoke((Action)(() =>
            {
                foreach (var pair in _meterWindows)
                {
                    var frame = _meterEngine.Tick(encounter, combatants, pair.Key.MetricKey, paletteArgb);
                    pair.Value.Render(frame);
                }
            }));
        }
```

- [ ] **Step 6: Close all meter windows in `Dispose()`**

In `Dispose()`, replace:

```csharp
                _meterWindow?.Close();
                _meterWindow = null;
```

with:

```csharp
                foreach (var window in _meterWindows.Values) window.Close();
                _meterWindows.Clear();
```

- [ ] **Step 7: Move `MeterEngine` ownership out of the plugin into the fan-out**

In `Eq2AurasPlugin.cs`:

Remove the field (line ~26): `private MeterEngine _meterEngine;`

Replace the probe wiring:

```csharp
            _meterEngine = new MeterEngine();
            _encounterProbe = new EncounterProbe(
                () => _settings.Meter.Enabled,
                (encounter, combatants) => _overlay.UpdateMeterFrame(
                    _meterEngine.Tick(encounter, combatants, _settings.Meter.MetricKey, _settings.PaletteArgb)));
```

with:

```csharp
            _encounterProbe = new EncounterProbe(
                () => _settings.Meter.Enabled,
                (encounter, combatants) => _overlay.UpdateMeterSample(encounter, combatants, _settings.PaletteArgb));
```

In `DeInitPlugin()`, remove the line: `_meterEngine = null;` (keep `_encounterProbe = null;`).

_(Leave `using Eq2Auras.Core.Meter;` — the probe delegate's DTO types resolve through it; an unused-using is at most a warning, never a build break.)_

- [ ] **Step 8: Commit**

```bash
git add src/eq2auras.Plugin/Overlay/OverlayHost.cs src/eq2auras.Plugin/Eq2AurasPlugin.cs
git commit -m "Meter slice2 inc1: OverlayHost multi-window management + per-poll fan-out through one shared engine"
```

---

## Task 4: CI checkpoint + field-test handoff

**Files:** none (verification only).

- [ ] **Step 1: Push the branch**

```bash
git push -u origin meter-slice2
```

- [ ] **Step 2: Watch verify-only CI to green**

```bash
gh run list --branch meter-slice2 --limit 1
gh run watch <run-id> --exit-status
```
Expected: **success** — Core tests pass and the WPF plugin compiles + produces the build artifact (no publish; branch push never touches `dev-latest`). A red compile here is the first real check of Tasks 2–3 (no Mac compile exists). If red, fix and re-push before handing off.

- [ ] **Step 3: Hand the increment-1 field-test script to the owner** (branch artifact — download the `meter-slice2` CI artifact DLL and drop it into ACT's `Plugins` folder)

```
1. Tab → tick "Parse Meter" → ONE window appears (DPS, screen-right).
2. Solo or group combat → live DPS rows within a poll interval.
3. Right-click the window → "New meter window" → a SECOND window appears, offset from the first.
4. Right-click the second → metric → HPS → it shows HPS live; the first still shows DPS. (DPS + HPS side by side.)
5. A given ally shows the SAME bar color in both windows.
6. Drag each window somewhere; reload the plugin (untick/retick ACT's plugin checkbox) → both windows return at their dragged positions, with their metrics and lock states.
7. Right-click a window → "Close this window" → it closes; the other remains. With one window left, "Close this window" is greyed/disabled.
8. Untick "Parse Meter" → all windows vanish. Re-tick → the same windows return (positions + metrics preserved).
9. Timer sanity (light): timer overlay still draws, drains, sparks, and colors normally — no regression.
```

- [ ] **Step 4: Do NOT merge.** Per the integration-branch model, `meter-slice2` stays unmerged; on the owner's word, proceed to Increment 2's plan (settings window + opacity).

---

## Self-Review

**1. Spec coverage** (SPEC Part III §Multiple windows, §Configuration [New/Close only], §Settings, §Assembly split, §Testing strategy slice 2):
- Per-window `MeterWindowConfig` list + migration → Task 1. ✓
- Enabled = show/hide over persisted list; first-enable seeds one default → Task 1 (`Normalize`) + Task 3 (`SetMeterEnabled`). ✓
- New (clone + offset + clamp) / Close (blocked at last) on the right-click menu → Task 2 (menu) + Task 3 (host logic). ✓
- N windows over one pipeline; one shared engine/palette; per-window metric fan-out → Task 3 (`UpdateMeterSample`). ✓
- Quick dark menu styling → Task 2 (`StyleMenu`). ✓
- Core TDD (config shape, migration, seed, drop-nulls, roundtrip) → Task 1 tests. ✓
- Live merge-gate items (side-by-side, persistence, close/enable rules, timer sanity) → Task 4 script. ✓
- **Deliberately deferred to later increments (not this plan):** the ⚙ cog + settings window, row-height/font/opacity knobs, edge-resize, per-window visible-row count. Increment-1 windows use baked size/look. ✓ (matches the backlog increment ordering)

**2. Placeholder scan:** No TBD/TODO; every code step shows complete code; commands have expected output. ✓

**3. Type consistency:** `MeterWindowConfig { MetricKey, Left, Top, Locked }` used identically in Task 1 (defn/tests), Task 3 (`config.MetricKey/.Left/.Top/.Locked`); `MeterWindow` 11-arg ctor defined in Task 2 matches the call in Task 3; `UpdateMeterSample(EncounterReading, List<CombatantReading>, IReadOnlyList<int>)` defined in Task 3 Step 5 matches the call in Task 3 Step 7. ✓
