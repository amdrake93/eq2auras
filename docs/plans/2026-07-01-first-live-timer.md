# First Live Timer (calm list slice) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the pulsing test box with a real overlay: every active ACT spell timer rendered as a colored, draining bar in a sorted (soonest-first) list over the game — delivered to the Windows box via the proven one-button self-update.

**Architecture:** First restructure to **single-assembly packaging** (plugin `<Compile Include>`s Core sources; resolver deleted; CI ships one DLL) per the amended spec — this removes the scan-safety landmine before feature code adds Core-typed fields. Then a pure, Mac-TDD'd **`TimerListBuilder`** in Core turns per-instance readings into sorted display rows (urgency derived from each timer's own `WarningValue`, with the spec's fraction-of-total → absolute-10s fallbacks), the existing poll loop feeds it (now iterating **all** `SpellTimers` per frame — the multi-instance finding), and a WPF **`TimerListWindow`** renders rows code-behind at the poll rate.

**Tech Stack:** C# / netstandard2.0 Core + net472 WPF plugin (SDK-style), xUnit on net10.0, GitHub Actions msbuild, ACT `FormSpellTimers` API.

**Slice discipline:** list only. No center escalation zone, no radial pie, no LATE minimum-display floor, no animations — that's the next slice. Urgency is *computed* now (cheap, testable, needed for row tinting) but escalation *behavior* is not built.

---

## File Structure

```
src/eq2auras.Core/
├── CoreBuildInfo.cs                    # marker bumped E (update tracer)
└── Timers/
    ├── TimerReading.cs                 # raw per-INSTANCE reading handed in by the adapter
    ├── TimerRow.cs                     # display row + TimerUrgency enum
    └── TimerListBuilder.cs             # readings -> sorted rows (the TDD heart)
src/eq2auras.Plugin/
├── eq2auras.Plugin.csproj              # shared-source Core, ProjectReference removed
├── Eq2AurasPlugin.cs                   # wiring: probe -> builder -> overlay
├── PluginAssemblyResolver.cs           # DELETED
├── Act/TimerProbe.cs                   # poll now emits List<TimerReading> (all SpellTimers)
└── Overlay/
    ├── OverlayHost.cs                  # hosts TimerListWindow; thread-safe UpdateRows
    ├── TimerListWindow.xaml (+.cs)     # transparent click-through list renderer
    └── TestWindow.xaml (+.cs)          # DELETED
.github/workflows/build.yml             # stage only eq2auras.dll
```

---

## Task 1: Single-assembly restructure

**Files:**
- Modify: `src/eq2auras.Plugin/eq2auras.Plugin.csproj`
- Delete: `src/eq2auras.Plugin/PluginAssemblyResolver.cs`
- Modify: `src/eq2auras.Plugin/Eq2AurasPlugin.cs` (remove resolver calls)
- Modify: `src/eq2auras.Core/CoreBuildInfo.cs` (marker `E`)
- Modify: `.github/workflows/build.yml`

- [ ] **Step 1: Replace the ProjectReference with shared-source compilation**

In `src/eq2auras.Plugin/eq2auras.Plugin.csproj`, replace:

```xml
  <ItemGroup>
    <ProjectReference Include="..\eq2auras.Core\eq2auras.Core.csproj" />
  </ItemGroup>
```

with:

```xml
  <ItemGroup>
    <!-- Shared-source Core (see SPEC Packaging): compiled INTO this assembly so ACT's
         pre-InitPlugin type scan can always resolve every field type. Never reference
         Core.dll and never add non-GAC assemblies whose types end up in fields. -->
    <Compile Include="..\eq2auras.Core\**\*.cs"
             Exclude="..\eq2auras.Core\obj\**;..\eq2auras.Core\bin\**"
             LinkBase="CoreShared" />
  </ItemGroup>
```

- [ ] **Step 2: Delete the resolver and its lifecycle calls**

Delete `src/eq2auras.Plugin/PluginAssemblyResolver.cs`. In `Eq2AurasPlugin.cs`, remove the `PluginAssemblyResolver.EnsureRegistered();` line (and its comment) from `InitPlugin`, and the `PluginAssemblyResolver.Unregister();` line (and comment) from `DeInitPlugin`.

- [ ] **Step 3: Bump the update tracer**

In `src/eq2auras.Core/CoreBuildInfo.cs`: `public static string Marker => "E";`

- [ ] **Step 4: CI stages one DLL**

