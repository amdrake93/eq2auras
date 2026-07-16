using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Eq2Auras.Core.Config;
using Eq2Auras.Core.Meter;

namespace Eq2Auras.Plugin.Overlay
{
    /// The Parse Meter window (SPEC Part III §The meter window): INTERACTIVE — never
    /// click-through — with the header as the interaction surface: drag handle,
    /// state display ((duration) title — metric | total), and right-click menu
    /// (metric picker + lock). Lock freezes geometry only; content stays clickable.
    /// The mouse wheel scrolls the rank window (Details' model — no scrollbar chrome;
    /// works while locked: scrolling is content, not geometry). The timer module's
    /// move mode does not govern this window.
    public sealed class MeterWindow : OverlayWindowBase
    {
        public const int VisibleRows = 10;   // view constant: slot count; the frame always carries every ally
        private const double WindowSlack = 10;

        private readonly List<MeterRowVisual> _slots = new List<MeterRowVisual>();
        private MeterFrame _lastFrame;
        private int _scrollOffset;           // transient view state — never persisted, clamps to the data
        private readonly VisualStyle _style;
        private readonly Action<string> _onMetricPicked;
        private readonly Action<bool> _onLockChanged;
        private readonly StackPanel _rowsPanel;
        private readonly TextBlock _durationText;
        private readonly TextBlock _titleText;
        private readonly TextBlock _metricText;
        private readonly TextBlock _totalText;
        private readonly ContextMenu _menu;
        private string _metricKey;
        private bool _locked;

        public MeterWindow(double left, double top, VisualStyle style, string metricKey, bool locked,
            Action<double, double> persistPosition, Action<string> onMetricPicked, Action<bool> onLockChanged)
            : base(left, top, GrowDirection.Down, persistPosition, clickThroughBaseline: false)
        {
            _style = style;
            _metricKey = MetricRegistry.Resolve(metricKey).Key;   // normalize unknown -> default
            _locked = locked;
            _onMetricPicked = onMetricPicked;
            _onLockChanged = onLockChanged;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height;
            Width = style.RowWidth + WindowSlack;

            double hr = style.HeightRatio;
            _durationText = HeaderBlock(style, dim: true);
            _titleText = HeaderBlock(style, dim: false);
            _titleText.FontWeight = FontWeights.SemiBold;
            _titleText.TextTrimming = TextTrimming.CharacterEllipsis;
            _metricText = HeaderBlock(style, dim: true);
            _totalText = HeaderBlock(style, dim: false);
            _totalText.FontWeight = FontWeights.SemiBold;

            var leftPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            leftPanel.Children.Add(_durationText);
            leftPanel.Children.Add(_titleText);
            leftPanel.Children.Add(_metricText);

            var affordance = HeaderBlock(style, dim: true);
            affordance.Text = " ⋯";   // ⋯ — hints the right-click menu (SPEC Part III §Header)
            var rightPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            rightPanel.Children.Add(_totalText);
            rightPanel.Children.Add(affordance);

            var headerGrid = new Grid { Margin = new Thickness(8 * hr, 0, 8 * hr, 0) };
            headerGrid.Children.Add(leftPanel);
            headerGrid.Children.Add(rightPanel);

            var header = new Border
            {
                Height = style.RowHeight,
                Margin = new Thickness(0, 0, 0, style.RowSpacing),
                CornerRadius = new CornerRadius(4 * hr),
                // A real background — a transparent surface would be mouse-invisible,
                // and the header IS the drag/menu hit target.
                Background = new SolidColorBrush(Color.FromArgb(224, 18, 20, 26)),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(OverlayTheme.CalmBorder),
                Child = headerGrid
            };
            header.MouseLeftButtonDown += OnHeaderDrag;
            MouseWheel += OnScroll;   // window-wide: header and rows both scroll

            _menu = BuildMenu();
            SyncMenuChecks();             // AFTER the field assignment — the sync walks _menu.Items
            header.ContextMenu = _menu;   // WPF opens it on right-click

            _rowsPanel = new StackPanel();
            var root = new StackPanel { Width = style.RowWidth };
            root.Children.Add(header);
            root.Children.Add(_rowsPanel);
            Content = root;
        }

        private TextBlock HeaderBlock(VisualStyle style, bool dim)
        {
            var block = new TextBlock
            {
                Foreground = new SolidColorBrush(dim
                    ? Color.FromArgb(255, 0x8B, 0x93, 0xA3)
                    : OverlayTheme.Text),
                VerticalAlignment = VerticalAlignment.Center
            };
            style.ApplyFont(block, style.RowText);
            return block;
        }

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
            var lockItem = new MenuItem { Header = "Lock window", IsCheckable = true };
            lockItem.Click += (s, e) =>
            {
                _locked = ((MenuItem)s).IsChecked;
                _onLockChanged(_locked);
            };
            menu.Items.Add(lockItem);
            return menu;   // no sync here — _menu is still null until the ctor assigns it
        }

        private void SyncMenuChecks()
        {
            foreach (var entry in _menu.Items)
            {
                if (entry is MenuItem item && item.Tag is string key) item.IsChecked = key == _metricKey;
                else if (entry is MenuItem lockItem && lockItem.Tag == null) lockItem.IsChecked = _locked;
            }
        }

        private void OnHeaderDrag(object sender, MouseButtonEventArgs e)
        {
            if (_locked) return;   // lock freezes geometry ONLY — the menu keeps working
            BeginDragAndPersist();
        }

        /// One wheel notch = one rank. No lock check — scrolling is content
        /// interaction, same side of the lock axis as the menu (SPEC Part III).
        private void OnScroll(object sender, MouseWheelEventArgs e)
        {
            if (_lastFrame == null) return;
            _scrollOffset += e.Delta < 0 ? 1 : -1;
            RenderSlots();   // immediate re-bind from the retained frame — no waiting for the next poll
        }

        /// Called on the overlay's dispatcher thread with a fresh frame. Slot-keyed
        /// pool: visual i shows rank (_scrollOffset + i); grow with fade-in, shrink
        /// with fade-out; the offset clamps to the data on every render.
        public void Render(MeterFrame frame)
        {
            _lastFrame = frame;
            _durationText.Text = "(" + frame.DurationText + ") ";
            _titleText.Text = frame.Title;
            _metricText.Text = (frame.Title.Length > 0 ? " — " : "") + frame.MetricLabel;
            _totalText.Text = frame.TotalText;

            RenderSlots();
        }

        private void RenderSlots()
        {
            var rows = _lastFrame.Rows;
            int total = rows.Count;
            _scrollOffset = Math.Max(0, Math.Min(_scrollOffset, total - VisibleRows));   // <= 10 allies -> always 0
            int visible = Math.Min(VisibleRows, total);

            while (_slots.Count < visible)
            {
                var slot = new MeterRowVisual(_style);
                _slots.Add(slot);
                _rowsPanel.Children.Add(slot.Root);
                slot.FadeIn();
            }
            while (_slots.Count > visible)
            {
                var last = _slots[_slots.Count - 1];
                _slots.RemoveAt(_slots.Count - 1);
                last.FadeOutAndRemove(_rowsPanel);
            }

            for (int i = 0; i < visible; i++)
            {
                _slots[i].Update(rows[_scrollOffset + i]);
            }
        }
    }
}
