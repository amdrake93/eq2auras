using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Eq2Auras.Core.Config;
using Eq2Auras.Core.Meter;

namespace Eq2Auras.Plugin.Overlay
{
    /// SPIKE / THROWAWAY (branch mouseover-spike) — the by-target hover surface POC.
    ///
    /// A borderless, topmost, CLICK-THROUGH window shown beside a MeterWindow while a row is
    /// hovered. Click-through (ClickThrough.Set + IsHitTestVisible=false) is the whole trick:
    /// the surface never captures the cursor, so the hovered row's MouseLeave still fires and
    /// left/right-click still land on the row underneath — the "it's just a tooltip, not a
    /// menu" resolution. It's a UX probe only; the real hover primitive and its data pipe get
    /// their own brainstorm later (docs/backlog.md). Nothing here is load-bearing or reviewed.
    internal sealed class MeterHoverWindow : Window
    {
        private const int MaxRows = 15;   // POC guess — caps a many-target popup's height; a UX iteration knob

        private readonly VisualStyle _style;
        private readonly double _opacity;
        private readonly StackPanel _rowsPanel;
        private readonly List<MeterRowVisual> _slots = new List<MeterRowVisual>();
        private readonly TextBlock _title;

        public MeterHoverWindow(VisualStyle style, double opacity)
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

            _title = new TextBlock { Foreground = Theme.TextLabel, Margin = new Thickness(8, 4, 8, 4) };
            style.ApplyFont(_title, style.RowText);

            _rowsPanel = new StackPanel();
            var content = new StackPanel { Width = style.RowWidth };
            content.Children.Add(_title);
            content.Children.Add(_rowsPanel);

            Content = new Border
            {
                Background = new SolidColorBrush(OverlayTheme.MeterBackplate),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(OverlayTheme.CalmBorder),
                CornerRadius = new CornerRadius(4),
                Child = content,
            };

            SourceInitialized += (s, e) => ClickThrough.Set(this, true);
        }

        /// Rebind the pooled rows to the current by-target breakdown.
        public void Update(string titleText, List<MeterRow> rows)
        {
            _title.Text = titleText;
            rows = rows ?? new List<MeterRow>();
            int show = rows.Count < MaxRows ? rows.Count : MaxRows;

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
            for (int i = 0; i < show; i++) _slots[i].Update(rows[i]);
        }
    }
}