In `.github/workflows/build.yml`, replace the "Stage plugin artifacts" run block's copy lines with:

```yaml
          mkdir dist
          cp "src/eq2auras.Plugin/bin/Release/net472/eq2auras.dll" dist/
```

- [ ] **Step 5: Verify the Mac test loop is untouched**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
Expected: PASS (5 tests) — the Core *project* still exists for tests; only the plugin stopped referencing its binary.

- [ ] **Step 6: Commit, push, verify CI + release**

```bash
git add -A && git commit -m "Single-assembly packaging: compile Core sources into plugin, delete resolver, one-file release"
git push && gh run watch --exit-status
gh release view dev-latest --json assets --jq '[.assets[].name]'   # expect ["eq2auras.dll"] only
```

---

## Task 2: TimerReading + TimerRow + TimerListBuilder (TDD, Mac)

**Files:**
- Create: `src/eq2auras.Core/Timers/TimerReading.cs`
- Create: `src/eq2auras.Core/Timers/TimerRow.cs`
- Create: `src/eq2auras.Core/Timers/TimerListBuilder.cs`
- Test: `tests/eq2auras.Core.Tests/TimerListBuilderTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/eq2auras.Core.Tests/TimerListBuilderTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using Eq2Auras.Core.Timers;
using Xunit;

public class TimerListBuilderTests
{
    private static TimerReading Reading(string name, int timeLeft,
        int warning = 10, int total = 30, string combatant = "none", int argb = -16776961)
        => new TimerReading
        {
            Name = name, Combatant = combatant, TimeLeft = timeLeft,
            WarningValue = warning, TotalSeconds = total, FillArgb = argb
        };

    [Fact]
    public void Sorts_soonest_first_with_name_tiebreak()
    {
        var rows = TimerListBuilder.Build(new List<TimerReading>
        {
            Reading("Slow", 25), Reading("Fast", 3), Reading("Mid", 12), Reading("Also3", 3)
        });

        Assert.Equal(new[] { "Also3", "Fast", "Mid", "Slow" }, rows.Select(r => r.Name).ToArray());
    }

    [Theory]
    [InlineData(11, TimerUrgency.Calm)]
    [InlineData(10, TimerUrgency.Imminent)]   // at WarningValue -> warning state
    [InlineData(1, TimerUrgency.Imminent)]
    [InlineData(0, TimerUrgency.Overdue)]     // measured: expire fires at 0
    [InlineData(-3, TimerUrgency.Overdue)]    // measured: TimeLeft goes negative
    public void Urgency_pivots_on_the_timers_own_WarningValue(int timeLeft, TimerUrgency expected)
    {
        var rows = TimerListBuilder.Build(new List<TimerReading> { Reading("t", timeLeft, warning: 10, total: 30) });

        Assert.Equal(expected, rows[0].Urgency);
    }

    [Theory]
    [InlineData(0, 40, 10, TimerUrgency.Imminent)]   // warning=0 -> fallback total/4 = 10
    [InlineData(0, 40, 11, TimerUrgency.Calm)]
    [InlineData(30, 30, 7, TimerUrgency.Imminent)]   // warning >= total -> same fallback (30/4=7)
    [InlineData(30, 30, 8, TimerUrgency.Calm)]
    [InlineData(0, 0, 10, TimerUrgency.Imminent)]    // total also unusable -> absolute 10s
    [InlineData(0, 0, 11, TimerUrgency.Calm)]
    public void Warning_fallbacks_fraction_of_total_then_absolute(
        int warning, int total, int timeLeft, TimerUrgency expected)
    {
        var rows = TimerListBuilder.Build(new List<TimerReading> { Reading("t", timeLeft, warning, total) });

        Assert.Equal(expected, rows[0].Urgency);
    }

    [Theory]
    [InlineData(15, 30, 0.5)]
    [InlineData(-3, 30, 0.0)]   // overdue clamps empty
    [InlineData(45, 30, 1.0)]   // >total clamps full
    [InlineData(10, 0, 0.0)]    // unusable total -> empty fill, no divide-by-zero
    public void FillFraction_is_timeLeft_over_total_clamped(int timeLeft, int total, double expected)
    {
        var rows = TimerListBuilder.Build(new List<TimerReading> { Reading("t", timeLeft, total: total) });

        Assert.Equal(expected, rows[0].FillFraction, 3);
    }

    [Fact]
    public void Multiple_instances_of_the_same_timer_each_get_a_row()
    {
        var rows = TimerListBuilder.Build(new List<TimerReading>
        {
            Reading("Holy Shield", 12), Reading("Holy Shield", 27)
        });

        Assert.Equal(2, rows.Count);
        Assert.Equal(12, rows[0].TimeLeft);
        Assert.Equal(27, rows[1].TimeLeft);
    }
}
```

