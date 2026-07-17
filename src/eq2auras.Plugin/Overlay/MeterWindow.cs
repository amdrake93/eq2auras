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
        private readonly Action _onNewWindow;
        private readonly Action _onCloseWindow;
        private readonly Func<bool> _canClose;
        private MenuItem _lockItem;
        private readonly StackPanel _rowsPanel;
        private readonly TextBlock _durationText;
        private readonly TextBlock _titleText;
        private readonly TextBlock _metricText;
        private readonly TextBlock _totalText;
        private readonly ContextMenu _menu;
        private string _metricKey;
        private bool _locked;

        public MeterWindow(double left, double top, VisualStyle style, string metricKey, bool locked,
            Action<double, double> persistPosition, Action<string> onMetricPicked, Action<bool> onLockChanged,
            Action onNewWindow, Action onCloseWindow, Func<bool> canClose)
            : base(left, top, GrowDirection.Down, persistPosition, clickThroughBaseline: false)
        {
            _style = style;
            _metricKey = MetricRegistry.Resolve(metricKey).Key;   // normalize unknown -> default
            _locked = locked;
            _onMetricPicked = onMetricPicked;
            _onLockChanged = onLockChanged;
            _onNewWindow = onNewWindow;
            _onCloseWindow = onCloseWindow;
            _canClose = canClose;

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

            // Left cluster: duration | title | metric — the TITLE column is the only
            // flexible one (star), so a long EQ2 mob name trims to an ellipsis while
            // duration and metric stay fixed and visible (SPEC Part III §Header).
            var leftGrid = new Grid { VerticalAlignment = VerticalAlignment.Center };
            leftGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            leftGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            leftGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(_durationText, 0);
            Grid.SetColumn(_titleText, 1);
            Grid.SetColumn(_metricText, 2);
            leftGrid.Children.Add(_durationText);
            leftGrid.Children.Add(_titleText);
            leftGrid.Children.Add(_metricText);

            var affordance = HeaderBlock(style, dim: true);
            affordance.Text = " ⋯";   // ⋯ — hints the right-click menu (SPEC Part III §Header)
            var rightPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10 * hr, 0, 0, 0)   // breathing room between the metric label and the total
            };
            rightPanel.Children.Add(_totalText);
            rightPanel.Children.Add(affordance);

            // Outer: left cluster (star, bounds the title) | right cluster (auto,
            // total + affordance always visible). This is what stops "— DPS" and the
            // total from overlapping when the title is long.
            var headerGrid = new Grid { Margin = new Thickness(8 * hr, 0, 8 * hr, 0) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(leftGrid, 0);
            Grid.SetColumn(rightPanel, 1);
            headerGrid.Children.Add(leftGrid);
            headerGrid.Children.Add(rightPanel);

            var header = new Border
            {
                Height = style.RowHeight,
                Margin = new Thickness(0, 0, 0, style.RowSpacing),
                CornerRadius = new CornerRadius(4 * hr),
                // A real background — a transparent surface would be mouse-invisible,
                // and the header IS the drag/menu hit target. Shared with the row
                // backplate so they can't drift (SPEC Part III §Meter display defaults).
                Background = new SolidColorBrush(OverlayTheme.MeterBackplate),
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

        private void SyncMenuChecks()
        {
            foreach (var entry in _menu.Items)
            {
                if (entry is MenuItem item && item.Tag is string key) item.IsChecked = key == _metricKey;
            }
            _lockItem.IsChecked = _locked;
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
