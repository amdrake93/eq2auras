# Hover Surface (HoverCard + HoverPlacement) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Formalize the field-proven hover surface (SPEC Part I §The hover surface) as a reusable, module-agnostic floating-card component whose never-off-screen placement is a pure, TDD'd Core guarantee — wired to the meter's by-row mouseover and fed **placeholder** content (real by-target data deferred).

**Architecture:** Three units. (1) **Core `HoverPlacement`** — a pure geometry function (no WPF) that, given the host window rect, the anchored row rect, the card size, the screen bounds, the card's chrome insets, and prefer-left/grow-up flags, returns the card's on-screen `(Left, Top)`; the whole off-screen safeguard, exhaustively unit-tested. (2) **Plugin `HoverCard`** — a module-agnostic WPF floating card (adapted from the spike's `MeterHoverWindow`) that measures its content, draws the distinct card chrome, and asks `HoverPlacement` where to sit. (3) The **meter's hover lifecycle** in `MeterWindow` — row mouse-enter/leave → build placeholder content → show/hide the card, recreated per appearance. This build is off `main`; the `mouseover-spike` branch is the reference, not a base — nothing is refactored, the code below is written fresh.

**Tech Stack:** C# — Core `netstandard2.0` (Mac-testable, xUnit), Plugin `net472`/WPF (compile-verified in CI, behavior field-verified on the box).

## Global Constraints

Copied from the SPEC (every task's requirements implicitly include these):

- **Single-assembly packaging.** New Core files are compiled into the plugin via the existing `<Compile Include="..\eq2auras.Core\**\*.cs">` glob, and new Plugin files via the SDK default glob — **no `.csproj` edits needed**; never reference a second DLL.
- **No WPF in Core.** `HoverPlacement`, `HoverRect`, `HoverPoint` use only `System` (`Math`) — no `System.Windows.*`. Core must keep building on `netstandard2.0` for the Mac test loop.
- **No `async` added to the Plugin project; no `System.Web.Extensions`.** N/A here (no async, no JSON), stated so it stays honored.
- **Transient, not persisted.** The hover card is runtime-only — no `MeterWindowConfig`/`MeterSettings` changes, no DCJS surface.
- **Core-TDD, Plugin-transcribe.** Task 1 is strict TDD in Core. Tasks 2–3 are WPF transcribe: not Mac-buildable, so their gate is the branch verify-CI compile plus the on-box field script (§Verification).
- **This build touches only Core `HoverPlacement` + Plugin `HoverCard` + `MeterWindow`.** No `OverlayHost`, `EncounterProbe`, `MeterWindowCallbacks`, or `Eq2AurasPlugin` changes — the placeholder content is built synchronously in the lifecycle, so there is **no async data channel** (the spike's channel is not ported).

---

## File Structure

- **Create** `src/eq2auras.Core/Overlay/HoverPlacement.cs` — the pure placement function + `HoverRect`/`HoverPoint` DTOs (namespace `Eq2Auras.Core.Overlay`, feature-agnostic overlay geometry).
- **Create** `tests/eq2auras.Core.Tests/HoverPlacementTests.cs` — the xUnit tests for every placement branch.
- **Create** `src/eq2auras.Plugin/Overlay/HoverCard.cs` — the module-agnostic WPF card (namespace `Eq2Auras.Plugin.Overlay`).
- **Modify** `src/eq2auras.Plugin/Overlay/MeterWindow.cs` — the hover lifecycle (fields, slot wiring, enter/leave, show/hide, placeholder content, host/anchor rects) + `OnClosed` cleanup.

---

## Task 1: Core `HoverPlacement` (strict TDD)

**Files:**
- Create: `src/eq2auras.Core/Overlay/HoverPlacement.cs`
- Test: `tests/eq2auras.Core.Tests/HoverPlacementTests.cs`

**Interfaces:**
- Produces:
  - `struct Eq2Auras.Core.Overlay.HoverRect { double Left, Top, Width, Height; double Right => Left+Width; double Bottom => Top+Height; }`
  - `struct Eq2Auras.Core.Overlay.HoverPoint { double Left, Top; }`
  - `static HoverPoint HoverPlacement.Compute(HoverRect host, HoverRect anchor, double cardWidth, double cardHeight, double screenWidth, double screenHeight, double topInset, double bottomInset, double gap, bool preferLeft, bool growUp)`

- [ ] **Step 1: Write the failing tests**

Create `tests/eq2auras.Core.Tests/HoverPlacementTests.cs`:

```csharp
using Eq2Auras.Core.Overlay;
using Xunit;

public class HoverPlacementTests
{
    private static HoverRect R(double left, double top, double width, double height)
        => new HoverRect { Left = left, Top = top, Width = width, Height = height };

    // Common card/screen for the readable cases: 300x120 card, 1920x1080 screen,
    // topInset 30, bottomInset 10, gap 8.
    private static HoverPoint Place(HoverRect host, HoverRect anchor, bool preferLeft = true, bool growUp = true,
        double cardW = 300, double cardH = 120, double screenW = 1920, double screenH = 1080)
        => HoverPlacement.Compute(host, anchor, cardW, cardH, screenW, screenH,
            topInset: 30, bottomInset: 10, gap: 8, preferLeft: preferLeft, growUp: growUp);

    [Fact]
    public void PreferLeft_when_left_fits_places_left_and_grows_up_last_row_on_row_bottom()
    {
        var p = Place(host: R(1000, 800, 260, 200), anchor: R(1000, 850, 260, 26));
        Assert.Equal(692, p.Left);                 // 1000 - 8 - 300
        Assert.Equal(766, p.Top);                  // rowBottom(876) - 120 + 10
        Assert.Equal(876, p.Top + 120 - 10);       // last row bottom == row bottom
    }

    [Fact]
    public void PreferLeft_when_left_overflows_flips_right()
    {
        var p = Place(host: R(100, 800, 260, 200), anchor: R(100, 850, 260, 26));
        Assert.Equal(368, p.Left);                 // right(360) + 8
    }

    [Fact]
    public void When_neither_side_fits_keeps_preferred_then_clamps_on_screen()
    {
        var p = Place(host: R(50, 800, 260, 40), anchor: R(50, 810, 260, 26), screenW: 400);
        Assert.Equal(0, p.Left);                   // leftX -258 kept, clamped to 0
    }

    [Fact]
    public void GrowUp_off_the_top_falls_back_to_grow_down_first_row_on_row_top()
    {
        var p = Place(host: R(1000, 30, 260, 200), anchor: R(1000, 50, 260, 26));
        Assert.Equal(20, p.Top);                   // yUp -34 -> yDown = rowTop(50) - 30
        Assert.Equal(50, p.Top + 30);              // first row top == row top
    }

    [Fact]
    public void GrowDown_preference_when_down_fits_first_row_on_row_top()
    {
        var p = Place(host: R(1000, 100, 260, 200), anchor: R(1000, 100, 260, 26), growUp: false);
        Assert.Equal(70, p.Top);                   // yDown = rowTop(100) - 30
        Assert.Equal(100, p.Top + 30);             // first row top == row top
    }

    [Fact]
    public void GrowDown_off_the_bottom_falls_back_to_grow_up()
    {
        var p = Place(host: R(1000, 900, 260, 200), anchor: R(1000, 1000, 260, 26), growUp: false);
        Assert.Equal(916, p.Top);                  // yDown 970 overflows -> yUp = rowBottom(1026) - 120 + 10
    }

    [Fact]
    public void PreferRight_when_right_fits_places_right()
    {
        var p = Place(host: R(200, 800, 260, 200), anchor: R(200, 850, 260, 26), preferLeft: false);
        Assert.Equal(468, p.Left);                 // right(460) + 8
    }

    [Fact]
    public void GrowUp_that_would_overflow_the_bottom_is_clamped_up()
    {
        var p = Place(host: R(1000, 1040, 260, 40), anchor: R(1000, 1049, 260, 26), cardH: 50);
        Assert.Equal(1030, p.Top);                 // yUp 1035 -> clamped to screenH(1080) - 50
    }

    [Fact]
    public void No_input_yields_an_off_screen_card()
    {
        var p = Place(host: R(-500, 2000, 260, 40), anchor: R(-500, 2010, 260, 26));
        Assert.InRange(p.Left, 0, 1920 - 300);
        Assert.InRange(p.Top, 0, 1080 - 120);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter FullyQualifiedName~HoverPlacement`
Expected: FAIL — `HoverPlacement` / `HoverRect` / `HoverPoint` do not exist (compile error).

- [ ] **Step 3: Write the implementation**

Create `src/eq2auras.Core/Overlay/HoverPlacement.cs`:

```csharp
using System;

namespace Eq2Auras.Core.Overlay
{
    /// A screen-space rectangle for hover placement (feature-agnostic — no WPF).
    public struct HoverRect
    {
        public double Left;
        public double Top;
        public double Width;
        public double Height;

        public double Right => Left + Width;
        public double Bottom => Top + Height;
    }

    /// The placed card's top-left in screen DIPs.
    public struct HoverPoint
    {
        public double Left;
        public double Top;
    }

    /// Where a hover card lands, as pure geometry (SPEC Part I §The hover surface). The whole
    /// never-off-screen guarantee: prefer a side of the host and flip on overflow; grow the
    /// preferred vertical direction and fall back to the other; then clamp the minimum amount so
    /// the whole card is on-screen. The chrome insets align a *row inside the card* to the hovered
    /// row (bottom inset in grow-up: the card's last row bottom on the row's bottom; top inset in
    /// grow-down: the card's first row top on the row's top). Any renderer calling this is safe by
    /// construction; Core drives nothing — it only answers.
    public static class HoverPlacement
    {
        public static HoverPoint Compute(
            HoverRect host, HoverRect anchor,
            double cardWidth, double cardHeight,
            double screenWidth, double screenHeight,
            double topInset, double bottomInset,
            double gap, bool preferLeft, bool growUp)
        {
            double leftX = host.Left - gap - cardWidth;
            double rightX = host.Right + gap;
            double x = preferLeft
                ? (leftX >= 0 ? leftX : (rightX + cardWidth <= screenWidth ? rightX : leftX))
                : (rightX + cardWidth <= screenWidth ? rightX : (leftX >= 0 ? leftX : rightX));

            double yUp = anchor.Bottom - cardHeight + bottomInset;   // card's last row bottom on the row's bottom
            double yDown = anchor.Top - topInset;                    // card's first row top on the row's top
            double y = growUp
                ? (yUp >= 0 ? yUp : yDown)
                : (yDown + cardHeight <= screenHeight ? yDown : yUp);

            x = Math.Max(0, Math.Min(x, screenWidth - cardWidth));
            y = Math.Max(0, Math.Min(y, screenHeight - cardHeight));
            return new HoverPoint { Left = x, Top = y };
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter FullyQualifiedName~HoverPlacement`
Expected: PASS — 9 passed.

- [ ] **Step 5: Run the full Core suite (no regressions)**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
Expected: PASS — the prior green count + 9.

- [ ] **Step 6: Commit**

```bash
git add src/eq2auras.Core/Overlay/HoverPlacement.cs tests/eq2auras.Core.Tests/HoverPlacementTests.cs
git commit -m "Hover surface: Core HoverPlacement — pure on-screen placement geometry (TDD)"
```

---

## Task 2: Plugin `HoverCard` component (transcribe)

**Files:**
- Create: `src/eq2auras.Plugin/Overlay/HoverCard.cs`

**Interfaces:**
- Consumes: `Eq2Auras.Core.Overlay.HoverPlacement.Compute(...)`, `HoverRect`; `Eq2Auras.Core.Meter.MeterRow`; `Eq2Auras.Plugin.Overlay.VisualStyle`, `MeterRowVisual`, `MeterColumns`, `Theme`, `ClickThrough` (all Plugin, `HoverCard`'s own namespace — no `using`).
- Produces:
  - `internal sealed class HoverCard : Window` with `HoverCard(VisualStyle style, double opacity)`, `void Update(string titleText, List<MeterRow> rows)`, `void ShowAt(HoverRect host, HoverRect anchor)`, and properties `double ContentTopInset`, `double ContentBottomInset`, `double MeasuredHeight`.

- [ ] **Step 1: Write the component**

Create `src/eq2auras.Plugin/Overlay/HoverCard.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Eq2Auras.Core.Meter;
using Eq2Auras.Core.Overlay;

namespace Eq2Auras.Plugin.Overlay
{
    /// The shared hover surface (SPEC Part I §The hover surface): a borderless, topmost,
    /// CLICK-THROUGH floating card shown beside a window while a row is hovered. Click-through
    /// (ClickThrough.Set + IsHitTestVisible=false) is the whole trick — it never captures the
    /// cursor, so the row's MouseLeave still fires and clicks still land on the row beneath.
    /// Module-agnostic: the caller supplies the look (VisualStyle + opacity) and the content
    /// (title + MeterRow list); the card owns only its own chrome and delegates every placement
    /// decision to Core HoverPlacement. Fed placeholder content this pass; the real data path
    /// plugs into the caller's content build later (SPEC §Reserved seams).
    internal sealed class HoverCard : Window
    {
        private const int MaxRows = 15;        // caps a many-row card's height
        private const double MaxWidth = 560;   // sanity cap so one long label can't span the screen
        private const double CardBorder = 2;
        private const double CardPadding = 5;
        private static readonly Color CardSurface = Color.FromArgb(0xFF, 0x2A, 0x2F, 0x3A);   // lighter than the meter backplate, opaque
        private static readonly Color CardEdge = Color.FromRgb(0xA6, 0xAE, 0xBE);             // a bright, unmistakable edge

        private readonly VisualStyle _style;
        private readonly double _opacity;
        private readonly StackPanel _rowsPanel;
        private readonly StackPanel _content;
        private readonly List<MeterRowVisual> _slots = new List<MeterRowVisual>();
        private readonly TextBlock _title;

        public HoverCard(VisualStyle style, double opacity)
        {
            _style = style;
            _opacity = opacity;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height;
            Width = style.RowWidth;
            IsHitTestVisible = false;   // belt-and-suspenders with ClickThrough — never steal the cursor

            // Header band at the window's header height (DefaultRowHeight) so the card's chrome
            // lines up with the spawning window's header, not just a bare text line.
            _title = new TextBlock { Foreground = Theme.TextLabel, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
            style.ApplyFont(_title, style.RowText);
            var header = new Border { Height = VisualStyle.DefaultRowHeight, Child = _title };

            _rowsPanel = new StackPanel();
            _content = new StackPanel { Width = style.RowWidth };
            _content.Children.Add(header);
            _content.Children.Add(_rowsPanel);

            Content = new Border
            {
                Background = new SolidColorBrush(CardSurface),
                BorderThickness = new Thickness(CardBorder),
                BorderBrush = new SolidColorBrush(CardEdge),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(CardPadding),   // the lighter surface frames the darker rows
                Child = _content,
            };

            SourceInitialized += (s, e) => ClickThrough.Set(this, true);
        }

        /// Rebind the pooled rows and size the card's width to fit the content (a mouseover is a
        /// deliberate act, so fitting the data earns the affordance). One width across all rows so
        /// the proportional bars stay comparable; floored at the source window's width, capped.
        public void Update(string titleText, List<MeterRow> rows)
        {
            _title.Text = titleText;
            rows = rows ?? new List<MeterRow>();
            int show = rows.Count < MaxRows ? rows.Count : MaxRows;

            double width = ComputeWidth(titleText, rows, show);
            _content.Width = width;
            Width = width + 2 * (CardPadding + CardBorder);   // window footprint = rows + the card frame

            while (_slots.Count < show)
            {
                var slot = new MeterRowVisual(_style, _opacity);
                _slots.Add(slot);
                _rowsPanel.Children.Add(slot.Root);
            }
            while (_slots.Count > show)
            {
                var last = _slots[_slots.Count - 1];
                _slots.RemoveAt(_slots.Count - 1);
                _rowsPanel.Children.Remove(last.Root);
            }
            for (int i = 0; i < show; i++)
            {
                _slots[i].SetRowWidth(width);
                _slots[i].Update(rows[i]);
            }
        }

        /// Place beside the host, anchored to the row, via the Core HoverPlacement guarantee, then
        /// show. Call Update() first so Width/MeasuredHeight reflect the content.
        public void ShowAt(HoverRect host, HoverRect anchor)
        {
            var pos = HoverPlacement.Compute(
                host, anchor,
                Width, MeasuredHeight,
                SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight,
                ContentTopInset, ContentBottomInset,
                gap: 8, preferLeft: true, growUp: true);
            Left = pos.Left;
            Top = pos.Top;
            if (!IsVisible) Show();
        }

        /// Window bottom edge → last row's bottom edge (bottom padding + border). Grow-up aligns
        /// the last ROW, not the card edge, to the hovered row's bottom.
        public double ContentBottomInset => CardPadding + CardBorder;

        /// Window top edge → first row's top edge (border + padding + header band). Grow-down aligns
        /// the first ROW to the hovered row's top.
        public double ContentTopInset => CardBorder + CardPadding + VisualStyle.DefaultRowHeight;

        /// The card's rendered height for the current content — measured, since the window is not
        /// shown yet, so the caller can bottom-align it before ShowAt().
        public double MeasuredHeight
        {
            get
            {
                _content.Measure(new Size(_content.Width, double.PositiveInfinity));
                return _content.DesiredSize.Height + 2 * (CardPadding + CardBorder);
            }
        }

        /// The width that fits the widest row (name inset + name + value/percent columns + right
        /// inset + border) and the title, floored at the source window's width and capped. Row
        /// column widths mirror MeterRowVisual so the reserve is exact — a name gets its full space.
        private double ComputeWidth(string titleText, List<MeterRow> rows, int show)
        {
            double hr = _style.HeightRatio;
            double numberWidth = MeterColumns.NumberWidth(_style, _style.RowText);
            double percentWidth = MeterColumns.PercentWidth(_style, _style.RowText * 11.0 / 13.0);
            double trailingCluster = 2 * MeterColumns.ColumnGap + numberWidth + percentWidth;
            double rowChrome = 8 * hr /*name inset*/ + 10 /*name→numbers gap*/ + trailingCluster + 8 * hr /*right inset*/ + 2 /*border*/;

            double widest = _style.RowWidth;   // floor: never narrower than the source window
            for (int i = 0; i < show; i++)
                widest = Math.Max(widest, MeasureText(rows[i].Name, _style.RowText) + rowChrome);
            widest = Math.Max(widest, MeasureText(titleText, _style.RowText) + 16 /*title L+R margin*/);

            return Math.Ceiling(Math.Min(widest, MaxWidth));
        }

        private double MeasureText(string text, double fontSize)
        {
            var probe = new TextBlock { Text = text ?? "" };
            _style.ApplyFont(probe, fontSize);
            probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return probe.DesiredSize.Width;
        }
    }
}
```

- [ ] **Step 2: Sanity-check Core still builds (the file references a new Core namespace)**

Run: `dotnet build src/eq2auras.Core/eq2auras.Core.csproj`
Expected: PASS (confirms `Eq2Auras.Core.Overlay` compiles standalone; the WPF `HoverCard` itself is compile-verified in CI, §Verification).

- [ ] **Step 3: Commit**

```bash
git add src/eq2auras.Plugin/Overlay/HoverCard.cs
git commit -m "Hover surface: HoverCard — module-agnostic WPF card, places via Core HoverPlacement"
```

---

## Task 3: Meter hover lifecycle + placeholder content (transcribe)

**Files:**
- Modify: `src/eq2auras.Plugin/Overlay/MeterWindow.cs`

**Interfaces:**
- Consumes: `HoverCard` (Task 2), `HoverRect` (Task 1), `MetricRegistry.ResolvePrimary`, `MeterFamilyColors.ArgbFor`, `MeterRow`/`SecondaryValue`, `MeterRowVisual.CurrentRow`/`.Root`, the window's `_style`/`_opacity`/`_metricKey`/`_drilledCombatant` fields.
- Produces: `public void HideHover()` (called from `OnClosed`); the row enter/leave behavior.

- [ ] **Step 1: Add the `using` for the Core placement DTOs**

In `src/eq2auras.Plugin/Overlay/MeterWindow.cs`, add to the `using` block (after `using Eq2Auras.Core.Meter;`):

```csharp
using Eq2Auras.Core.Overlay;
```

- [ ] **Step 2: Add the hover state fields**

Below `private List<MeterRow> _currentRows;` (the last field in the field block), add:

```csharp
        private string _hoverCombatant;    // list-mode row currently hovered, or null
        private MeterRowVisual _hoverSlot;  // the hovered slot — its screen rect anchors the card
        private HoverCard _hover;           // the by-row hover card (recreated per appearance)
```

- [ ] **Step 3: Wire mouse-enter/leave onto each slot**

In `RenderSlots()`, in the `while (_slots.Count < visible)` block, immediately after the existing `slot.Root.MouseLeftButtonUp += …;` handler and before `_slots.Add(slot);`, add:

```csharp
                slot.Root.MouseEnter += (s, e) => OnRowHoverEnter(slot);
                slot.Root.MouseLeave += (s, e) => OnRowHoverLeave();
```

- [ ] **Step 4: Add the lifecycle + placeholder methods**

Add these methods to the class (e.g. just before `private double ReservedRowsHeight()`):

```csharp
        // ─── The hover surface (SPEC Part I §The hover surface) ───────────────────────────
        // List-mode row mouseover → a floating card beside the window, anchored to the row, placed
        // by Core HoverPlacement. Fed PLACEHOLDER content — the real by-target data path is a later
        // slice (SPEC §Reserved seams); it will replace PlaceholderRows() only.

        private void OnRowHoverEnter(MeterRowVisual slot)
        {
            if (_drilledCombatant != null) return;          // list mode only
            var row = slot?.CurrentRow;
            if (row == null || string.IsNullOrEmpty(row.Name)) return;
            if (row.Name == _hoverCombatant) return;        // already the hovered row
            _hoverCombatant = row.Name;
            _hoverSlot = slot;
            ShowHoverCard(row.Name);
        }

        private void OnRowHoverLeave()
        {
            if (_hoverCombatant == null) return;
            _hoverCombatant = null;
            _hoverSlot = null;
            HideHover();
        }

        /// Recreate the card fresh each appearance: a reused hidden WPF window flashes its stale
        /// composited frame on the next show before Update() re-renders, so a fresh one only ever
        /// composites the current content.
        private void ShowHoverCard(string combatant)
        {
            HideHover();
            _hover = new HoverCard(_style, _opacity);
            _hover.Update(combatant + " — by target", PlaceholderRows());
            _hover.ShowAt(HostRect(), AnchorRect());
        }

        public void HideHover()
        {
            _hover?.Close();
            _hover = null;
        }

        private HoverRect HostRect()
            => new HoverRect { Left = Left, Top = Top, Width = Width, Height = ActualHeight };

        /// The hovered row's screen rect (DIPs). WindowStyle.None + AllowsTransparency ⇒ the window's
        /// root visual origin == its screen top-left, so a point transformed into this window's space
        /// plus Top is a screen DIP. Falls back to the meter's own bottom row band if the transform
        /// isn't available yet.
        private HoverRect AnchorRect()
        {
            double top = Top + ActualHeight - _style.RowHeight;
            double height = _style.RowHeight;
            var rowRoot = _hoverSlot?.Root as FrameworkElement;
            if (rowRoot != null)
            {
                try
                {
                    var t = rowRoot.TransformToAncestor(this);
                    top = Top + t.Transform(new Point(0, 0)).Y;
                    height = rowRoot.ActualHeight;
                }
                catch { /* not in the tree yet — keep the fallback */ }
            }
            return new HoverRect { Left = Left, Top = top, Width = Width, Height = height };
        }

        /// Placeholder content while the real by-target data path is designed (SPEC §The hover
        /// surface — "fed placeholder content"). Three sample rows in the window's family color,
        /// enough to exercise width, bars, and placement.
        private List<MeterRow> PlaceholderRows()
        {
            var metric = MetricRegistry.ResolvePrimary(_metricKey);
            int fill = metric != null ? MeterFamilyColors.ArgbFor(metric.Category) : unchecked((int)0xFFE05A5A);
            return new List<MeterRow>
            {
                PlaceholderRow("Sample target A", "2.66K", "54%", 1.00, fill),
                PlaceholderRow("Sample target B", "1.33K", "27%", 0.50, fill),
                PlaceholderRow("Sample target C", "935",   "19%", 0.35, fill),
            };
        }

        private static MeterRow PlaceholderRow(string name, string value, string percent, double bar, int fill)
            => new MeterRow
            {
                Name = name,
                FormattedValue = value,
                FormattedPercent = percent,
                BarFraction = bar,
                FillArgb = fill,
                Secondaries = new List<SecondaryValue>(),
            };
```

- [ ] **Step 5: Close the card on window close**

In `OnClosed`, add `_hover?.Close();` alongside `_settings?.Close();`:

```csharp
        protected override void OnClosed(EventArgs e)
        {
            _settings?.Close();
            _hover?.Close();
            base.OnClosed(e);
        }
```

- [ ] **Step 6: Commit**

```bash
git add src/eq2auras.Plugin/Overlay/MeterWindow.cs
git commit -m "Hover surface: meter hover lifecycle + placeholder content (row mouseover → HoverCard)"
```

---

## Verification

The Plugin (WPF) does not build on the Mac, so its gate is CI + the box, per §Global Constraints.

- [ ] **Push the branch; the verify-only CI compiles the WPF plugin.**

```bash
git push -u origin hover-card
```

Watch: `gh run watch <id> --exit-status --interval 20`
Expected: **Run Core unit tests** ✓ (prior green + 9) and **Build the plugin (MSBuild)** ✓. Publish is skipped (branch, not `main`).

- [ ] **Fix any compile errors** surfaced by CI (transcribe fixes only), re-push, re-watch until green.

- [ ] **On-box field script** (the SPEC §Testing strategy (Parse Meter — hover surface) merge-gate, run after the dev release below): hover a meter row → a distinct floating card opens beside the window (default **left**, flipping **right** only when left would clip) with the **placeholder** rows, its width fitting the content, inheriting the window's row height/font/opacity, header band at the window's header height; it **grows up** with its bottom row level with the hovered row, and near the **top** edge **grows down** with its first row level instead; moving to another row re-anchors it with **no stale flash**; it never renders off any screen edge; left- and right-click still land on the row underneath (click-through preserved). Light timer sanity check (this slice re-extracts nothing from the shared substrate).

- [ ] **Once CI is green, push to `main` for the dev release** (authorized by the owner for this branch). The `dev-latest` prerelease republishes from `main`, superseding the manual spike build with the formalized version; the on-box field script above is then the merge-gate confirmation.

---

## Self-Review Notes

- **Spec coverage:** §The hover surface (Part I) — `HoverPlacement` (Task 1, Core geometry + safeguard) and `HoverCard` (Task 2, module-agnostic card, measures/draws/asks-Core); §Reserved seams / §Row drill-down (mouseover surface built, placeholder-fed) — Task 3 (lifecycle + `PlaceholderRows`); §Assembly split (Core `HoverPlacement`, Plugin card + lifecycle) — Tasks 1–3; §Slice map (this design) — the whole plan; Testing strategy (hover surface) — Task 1 tests + §Verification field script. No spec requirement is left unimplemented.
- **Placeholder scan:** every code step shows complete code; the "placeholder content" is a deliberate spec'd deliverable (`PlaceholderRows`), not a plan gap.
- **Type consistency:** `HoverRect`/`HoverPoint`/`HoverPlacement.Compute` signatures match across Task 1 (defined), Task 2 (`ShowAt` calls `Compute`; builds nothing — passes its own `Width`/`MeasuredHeight`/insets), and Task 3 (`HostRect`/`AnchorRect` build `HoverRect`s, `ShowHoverCard` calls `Update` then `ShowAt`). `MeterRow` fields used (`Name`, `FormattedValue`, `FormattedPercent`, `BarFraction`, `FillArgb`, `Secondaries`) match `MeterFrame.cs`. `HoverCard` is `internal` (same assembly as `MeterWindow`).
- **No async data channel:** confirmed — Task 3 builds content synchronously; `OverlayHost`/`EncounterProbe`/`MeterWindowCallbacks`/`Eq2AurasPlugin` are untouched.
