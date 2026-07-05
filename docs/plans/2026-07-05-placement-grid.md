# Placement Grid Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task, **inline in the main session** (repo convention). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A full-screen, permanently click-through reference grid (1 logical cm majors, 0.5 cm fainter minors) that appears with move mode and sits beneath the overlay windows — SPEC §Moving the overlay (branch `grid-overlay`, spec commits `f98d304`/`5d62931`).

**Architecture:** One code-only WPF window (`GridOverlayWindow`) hosting a draw-once `GridLines` element (aliased 1-DIP lines, frozen pens). Z-order is enforced deterministically: when move mode opens, the grid shows and every overlay window **re-asserts `HWND_TOPMOST`** (a `SetWindowPos` helper), which moves them to the top of the topmost band above the grid — no reliance on show-order accidents. `OverlayHost` owns the lifecycle. No Core changes, no settings, no persistence.

**Tech Stack:** existing (net472 WPF, P/Invoke user32). Plugin-only; branch CI is the compile gate.

## Global Constraints

- Grid is **permanently click-through**: `WS_EX_TRANSPARENT` set once at `SourceInitialized`, never toggled; `ShowActivated = false`; no chrome, no handlers.
- Draw once — no per-tick work, no animations, nothing in the render loop.
- Constants (future knobs per §Baked-in constants): pitch `96/2.54` DIPs, sky-blue lines at two alphas (major 90, minor 40 of 255), 1-DIP pen.
- Scan-safety holds: new fields are plugin-assembly/GAC types; no async.
- Merge to `main` = release (Alex's gate).

---

### Task 1: GridOverlayWindow + GridLines + WindowOrder [CI-only compile]

**Files:**
- Create: `src/eq2auras.Plugin/Overlay/GridOverlayWindow.cs`
- Create: `src/eq2auras.Plugin/Overlay/WindowOrder.cs`

**Interfaces:**
- Produces: `GridOverlayWindow()` (code-only `Window`, no XAML); `WindowOrder.RaiseTopmost(Window)`.

- [ ] **Step 1: The window + grid element** — create `src/eq2auras.Plugin/Overlay/GridOverlayWindow.cs`:

```csharp
using System.Windows;
using System.Windows.Media;

namespace Eq2Auras.Plugin.Overlay
{
    /// The unlock-mode placement grid (SPEC §Moving the overlay): a full-screen,
    /// PERMANENTLY click-through reference pinned to the primary monitor — no chrome,
    /// no drag handling, WS_EX_TRANSPARENT set once and never toggled. It cannot be
    /// moved because it is not movable. Drawn once; shown/hidden with move mode.
    public sealed class GridOverlayWindow : Window
    {
        public GridOverlayWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            Left = 0;
            Top = 0;
            Width = SystemParameters.PrimaryScreenWidth;
            Height = SystemParameters.PrimaryScreenHeight;
            Content = new GridLines();
            SourceInitialized += (s, e) => ClickThrough.Set(this, true);
        }
    }

    /// Draw-once line grid: majors every 1 logical cm, fainter minors at half-cm.
    /// Aliased 1-DIP lines (device-pixel-exact only at 100% scaling — Phase-1 DPI
    /// stance). Pens frozen; nothing here ever re-renders after layout.
    internal sealed class GridLines : FrameworkElement
    {
        private const double CmInDips = 96.0 / 2.54;          // 1 logical cm ≈ 37.8 DIPs
        private static readonly Pen MajorPen = MakePen(90);
        private static readonly Pen MinorPen = MakePen(40);

        public GridLines()
        {
            IsHitTestVisible = false;
            RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
        }

        private static Pen MakePen(byte alpha)
        {
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(alpha, 86, 180, 233)), 1.0);
            pen.Freeze();
            return pen;
        }

        protected override void OnRender(DrawingContext dc)
        {
            double half = CmInDips / 2.0;
            int i = 0;
            for (double x = 0; x <= ActualWidth; x += half, i++)
            {
                dc.DrawLine(i % 2 == 0 ? MajorPen : MinorPen, new Point(x, 0), new Point(x, ActualHeight));
            }
            i = 0;
            for (double y = 0; y <= ActualHeight; y += half, i++)
            {
                dc.DrawLine(i % 2 == 0 ? MajorPen : MinorPen, new Point(0, y), new Point(ActualWidth, y));
            }
        }
    }
}
```

- [ ] **Step 2: The z-order helper** — create `src/eq2auras.Plugin/Overlay/WindowOrder.cs`:

```csharp
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Eq2Auras.Plugin.Overlay
{
    /// Deterministic stacking inside the topmost band: re-asserting HWND_TOPMOST
    /// moves a window to the TOP of the band. The grid never gets this call — being
    /// click-through it never activates — so it stays beneath the overlay windows
    /// (SPEC §Moving the overlay: the reference draws under the things being placed).
    internal static class WindowOrder
    {
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int x, int y, int cx, int cy, uint flags);

        public static void RaiseTopmost(Window window)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
    }
}
```

- [ ] **Step 3: Core tests still green** (sanity): `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj` → PASS.
- [ ] **Step 4: Commit** — `git add src/eq2auras.Plugin/Overlay/ && git commit -m "Plugin: GridOverlayWindow (draw-once logical-cm grid, permanently click-through) + WindowOrder helper"`

---

### Task 2: OverlayHost lifecycle wiring [CI-only compile]

**Files:**
- Modify: `src/eq2auras.Plugin/Overlay/OverlayHost.cs`

**Interfaces:**
- Consumes: `GridOverlayWindow`, `WindowOrder.RaiseTopmost` (Task 1).

- [ ] **Step 1: Field + creation.** Add the field `private GridOverlayWindow _grid;` and create it (hidden — no `Show`) in the `Start` thread body, after the panel loop and **before `ready.Set()`**:

```csharp
                _grid = new GridOverlayWindow();
```

- [ ] **Step 2: Show/hide with move mode + deterministic stacking.** In `SetMoveMode`'s dispatched block, before the per-window `SetMoveMode` loop:

```csharp
                if (moving)
                {
                    _grid?.Show();
                    // The grid must sit BENEATH the windows being placed: re-asserting
                    // HWND_TOPMOST lifts each overlay window to the top of the topmost
                    // band, above the just-shown grid (SPEC §Moving the overlay).
                    foreach (var window in _listWindows) WindowOrder.RaiseTopmost(window);
                    foreach (var window in _centerWindows) WindowOrder.RaiseTopmost(window);
                }
                else
                {
                    _grid?.Hide();
                }
```

- [ ] **Step 3: Teardown.** In `Dispose`'s dispatched block, alongside the window closes:

```csharp
                _grid?.Close();
                _grid = null;
```

- [ ] **Step 4: Core tests green; commit** — `"Plugin: grid shows with move mode, beneath the overlay windows (re-assert topmost); host owns lifecycle"`

---

### Task 3: Ship + live verification **[WIN]**

- [ ] **Step 1: Push; branch CI green** (`git push -u origin grid-overlay`; publish/stamp skipped).
- [ ] **Step 2: Alex reviews** `git diff main..grid-overlay` (spec + plan + 2 implementation commits); merge on approval → release.
- [ ] **Step 3: Live check:**
1. Tick "Move overlay windows" → the grid covers the whole primary screen edge to edge; majors visibly stronger than minors; the four overlay windows and their chrome draw **on top of** the grid lines.
2. Click and drag anywhere on the grid that isn't an overlay window → the click goes **through to the game** (grid is not draggable, not clickable, not focusable).
3. Drag an overlay window across the screen → it stays above the grid the whole way; on release it still aligns against grid lines for placement.
4. Untick → grid gone instantly, overlays re-lock as before. Toggle a few times → no flicker accumulation, no leaked windows (disable plugin → everything vanishes cleanly).
5. Sanity: with a ruler, a major cell ≈ 1 cm at 100% display scaling (approximation expected otherwise — logical cm per spec).
- [ ] **Step 4: Backlog** — grid side-task → shipped note.
