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