- [ ] **Step 2: Run to verify red**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
Expected: FAIL — `Eq2Auras.Core.Timers` namespace missing.

- [ ] **Step 3: Implement the types**

Create `src/eq2auras.Core/Timers/TimerReading.cs`:

```csharp
namespace Eq2Auras.Core.Timers
{
    /// One raw reading of one live timer INSTANCE (a TimerFrame can hold several —
    /// measured: concurrent triggers share a frame's SpellTimers list). Colors travel
    /// as ARGB ints so Core stays free of any drawing assembly.
    public sealed class TimerReading
    {
        public string Name { get; set; }
        public string Combatant { get; set; }
        public int TimeLeft { get; set; }        // int seconds, negative after expiry (measured)
        public int WarningValue { get; set; }    // the timer's own "alert at N seconds left"
        public int TotalSeconds { get; set; }    // post-mod duration (TimerFinalDuration)
        public int FillArgb { get; set; }        // TimerData.FillColor.ToArgb()
    }
}
```

Create `src/eq2auras.Core/Timers/TimerRow.cs`:

```csharp
namespace Eq2Auras.Core.Timers
{
    public enum TimerUrgency { Calm, Imminent, Overdue }

    /// One display row of the calm list, ready for a renderer: no ACT types, no WPF types.
    public sealed class TimerRow
    {
        public string Name { get; set; }
        public string Combatant { get; set; }
        public int TimeLeft { get; set; }
        public double FillFraction { get; set; }  // 0..1 share of total duration remaining
        public int FillArgb { get; set; }
        public TimerUrgency Urgency { get; set; }
    }
}
```

Create `src/eq2auras.Core/Timers/TimerListBuilder.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace Eq2Auras.Core.Timers
{
    /// Turns raw per-instance readings into the sorted calm-list rows.
    /// Escalation pivots on each timer's own WarningValue (SPEC: we do not invent
    /// thresholds); fallbacks per SPEC when the timer lacks a usable one.
    public static class TimerListBuilder
    {
        private const double FallbackWarningFractionOfTotal = 0.25;
        private const int FallbackWarningAbsoluteSeconds = 10;

        public static List<TimerRow> Build(IEnumerable<TimerReading> readings)
        {
            return readings
                .Select(ToRow)
                .OrderBy(r => r.TimeLeft)
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static TimerRow ToRow(TimerReading reading)
        {
            return new TimerRow
            {
                Name = reading.Name,
                Combatant = reading.Combatant,
                TimeLeft = reading.TimeLeft,
                FillFraction = FillFraction(reading),
                FillArgb = reading.FillArgb,
                Urgency = UrgencyOf(reading)
            };
        }

        private static TimerUrgency UrgencyOf(TimerReading reading)
        {
            if (reading.TimeLeft <= 0) return TimerUrgency.Overdue;
            return reading.TimeLeft <= EffectiveWarning(reading) ? TimerUrgency.Imminent : TimerUrgency.Calm;
        }

        private static int EffectiveWarning(TimerReading reading)
        {
            if (reading.WarningValue > 0 && reading.WarningValue < reading.TotalSeconds)
                return reading.WarningValue;
            if (reading.TotalSeconds > 0)
                return Math.Max(1, (int)(reading.TotalSeconds * FallbackWarningFractionOfTotal));
            return FallbackWarningAbsoluteSeconds;
        }

        private static double FillFraction(TimerReading reading)
        {
            if (reading.TotalSeconds <= 0) return 0;
            var fraction = reading.TimeLeft / (double)reading.TotalSeconds;
            return Math.Max(0, Math.Min(1, fraction));
        }
    }
}
```

- [ ] **Step 4: Run to verify green**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
Expected: PASS — 5 old + 17 new (1 sort + 5 urgency + 6 fallback + 4 fraction + 1 multi-instance).

- [ ] **Step 5: Commit**

```bash
git add src/eq2auras.Core/Timers tests/eq2auras.Core.Tests/TimerListBuilderTests.cs
git commit -m "Core: TimerListBuilder — sorted calm-list rows, WarningValue urgency + spec fallbacks (Mac TDD)"
```

---

## Task 3: Probe emits readings (all SpellTimers per frame)

