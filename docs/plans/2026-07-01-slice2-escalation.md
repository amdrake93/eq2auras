# Slice 2: Escalation Engine (center pies + LATE floor) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Timers escalate: crossing their own `WarningValue` they leave the list and become a big draining radial pie in a center zone; on expiry a `LATE +Ns` alert rides a ~2s minimum-display floor (superseded instantly by a reset); overflow imminents wait highlighted in the list.

**Architecture:** A stateful but bounded **`EscalationTracker`** in Core (TDD'd on the Mac with an injected `nowMs` clock) consumes each tick's readings and produces an **`OverlayFrame`** — list rows + center elements. The tracker owns all escalation policy: warning-window pie fractions (`PreciseTimeLeft / EffectiveWarning`), the center cap with overflow-stays-in-list, LATE creation only from escalated states, the floor, and live-reading supersession. WPF gains a second click-through window (**`CenterZoneWindow`**) rendering pies (ArcSegment wedge, opacity pulse) and LATE cards (faster pulse); `OverlayHost` hosts both windows on the one STA dispatcher.

**Tech Stack:** existing stack (netstandard2.0 Core / net472 WPF / xUnit net10.0 / GitHub Actions msbuild).

**Slice discipline:** no drag-positioning, no sounds, no config UI. All new values are named constants (future knobs).

---

## Design rules being implemented (from SPEC + measured findings)

- **Pie = warning window:** fraction `= PreciseTimeLeft / EffectiveWarning`, clamped [0,1] — full at escalation, empty at zero. `PreciseTimeLeft` is sub-second (smooth drain) but clamped to agree with the integer `TimeLeft` ACT reports.
- **Model A:** a centered timer's row leaves the list. **Cap = 3 center slots**; LATE alerts rank ahead of pies and consume slots; overflow imminents stay in the list as highlighted rows and move up as slots free.
- **LATE lifecycle (per key `Name|Combatant`):** created only when a key that was **escalated** (min live `TimeLeft ≤ warning`) loses its last live reading (`TimeLeft > 0`); shows `LATE +Ns` counting from creation; expires at `created + 2000ms` (the floor — ACT's own data window is <1s, measured); **any live reading for the key cancels it instantly** (covers resets AND a still-running second instance). A *calm* key vanishing produces nothing (early removal ≠ overdue).
- Escalation is carried by **position + size + motion** (center zone, big pie, pulse); color rides on top.

## File Structure

```
src/eq2auras.Core/Timers/
├── TimerReading.cs            # + RawPreciseTimeLeft (double)
├── TimerMath.cs               # NEW: EffectiveWarning + PreciseOf (shared policy math)
├── TimerListBuilder.cs        # delegates EffectiveWarning to TimerMath
├── CenterElement.cs           # NEW: Pie | Late display element
└── EscalationTracker.cs       # NEW: readings + nowMs -> OverlayFrame (stateful, bounded)
src/eq2auras.Plugin/
├── Act/TimerProbe.cs          # populate RawPreciseTimeLeft
├── Overlay/CenterZoneWindow.xaml (+.cs)   # NEW: pies + LATE cards, pulsing
├── Overlay/OverlayHost.cs     # hosts both windows; UpdateFrame(OverlayFrame)
└── Eq2AurasPlugin.cs          # tracker in the pipeline
tests/eq2auras.Core.Tests/
├── TimerMathTests.cs          # NEW
└── EscalationTrackerTests.cs  # NEW (the meat)
```

---

## Task 1: TimerMath + PreciseTimeLeft (TDD, Mac)

**Files:**
- Create: `src/eq2auras.Core/Timers/TimerMath.cs`
- Modify: `src/eq2auras.Core/Timers/TimerReading.cs`
- Modify: `src/eq2auras.Core/Timers/TimerListBuilder.cs`
- Test: `tests/eq2auras.Core.Tests/TimerMathTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/eq2auras.Core.Tests/TimerMathTests.cs`:

```csharp
using Eq2Auras.Core.Timers;
using Xunit;

public class TimerMathTests
{
    private static TimerReading Reading(int timeLeft, int warning = 10, int total = 30, double rawPrecise = double.NaN)
        => new TimerReading
        {
            Name = "t", Combatant = "none", TimeLeft = timeLeft,
            WarningValue = warning, TotalSeconds = total,
            RawPreciseTimeLeft = double.IsNaN(rawPrecise) ? timeLeft : rawPrecise
        };

    [Theory]
    [InlineData(10, 30, 10)]   // usable warning -> itself
    [InlineData(0, 40, 10)]    // unusable -> total/4
    [InlineData(30, 30, 7)]    // warning >= total -> total/4
    [InlineData(0, 0, 10)]     // total unusable too -> absolute 10
    public void EffectiveWarning_matches_spec_fallbacks(int warning, int total, int expected)
    {
        Assert.Equal(expected, TimerMath.EffectiveWarning(Reading(5, warning, total)));
    }

    [Theory]
    [InlineData(7, 7.4, 7.4)]    // in-window raw passes through
    [InlineData(7, 12.0, 7.999)] // raw drifted high -> clamped just under next second
    [InlineData(7, 3.0, 7.0)]    // raw drifted low -> clamped to the displayed second
    public void PreciseOf_clamps_raw_into_the_displayed_second(int timeLeft, double raw, double expected)
    {
        Assert.Equal(expected, TimerMath.PreciseOf(Reading(timeLeft, rawPrecise: raw)), 3);
    }
}
```

- [ ] **Step 2: Run to verify red**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
Expected: FAIL — `TimerMath` / `RawPreciseTimeLeft` missing.

- [ ] **Step 3: Implement**

Add to `src/eq2auras.Core/Timers/TimerReading.cs` (after `TimeLeft`):

```csharp
        /// Sub-second remaining time computed by the adapter from StartTime + duration.
        /// May drift from ACT's log-derived clock — consume via TimerMath.PreciseOf,
        /// which clamps it to agree with the integer TimeLeft on display.
        public double RawPreciseTimeLeft { get; set; }
```

Create `src/eq2auras.Core/Timers/TimerMath.cs` (policy moved verbatim from `TimerListBuilder`):

```csharp
using System;

namespace Eq2Auras.Core.Timers
{
    /// Shared escalation math: one source of truth for the warning threshold and
    /// the smooth remaining-time value.
    public static class TimerMath
    {
        private const double FallbackWarningFractionOfTotal = 0.25;
        private const int FallbackWarningAbsoluteSeconds = 10;

        public static int EffectiveWarning(TimerReading reading)
        {
            if (reading.WarningValue > 0 && reading.WarningValue < reading.TotalSeconds)
                return reading.WarningValue;
            if (reading.TotalSeconds > 0)
                return Math.Max(1, (int)(reading.TotalSeconds * FallbackWarningFractionOfTotal));
            return FallbackWarningAbsoluteSeconds;
        }

        /// Smooth remaining seconds, clamped into [TimeLeft, TimeLeft + 0.999] so the
        /// pie's drain always agrees with the integer second being displayed.
        public static double PreciseOf(TimerReading reading)
        {
            return Math.Max(reading.TimeLeft, Math.Min(reading.TimeLeft + 0.999, reading.RawPreciseTimeLeft));
        }
    }
}
```

In `src/eq2auras.Core/Timers/TimerListBuilder.cs`, delete the private `EffectiveWarning` method and the two fallback constants, and change its caller:

```csharp
            return reading.TimeLeft <= TimerMath.EffectiveWarning(reading) ? TimerUrgency.Imminent : TimerUrgency.Calm;
```

- [ ] **Step 4: Run to verify green** — `dotnet test …` → PASS (20 old + 7 new).

- [ ] **Step 5: Commit**

```bash
git add src/eq2auras.Core/Timers tests/eq2auras.Core.Tests/TimerMathTests.cs
git commit -m "Core: TimerMath (shared warning policy) + clamped sub-second PreciseTimeLeft"
```

---

## Task 2: EscalationTracker (TDD, Mac — the heart of the slice)

**Files:**
- Create: `src/eq2auras.Core/Timers/CenterElement.cs`
- Create: `src/eq2auras.Core/Timers/EscalationTracker.cs`
- Test: `tests/eq2auras.Core.Tests/EscalationTrackerTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/eq2auras.Core.Tests/EscalationTrackerTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using Eq2Auras.Core.Timers;
using Xunit;

public class EscalationTrackerTests
{
    private const long T0 = 1_000_000;

    private static TimerReading Reading(string name, int timeLeft,
        int warning = 10, int total = 30, string combatant = "none")
        => new TimerReading
        {
            Name = name, Combatant = combatant, TimeLeft = timeLeft,
            WarningValue = warning, TotalSeconds = total,
            RawPreciseTimeLeft = timeLeft, FillArgb = -16776961
        };

    private static List<TimerReading> R(params TimerReading[] readings) => readings.ToList();

    [Fact]
    public void Calm_timers_stay_in_the_list_and_center_is_empty()
    {
        var frame = new EscalationTracker().Tick(R(Reading("a", 25), Reading("b", 18)), T0);

        Assert.Equal(2, frame.ListRows.Count);
        Assert.Empty(frame.CenterElements);
    }

    [Fact]
    public void Imminent_timer_leaves_the_list_and_becomes_a_pie()
    {
        var frame = new EscalationTracker().Tick(R(Reading("boss", 8), Reading("calm", 25)), T0);

        Assert.Single(frame.ListRows);
        Assert.Equal("calm", frame.ListRows[0].Name);
        var pie = Assert.Single(frame.CenterElements);
        Assert.Equal(CenterElementKind.Pie, pie.Kind);
        Assert.Equal("boss", pie.Name);
        Assert.Equal(8, pie.SecondsLeft);
        Assert.Equal(0.8, pie.PieFraction, 2);   // 8s left of a 10s warning window
    }

    [Fact]
    public void Pie_is_full_at_the_moment_of_escalation()
    {
        var frame = new EscalationTracker().Tick(R(Reading("boss", 10)), T0);

        Assert.Equal(1.0, frame.CenterElements[0].PieFraction, 2);
    }

    [Fact]
    public void Overflow_imminents_wait_in_the_list_as_highlighted_rows()
    {
        var frame = new EscalationTracker().Tick(
            R(Reading("a", 2), Reading("b", 4), Reading("c", 6), Reading("d", 8), Reading("calm", 25)), T0);

        Assert.Equal(3, frame.CenterElements.Count);   // cap
        Assert.Equal(new[] { "a", "b", "c" }, frame.CenterElements.Select(e => e.Name).ToArray());
        Assert.Equal(2, frame.ListRows.Count);          // overflow "d" + "calm"
        Assert.Equal("d", frame.ListRows[0].Name);
        Assert.Equal(TimerUrgency.Imminent, frame.ListRows[0].Urgency);
    }

    [Fact]
    public void Escalated_key_that_vanishes_becomes_a_LATE_on_the_floor()
    {
        var tracker = new EscalationTracker();
        tracker.Tick(R(Reading("boss", 1)), T0);                       // escalated
        var frame = tracker.Tick(R(), T0 + 500);                        // gone (ACT dropped it)

        var late = Assert.Single(frame.CenterElements);
        Assert.Equal(CenterElementKind.Late, late.Kind);
        Assert.Equal("boss", late.Name);
        Assert.Equal(0, late.LateSeconds);

        frame = tracker.Tick(R(), T0 + 1600);                           // still on the floor
        Assert.Equal(1, Assert.Single(frame.CenterElements).LateSeconds);

        frame = tracker.Tick(R(), T0 + 2600);                           // floor expired (2000ms)
        Assert.Empty(frame.CenterElements);
    }

    [Fact]
    public void Readings_at_or_below_zero_count_as_not_live_and_trigger_LATE()
    {
        var tracker = new EscalationTracker();
        tracker.Tick(R(Reading("boss", 1)), T0);
        var frame = tracker.Tick(R(Reading("boss", -1)), T0 + 400);     // frame lingers at -1 (measured)

        Assert.Empty(frame.ListRows);
        Assert.Equal(CenterElementKind.Late, Assert.Single(frame.CenterElements).Kind);
    }

    [Fact]
    public void Reset_supersedes_the_floor_instantly()
    {
        var tracker = new EscalationTracker();
        tracker.Tick(R(Reading("boss", 1)), T0);
        tracker.Tick(R(), T0 + 500);                                    // LATE created
        var frame = tracker.Tick(R(Reading("boss", 30)), T0 + 900);     // ability fired -> reset

        Assert.Empty(frame.CenterElements);                             // LATE cancelled
        Assert.Equal("boss", Assert.Single(frame.ListRows).Name);       // calm row back
    }

    [Fact]
    public void A_surviving_second_instance_suppresses_LATE()
    {
        var tracker = new EscalationTracker();
        tracker.Tick(R(Reading("boss", 1), Reading("boss", 12)), T0);
        var frame = tracker.Tick(R(Reading("boss", 12)), T0 + 500);     // first instance expired

        Assert.Empty(frame.CenterElements.Where(e => e.Kind == CenterElementKind.Late));
        Assert.Single(frame.ListRows);
    }

    [Fact]
    public void Calm_key_vanishing_produces_nothing()
    {
        var tracker = new EscalationTracker();
        tracker.Tick(R(Reading("boss", 25)), T0);
        var frame = tracker.Tick(R(), T0 + 500);

        Assert.Empty(frame.CenterElements);
        Assert.Empty(frame.ListRows);
    }

    [Fact]
    public void LATEs_rank_ahead_of_pies_and_consume_center_slots()
    {
        var tracker = new EscalationTracker();
        tracker.Tick(R(Reading("dead1", 1), Reading("dead2", 1, combatant: "other")), T0);
        var frame = tracker.Tick(
            R(Reading("p1", 2), Reading("p2", 4), Reading("p3", 6)), T0 + 500);

        Assert.Equal(3, frame.CenterElements.Count);
        Assert.Equal(2, frame.CenterElements.Count(e => e.Kind == CenterElementKind.Late));
        Assert.Equal("p1", frame.CenterElements.Single(e => e.Kind == CenterElementKind.Pie).Name);
        Assert.Equal(2, frame.ListRows.Count);          // p2, p3 overflow back to the list
    }
}
```

- [ ] **Step 2: Run to verify red** — FAIL: `CenterElement`/`EscalationTracker` missing.

- [ ] **Step 3: Implement the types**

Create `src/eq2auras.Core/Timers/CenterElement.cs`:

```csharp
using System.Collections.Generic;

namespace Eq2Auras.Core.Timers
{
    public enum CenterElementKind { Pie, Late }

    /// One element of the center escalation zone.
    public sealed class CenterElement
    {
        public CenterElementKind Kind { get; set; }
        public string Name { get; set; }
        public string Combatant { get; set; }
        public int SecondsLeft { get; set; }      // Pie: seconds remaining
        public double PieFraction { get; set; }   // Pie: remaining share of the warning window
        public int LateSeconds { get; set; }      // Late: seconds since it went overdue
        public int FillArgb { get; set; }
    }

    /// Everything the overlay renders for one tick.
    public sealed class OverlayFrame
    {
        public List<TimerRow> ListRows { get; set; } = new List<TimerRow>();
        public List<CenterElement> CenterElements { get; set; } = new List<CenterElement>();
    }
}
```

Create `src/eq2auras.Core/Timers/EscalationTracker.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace Eq2Auras.Core.Timers
{
    /// The escalation state machine. Stateful but bounded (SPEC): per-key memory of
    /// "was escalated last tick" plus active LATE floors — nothing unbounded. Call
    /// Tick once per poll from a single thread with a monotonic nowMs.
    public sealed class EscalationTracker
    {
        private const int CenterSlots = 3;          // future config knob
        private const long LateFloorMs = 2000;      // minimum LATE display (measured: ACT gives <1s)

        private sealed class LateEntry
        {
            public long CreatedMs;
            public string Name;
            public string Combatant;
            public int FillArgb;
        }

        private readonly Dictionary<string, bool> _wasEscalated = new Dictionary<string, bool>();
        private readonly Dictionary<string, LateEntry> _lates = new Dictionary<string, LateEntry>();

        public OverlayFrame Tick(IReadOnlyList<TimerReading> readings, long nowMs)
        {
            var live = readings.Where(r => r.TimeLeft > 0).ToList();
            var liveKeys = new HashSet<string>(live.Select(KeyOf));

            CancelLatesWithLiveReadings(liveKeys);
            CreateLatesForVanishedEscalatedKeys(readings, liveKeys, nowMs);
            ExpireLates(nowMs);
            RememberEscalationState(live, liveKeys);

            var lates = _lates.Values
                .OrderByDescending(l => l.CreatedMs)
                .Select(l => new CenterElement
                {
                    Kind = CenterElementKind.Late,
                    Name = l.Name,
                    Combatant = l.Combatant,
                    LateSeconds = (int)((nowMs - l.CreatedMs) / 1000),
                    FillArgb = l.FillArgb
                })
                .ToList();

            var imminent = live
                .Where(r => r.TimeLeft <= TimerMath.EffectiveWarning(r))
                .OrderBy(TimerMath.PreciseOf)
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var pieSlots = Math.Max(0, CenterSlots - lates.Count);
            var centered = imminent.Take(pieSlots).ToList();
            var pies = centered.Select(r => new CenterElement
            {
                Kind = CenterElementKind.Pie,
                Name = r.Name,
                Combatant = r.Combatant,
                SecondsLeft = r.TimeLeft,
                PieFraction = Math.Min(1.0, TimerMath.PreciseOf(r) / TimerMath.EffectiveWarning(r)),
                FillArgb = r.FillArgb
            });

            return new OverlayFrame
            {
                ListRows = TimerListBuilder.Build(live.Except(centered)),
                CenterElements = lates.Concat(pies).ToList()
            };
        }

        private void CancelLatesWithLiveReadings(HashSet<string> liveKeys)
        {
            foreach (var key in _lates.Keys.Where(liveKeys.Contains).ToList())
            {
                _lates.Remove(key);
            }
        }

        private void CreateLatesForVanishedEscalatedKeys(
            IReadOnlyList<TimerReading> readings, HashSet<string> liveKeys, long nowMs)
        {
            foreach (var pair in _wasEscalated.Where(p => p.Value && !liveKeys.Contains(p.Key)))
            {
                if (_lates.ContainsKey(pair.Key)) continue;

                var lastSeen = readings.FirstOrDefault(r => KeyOf(r) == pair.Key);
                var parts = pair.Key.Split(new[] { '|' }, 2);
                _lates[pair.Key] = new LateEntry
                {
                    CreatedMs = nowMs,
                    Name = parts[0],
                    Combatant = parts.Length > 1 ? parts[1] : "",
                    FillArgb = lastSeen != null ? lastSeen.FillArgb : 0
                };
            }
        }

        private void ExpireLates(long nowMs)
        {
            foreach (var key in _lates.Where(p => nowMs - p.Value.CreatedMs >= LateFloorMs)
                                      .Select(p => p.Key).ToList())
            {
                _lates.Remove(key);
            }
        }

        private void RememberEscalationState(List<TimerReading> live, HashSet<string> liveKeys)
        {
            foreach (var key in _wasEscalated.Keys.Where(k => !liveKeys.Contains(k)).ToList())
            {
                _wasEscalated.Remove(key);
            }
            foreach (var group in live.GroupBy(KeyOf))
            {
                _wasEscalated[group.Key] = group.Any(r => r.TimeLeft <= TimerMath.EffectiveWarning(r));
            }
        }

        private static string KeyOf(TimerReading r) => r.Name + "|" + r.Combatant;
    }
}
```

- [ ] **Step 4: Run to verify green** — `dotnet test …` → PASS (27 old + 10 new).

- [ ] **Step 5: Commit**

```bash
git add src/eq2auras.Core/Timers tests/eq2auras.Core.Tests/EscalationTrackerTests.cs
git commit -m "Core: EscalationTracker — pies at WarningValue, LATE floor + reset supersession, center cap with overflow (Mac TDD)"
```

---

## Task 3: Probe populates RawPreciseTimeLeft

**Files:**
- Modify: `src/eq2auras.Plugin/Act/TimerProbe.cs`

- [ ] **Step 1: Compute sub-second remaining in the poll**

In `TimerProbe.OnPoll`, the inner `foreach (var instance in instances)` block gains one line — replace the `readings.Add(...)` object initializer with:

```csharp
                    readings.Add(new TimerReading
                    {
                        Name = frame.Name ?? "",
                        Combatant = frame.Combatant ?? "",
                        TimeLeft = instance.TimeLeft,
                        RawPreciseTimeLeft = instance.TimerFinalDuration
                            - (DateTime.Now - instance.StartTime).TotalSeconds,
                        WarningValue = data.WarningValue,
                        TotalSeconds = instance.TimerFinalDuration,
                        FillArgb = data.FillColor.ToArgb()
                    });
```

(Wall clock may drift from ACT's log-derived clock — that's why `TimerMath.PreciseOf` clamps into the displayed second.)

- [ ] **Step 2: Commit**

```bash
git add src/eq2auras.Plugin/Act/TimerProbe.cs
git commit -m "Probe: sub-second RawPreciseTimeLeft from StartTime + TimerFinalDuration"
```

---

## Task 4: CenterZoneWindow (pies + LATE cards) and dual-window OverlayHost

**Files:**
- Create: `src/eq2auras.Plugin/Overlay/CenterZoneWindow.xaml`
- Create: `src/eq2auras.Plugin/Overlay/CenterZoneWindow.xaml.cs`
- Modify: `src/eq2auras.Plugin/Overlay/OverlayHost.cs`

- [ ] **Step 1: Window markup**

Create `src/eq2auras.Plugin/Overlay/CenterZoneWindow.xaml`:

```xml
<Window x:Class="Eq2Auras.Plugin.Overlay.CenterZoneWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        WindowStyle="None" AllowsTransparency="True" Background="Transparent"
        Topmost="True" ShowInTaskbar="False" ResizeMode="NoResize"
        SizeToContent="Height" Width="200">
    <StackPanel x:Name="ElementsPanel" />
</Window>
```

- [ ] **Step 2: Code-behind — pie geometry, LATE cards, pulses, click-through**

Create `src/eq2auras.Plugin/Overlay/CenterZoneWindow.xaml.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Eq2Auras.Core.Timers;

namespace Eq2Auras.Plugin.Overlay
{
    public partial class CenterZoneWindow : Window
    {
        // Phase-1 constants (future config knobs).
        private const double PieDiameter = 110;
        private const double ZoneVerticalScreenFraction = 0.38; // zone top ≈ 38% down the screen

        private static readonly Color LateColor = Colors.Crimson;
        private static readonly Color TextColor = Colors.WhiteSmoke;
        private static readonly Color PieBackplate = Color.FromArgb(120, 18, 24, 34);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_TRANSPARENT = 0x20;

        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        public CenterZoneWindow()
        {
            InitializeComponent();
            Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
            Top = SystemParameters.PrimaryScreenHeight * ZoneVerticalScreenFraction;
            SourceInitialized += MakeClickThrough;
        }

        private void MakeClickThrough(object sender, EventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_LAYERED | WS_EX_TRANSPARENT);
        }

        /// Called on the overlay's dispatcher thread with a fresh snapshot.
        public void RenderElements(List<CenterElement> elements)
        {
            ElementsPanel.Children.Clear();
            foreach (var element in elements)
            {
                ElementsPanel.Children.Add(element.Kind == CenterElementKind.Late
                    ? BuildLateCard(element)
                    : BuildPie(element));
            }
        }

        private static UIElement BuildPie(CenterElement element)
        {
            var color = Soften(ColorFromArgb(element.FillArgb));
            double r = PieDiameter / 2;
            var center = new Point(r, r);

            var canvas = new Canvas { Width = PieDiameter, Height = PieDiameter };
            canvas.Children.Add(new Ellipse
            {
                Width = PieDiameter, Height = PieDiameter,
                Fill = new SolidColorBrush(PieBackplate),
                Stroke = new SolidColorBrush(Color.FromArgb(200, color.R, color.G, color.B)),
                StrokeThickness = 2
            });
            canvas.Children.Add(new Path
            {
                Fill = new SolidColorBrush(Color.FromArgb(170, color.R, color.G, color.B)),
                Data = BuildPieGeometry(center, r - 4, element.PieFraction)
            });

            var seconds = new TextBlock
            {
                Text = element.SecondsLeft.ToString(),
                FontSize = 34, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(TextColor),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            var name = new TextBlock
            {
                Text = element.Name,
                FontSize = 13, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(TextColor),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 190
            };

            var overlayText = new Grid { Width = PieDiameter, Height = PieDiameter };
            overlayText.Children.Add(seconds);
            seconds.VerticalAlignment = VerticalAlignment.Center;

            var pieStack = new Grid();
            pieStack.Children.Add(canvas);
            pieStack.Children.Add(overlayText);

            var root = new StackPanel
            {
                Margin = new Thickness(0, 0, 0, 10),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            root.Children.Add(pieStack);
            root.Children.Add(name);

            Pulse(root, from: 1.0, to: 0.75, seconds: 0.6);
            return root;
        }

        private static UIElement BuildLateCard(CenterElement element)
        {
            var card = new Border
            {
                Width = 170,
                Margin = new Thickness(0, 0, 0, 10),
                Padding = new Thickness(10, 6, 10, 6),
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromArgb(200, 58, 20, 20)),
                BorderBrush = new SolidColorBrush(LateColor),
                BorderThickness = new Thickness(2),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = "LATE +" + element.LateSeconds + "s",
                FontSize = 22, FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(TextColor),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            stack.Children.Add(new TextBlock
            {
                Text = element.Name,
                FontSize = 12,
                Foreground = new SolidColorBrush(TextColor),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            card.Child = stack;

            Pulse(card, from: 1.0, to: 0.55, seconds: 0.35);
            return card;
        }

        private static void Pulse(UIElement element, double from, double to, double seconds)
        {
            var animation = new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            element.BeginAnimation(OpacityProperty, animation);
        }

        /// Filled wedge spanning `fraction` of the circle, from 12 o'clock, clockwise —
        /// full at escalation, draining to empty as the warning window runs out.
        private static Geometry BuildPieGeometry(Point center, double radius, double fraction)
        {
            fraction = Math.Max(0, Math.Min(1, fraction));
            if (fraction >= 0.999) return new EllipseGeometry(center, radius, radius);
            if (fraction <= 0.001) return Geometry.Empty;

            double theta = fraction * 2 * Math.PI;
            var start = new Point(center.X, center.Y - radius);
            var end = new Point(center.X + radius * Math.Sin(theta), center.Y - radius * Math.Cos(theta));

            var figure = new PathFigure { StartPoint = center, IsClosed = true };
            figure.Segments.Add(new LineSegment(start, false));
            figure.Segments.Add(new ArcSegment(end, new Size(radius, radius), 0,
                fraction > 0.5, SweepDirection.Clockwise, false));

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            return geometry;
        }

        private static Color ColorFromArgb(int argb) => Color.FromArgb(
            (byte)((argb >> 24) & 0xFF), (byte)((argb >> 16) & 0xFF),
            (byte)((argb >> 8) & 0xFF), (byte)(argb & 0xFF));

        private static Color Soften(Color c)
        {
            const double keep = 0.65;
            const byte slateR = 110, slateG = 118, slateB = 130;
            return Color.FromArgb(255,
                (byte)(c.R * keep + slateR * (1 - keep)),
                (byte)(c.G * keep + slateG * (1 - keep)),
                (byte)(c.B * keep + slateB * (1 - keep)));
        }
    }
}
```

- [ ] **Step 3: OverlayHost hosts both windows, takes OverlayFrame**

Replace `src/eq2auras.Plugin/Overlay/OverlayHost.cs`:

```csharp
using System;
using System.Threading;
using System.Windows.Threading;
using Eq2Auras.Core.Timers;

namespace Eq2Auras.Plugin.Overlay
{
    public sealed class OverlayHost : IDisposable
    {
        private Thread _thread;
        private Dispatcher _dispatcher;
        private TimerListWindow _listWindow;
        private CenterZoneWindow _centerWindow;

        public void Start()
        {
            var ready = new ManualResetEventSlim(false);
            _thread = new Thread(() =>
            {
                _dispatcher = Dispatcher.CurrentDispatcher;
                _listWindow = new TimerListWindow();
                _listWindow.Show();
                _centerWindow = new CenterZoneWindow();
                _centerWindow.Show();
                ready.Set();
                Dispatcher.Run();
            });
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.IsBackground = true;
            _thread.Start();
            ready.Wait(TimeSpan.FromSeconds(5));
        }

        /// Callable from any thread (the poll runs on ACT's UI thread).
        public void UpdateFrame(OverlayFrame frame)
        {
            var dispatcher = _dispatcher;
            if (dispatcher == null) return;
            dispatcher.BeginInvoke((Action)(() =>
            {
                _listWindow?.RenderRows(frame.ListRows);
                _centerWindow?.RenderElements(frame.CenterElements);
            }));
        }

        public void Dispose()
        {
            if (_dispatcher == null) return;
            _dispatcher.Invoke(() =>
            {
                _listWindow?.Close();
                _listWindow = null;
                _centerWindow?.Close();
                _centerWindow = null;
            });
            _dispatcher.InvokeShutdown();
            _thread?.Join(TimeSpan.FromSeconds(2));
        }
    }
}
```

- [ ] **Step 4: Commit**

```bash
git add src/eq2auras.Plugin/Overlay
git commit -m "Overlay: CenterZoneWindow (draining pies + pulsing LATE cards) hosted alongside the list"
```

---

## Task 5: Wire the tracker + tracer bump

**Files:**
- Modify: `src/eq2auras.Plugin/Eq2AurasPlugin.cs`
- Modify: `src/eq2auras.Core/CoreBuildInfo.cs`

- [ ] **Step 1: Tracker into the pipeline**

In `Eq2AurasPlugin`, add a field:

```csharp
        private EscalationTracker _tracker;
```

and in `InitPlugin` replace the probe construction:

```csharp
            _tracker = new EscalationTracker();
            _probe = new TimerProbe(_log,
                readings => _overlay.UpdateFrame(
                    _tracker.Tick(readings, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())));
```

(The tracker is only ever touched on ACT's UI thread — the poll — so it needs no locking.) In `DeInitPlugin`, add `_tracker = null;` after `_probe = null;`.

- [ ] **Step 2: Marker** — `CoreBuildInfo.Marker => "F";`

- [ ] **Step 3: Verify, commit, push, CI**

```bash
dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj   # PASS (37)
git add -A && git commit -m "Wire EscalationTracker into the pipeline; marker F"
git push && gh run watch --exit-status
```

---

## Task 6: Live verification **[WIN]**

- [ ] **Step 1: Check for updates** → self-reload → status shows `core=F`.
- [ ] **Step 2: Fire a timer and watch the full arc:** calm bar → at its `WarningValue` the row **leaves the list** and a **big draining pie** appears center-screen (full at escalation, pulsing, big seconds + name) → at 0 the pie is replaced by a **pulsing `LATE +Ns` card for ~2 seconds** → gone.
- [ ] **Step 3: Re-fire before expiry** → pie vanishes, calm bar returns (reset supersession).
- [ ] **Step 4: Stack 4+ timers into their warning windows** (paste trigger lines) → 3 center elements max, most-urgent-first; the rest wait highlighted in the list and move up as slots free.
- [ ] **Step 5: Report** — especially pie drain smoothness (sub-second precise time) and whether the LATE floor feels right at 2s.