**Files:**
- Modify: `src/eq2auras.Plugin/Act/TimerProbe.cs`

- [ ] **Step 1: Rewrite the poll path**

Replace the contents of `src/eq2auras.Plugin/Act/TimerProbe.cs` with:

```csharp
using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Advanced_Combat_Tracker;
using Eq2Auras.Core.Diagnostics;
using Eq2Auras.Core.Timers;
using Eq2Auras.Plugin.Diagnostics;

namespace Eq2Auras.Plugin.Act
{
    /// Polls GetTimerFrames() on ACT's UI thread (WinForms timer), snapshots EVERY
    /// SpellTimer instance in every frame into plain TimerReadings, hands them to the
    /// overlay pipeline, and logs diagnostics. Lifecycle events log-only.
    public sealed class TimerProbe : IDisposable
    {
        private readonly JsonlLogWriter _log;
        private readonly Action<List<TimerReading>> _onReadings;
        private readonly Timer _pollTimer;

        public TimerProbe(JsonlLogWriter log, Action<List<TimerReading>> onReadings)
        {
            _log = log;
            _onReadings = onReadings;

            ActGlobals.oFormSpellTimers.OnSpellTimerNotify += OnNotify;
            ActGlobals.oFormSpellTimers.OnSpellTimerWarning += OnWarning;
            ActGlobals.oFormSpellTimers.OnSpellTimerExpire += OnExpire;
            ActGlobals.oFormSpellTimers.OnSpellTimerRemoved += OnRemoved;

            _pollTimer = new Timer { Interval = 100 };
            _pollTimer.Tick += OnPoll;
            _pollTimer.Start();
        }

        private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        private void OnPoll(object sender, EventArgs e)
        {
            List<TimerFrame> frames;
            try { frames = ActGlobals.oFormSpellTimers.GetTimerFrames(); }
            catch { return; }
            if (frames == null) return;

            var readings = new List<TimerReading>();
            foreach (var frame in frames)
            {
                var data = frame.TimerData;
                var instances = frame.SpellTimers;
                if (data == null || instances == null) continue;

                foreach (var instance in instances)
                {
                    readings.Add(new TimerReading
                    {
                        Name = frame.Name ?? "",
                        Combatant = frame.Combatant ?? "",
                        TimeLeft = instance.TimeLeft,
                        WarningValue = data.WarningValue,
                        TotalSeconds = instance.TimerFinalDuration,
                        FillArgb = data.FillColor.ToArgb()
                    });
                }
            }

            foreach (var reading in readings) LogReading("poll", reading);
            _onReadings(readings);
        }

        private void LogReading(string kind, TimerReading reading)
        {
            _log.Write(new TimerSnapshotRecord
            {
                Kind = kind,
                TimestampUnixMs = NowMs(),
                Name = reading.Name,
                Combatant = reading.Combatant,
                TimeLeft = reading.TimeLeft,
                WarningValue = reading.WarningValue,
                TotalValue = reading.TotalSeconds
            });
        }

        private void LogFrameEvent(string kind, TimerFrame frame)
        {
            var timers = frame.SpellTimers;
            int? timeLeft = timers != null && timers.Count > 0 ? timers[0].TimeLeft : (int?)null;
            _log.Write(new TimerSnapshotRecord
            {
                Kind = kind,
                TimestampUnixMs = NowMs(),
                Name = frame.Name ?? "",
                Combatant = frame.Combatant ?? "",
                TimeLeft = timeLeft,
                WarningValue = frame.TimerData != null ? frame.TimerData.WarningValue : 0,
                TotalValue = frame.TimerData != null ? frame.TimerData.TimerValue : 0
            });
        }

        private void OnNotify(TimerFrame f) => LogFrameEvent("notify", f);
        private void OnWarning(TimerFrame f) => LogFrameEvent("warning", f);
        private void OnExpire(TimerFrame f) => LogFrameEvent("expire", f);
        private void OnRemoved(TimerFrame f) => LogFrameEvent("removed", f);

        public void Dispose()
        {
            _pollTimer.Stop();
            _pollTimer.Tick -= OnPoll;
            _pollTimer.Dispose();

            ActGlobals.oFormSpellTimers.OnSpellTimerNotify -= OnNotify;
            ActGlobals.oFormSpellTimers.OnSpellTimerWarning -= OnWarning;
            ActGlobals.oFormSpellTimers.OnSpellTimerExpire -= OnExpire;
            ActGlobals.oFormSpellTimers.OnSpellTimerRemoved -= OnRemoved;
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add src/eq2auras.Plugin/Act/TimerProbe.cs
git commit -m "Probe: emit TimerReadings for every SpellTimer instance (multi-instance finding); events log-only"
```

---

## Task 4: TimerListWindow replaces TestWindow

**Files:**
- Create: `src/eq2auras.Plugin/Overlay/TimerListWindow.xaml`
- Create: `src/eq2auras.Plugin/Overlay/TimerListWindow.xaml.cs`
- Modify: `src/eq2auras.Plugin/Overlay/OverlayHost.cs`
- Delete: `src/eq2auras.Plugin/Overlay/TestWindow.xaml`, `src/eq2auras.Plugin/Overlay/TestWindow.xaml.cs`

- [ ] **Step 1: Window markup**

Create `src/eq2auras.Plugin/Overlay/TimerListWindow.xaml`:

```xml
<Window x:Class="Eq2Auras.Plugin.Overlay.TimerListWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        WindowStyle="None" AllowsTransparency="True" Background="Transparent"
        Topmost="True" ShowInTaskbar="False" ResizeMode="NoResize"
        SizeToContent="Height" Width="260" Left="80" Top="160">
    <StackPanel x:Name="RowsPanel" />
</Window>
```

- [ ] **Step 2: Code-behind — click-through + row rendering**

Create `src/eq2auras.Plugin/Overlay/TimerListWindow.xaml.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Eq2Auras.Core.Timers;

namespace Eq2Auras.Plugin.Overlay
{
    public partial class TimerListWindow : Window
    {
        // Phase-1 constants (future config knobs): row size, list width, colors.
        private const double RowHeight = 26;
        private const double RowWidth = 250;

        private static readonly Color CalmBackground = Color.FromArgb(150, 18, 24, 34);
        private static readonly Color CalmBorder = Color.FromArgb(200, 51, 64, 79);
        private static readonly Color ImminentBorder = Color.FromArgb(255, 229, 181, 58);
        private static readonly Color OverdueBorder = Color.FromArgb(255, 255, 77, 77);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_TRANSPARENT = 0x20;

        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        public TimerListWindow()
        {
            InitializeComponent();
            SourceInitialized += MakeClickThrough;
        }

        private void MakeClickThrough(object sender, EventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_LAYERED | WS_EX_TRANSPARENT);
        }

        /// Called on the overlay's dispatcher thread with a fresh sorted snapshot.
        public void RenderRows(List<TimerRow> rows)
        {
            RowsPanel.Children.Clear();
            foreach (var row in rows)
            {
                RowsPanel.Children.Add(BuildRow(row));
            }
        }

        private static UIElement BuildRow(TimerRow row)
        {
            var timerColor = ColorFromArgb(row.FillArgb);

            var border = new Border
            {
                Width = RowWidth,
                Height = RowHeight,
                Margin = new Thickness(0, 0, 0, 4),
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(CalmBackground),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(BorderColorFor(row.Urgency)),
                ClipToBounds = true
            };

            var grid = new Grid();

            // Draining fill, tinted by the timer's own ACT FillColor.
            grid.Children.Add(new Border
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = Math.Max(0, (RowWidth - 2) * row.FillFraction),
                Background = new SolidColorBrush(
                    Color.FromArgb(90, timerColor.R, timerColor.G, timerColor.B)),
                CornerRadius = new CornerRadius(3)
            });

            grid.Children.Add(new TextBlock
            {
                Text = row.Name,
                Foreground = Brushes.WhiteSmoke,
                FontSize = 13,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            grid.Children.Add(new TextBlock
            {
                Text = TimeText(row),
                Foreground = new SolidColorBrush(BorderColorFor(row.Urgency)),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 8, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            });

            border.Child = grid;
            return border;
        }

        private static string TimeText(TimerRow row) =>
            row.Urgency == TimerUrgency.Overdue ? "LATE +" + (-row.TimeLeft) + "s" : row.TimeLeft + "s";

        private static Color BorderColorFor(TimerUrgency urgency)
        {
            switch (urgency)
            {
                case TimerUrgency.Overdue: return OverdueBorder;
                case TimerUrgency.Imminent: return ImminentBorder;
                default: return CalmBorder;
            }
        }

        private static Color ColorFromArgb(int argb) => Color.FromArgb(
            (byte)((argb >> 24) & 0xFF), (byte)((argb >> 16) & 0xFF),
            (byte)((argb >> 8) & 0xFF), (byte)(argb & 0xFF));
    }
}
```

- [ ] **Step 3: OverlayHost hosts the list + thread-safe updates**

Replace `src/eq2auras.Plugin/Overlay/OverlayHost.cs` contents:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Threading;
using Eq2Auras.Core.Timers;

namespace Eq2Auras.Plugin.Overlay
{
    public sealed class OverlayHost : IDisposable
    {
        private Thread _thread;
        private Dispatcher _dispatcher;
        private TimerListWindow _window;

        public void Start()
        {
            var ready = new ManualResetEventSlim(false);
            _thread = new Thread(() =>
            {
                _dispatcher = Dispatcher.CurrentDispatcher;
                _window = new TimerListWindow();
                _window.Show();
                ready.Set();
                Dispatcher.Run();
            });
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.IsBackground = true;
            _thread.Start();
            ready.Wait(TimeSpan.FromSeconds(5));
        }

        /// Callable from any thread (the poll runs on ACT's UI thread).
        public void UpdateRows(List<TimerRow> rows)
        {
            var dispatcher = _dispatcher;
            if (dispatcher == null) return;
            dispatcher.BeginInvoke((Action)(() => _window?.RenderRows(rows)));
        }

        public void Dispose()
        {
            if (_dispatcher == null) return;
            _dispatcher.Invoke(() =>
            {
                _window?.Close();
                _window = null;
            });
            _dispatcher.InvokeShutdown();
            _thread?.Join(TimeSpan.FromSeconds(2));
        }
    }
}
```

- [ ] **Step 4: Delete the test window**

```bash
git rm src/eq2auras.Plugin/Overlay/TestWindow.xaml src/eq2auras.Plugin/Overlay/TestWindow.xaml.cs
```

- [ ] **Step 5: Commit**

```bash
git add src/eq2auras.Plugin/Overlay
git commit -m "Overlay: TimerListWindow renders sorted timer bars (click-through, urgency tint); TestWindow retired"
```

---

## Task 5: Wire the pipeline

**Files:**
- Modify: `src/eq2auras.Plugin/Eq2AurasPlugin.cs`

- [ ] **Step 1: Probe → builder → overlay**

In `Eq2AurasPlugin.InitPlugin`, the construction order becomes overlay-first, and the probe gets the callback. Replace:

```csharp
            _log = new JsonlLogWriter();
            _probe = new TimerProbe(_log);
            _overlay = new OverlayHost();
            _overlay.Start();
```

with:

```csharp
            _log = new JsonlLogWriter();
            _overlay = new OverlayHost();
            _overlay.Start();
            _probe = new TimerProbe(_log,
                readings => _overlay.UpdateRows(TimerListBuilder.Build(readings)));
```

and add `using Eq2Auras.Core.Timers;` to the file's usings. (`DeInitPlugin` order already disposes probe before overlay? It disposes overlay first — swap so the **probe stops first**, then the overlay, then the log:)

```csharp
            _probe?.Dispose();
            _probe = null;
            _overlay?.Dispose();
            _overlay = null;
            _log?.Dispose();
            _log = null;
```

- [ ] **Step 2: Verify Mac tests, commit, push, CI**

```bash
dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj   # PASS
git add -A && git commit -m "Wire probe -> TimerListBuilder -> overlay list"
git push && gh run watch --exit-status
```

---

## Task 6: Live verification **[WIN]**

- [ ] **Step 1: One-button update.** In ACT: **Check for updates** → single `eq2auras.dll` downloads → self-reload. Status label shows the new version and `core=E`. Delete the now-orphaned `eq2auras.Core.dll` from the Plugins folder whenever convenient (nothing loads it anymore).
- [ ] **Step 2: Fire a timer** (Holy Shield or any trigger). Expected: a **bar appears in the list** — name left, seconds right, fill draining with the timer's ACT color, sorted if multiple run, one row per concurrent instance, amber border at its own `WarningValue`, `LATE +Ns` red at zero, gone when ACT removes it (~1s past zero).
- [ ] **Step 3: Report** — screenshot/description + any weirdness; the JSONL now logs per-instance rows if we need to debug.

---

## Notes for the executor

- **Slice discipline:** no pie, no center zone, no floor, no animations — next slice.
- **Scan-safety is structural now** (single assembly) — Core-typed fields (e.g. `Action<List<TimerReading>>` in TimerProbe) are safe *because of Task 1*. Do not reorder Task 1 later.
- All `[MAC]` except Task 6.
