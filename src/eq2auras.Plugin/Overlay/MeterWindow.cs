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
        public const int DefaultVisibleRows = 10;   // null config -> this
        private int _visibleRows;                    // per-window slot count; the frame always carries every ally

        private readonly List<MeterRowVisual> _slots = new List<MeterRowVisual>();
        private MeterFrame _lastFrame;
        private int _scrollOffset;           // transient view state — never persisted, clamps to the data
        private VisualStyle _style;
        private readonly MeterWindowCallbacks _cb;
        private MenuItem _lockItem;
        private double _opacity;
        private SolidColorBrush _headerBackplate;
        private TextBlock _affordance;
        private MeterSettingsWindow _settings;
        private readonly StackPanel _rowsPanel;
        private StackPanel _root;
        private System.Windows.Shapes.Rectangle _rightGrip;
        private System.Windows.Shapes.Rectangle _bottomGrip;
        private bool _resizing;
        private Point _resizeStart;
        private double _startWidth;
        private int _startVisibleRows;
        private readonly TextBlock _durationText;
        private readonly TextBlock _titleText;
        private readonly TextBlock _metricText;
        private readonly TextBlock _totalText;
        private readonly ContextMenu _menu;
        private string _metricKey;
        private string _secondaryKey;   // null = None; the settings dropdown's current value
        private bool _locked;

        public MeterWindow(double left, double top, VisualStyle style, string metricKey, string secondaryKey, bool locked, double opacity, int visibleRows,
            MeterWindowCallbacks callbacks)
            : base(left, top, GrowDirection.Down, callbacks.PersistPosition, clickThroughBaseline: false)
        {
            _cb = callbacks;
            _style = style;
            _metricKey = MetricRegistry.Resolve(metricKey).Key;   // normalize unknown -> default
            _secondaryKey = secondaryKey;                         // null/unknown -> None (no Resolve; off, not DPS)
            _locked = locked;
            _opacity = opacity;
            _visibleRows = visibleRows;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height;
            Width = style.RowWidth;   // no horizontal slack: the right edge = the visible edge, so the resize grip sits on it (matches the bottom)

            double hr = 1.0;   // header stays default-proportioned; the row-height knob thickens data rows only (SPEC Part III §Configuration)
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

            _affordance = HeaderBlock(style, dim: true);
            var affordance = _affordance;
            affordance.Text = " ⚙";   // ⚙ — opens the settings window (SPEC Part III §Header)
            affordance.Cursor = System.Windows.Input.Cursors.Hand;
            affordance.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;   // don't let the header drag fire under the cog
                OpenSettings();
            };
            // Total is a fixed-width right-aligned column matching the row's value column,
            // so it caps the column it sums (SPEC Part III §Header). The cog leads the
            // header to free the right edge for that alignment.
            _totalText.Width = MeterColumns.NumberWidth(style, style.RowText);
            _totalText.TextAlignment = TextAlignment.Right;

            // Outer header: [cog (auto)] [ (dur) title — metric (star, ellipsis-trims) ] [total (auto)].
            // The cog leads so the right edge belongs to the total↔value-column alignment; leftGrid
            // (the duration/title/metric cluster) survives unchanged, re-columned to index 1.
            var headerGrid = new Grid { Margin = new Thickness(8 * hr, 0, 8 * hr, 0) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });          // cog
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });  // (dur) title — metric
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });          // total
            Grid.SetColumn(affordance, 0);
            Grid.SetColumn(leftGrid, 1);
            Grid.SetColumn(_totalText, 2);
            affordance.Margin = new Thickness(0, 0, 6 * hr, 0);   // gap between cog and the duration
            headerGrid.Children.Add(affordance);
            headerGrid.Children.Add(leftGrid);
            headerGrid.Children.Add(_totalText);

            var header = new Border
            {
                Height = VisualStyle.DefaultRowHeight,
                Margin = new Thickness(0, 0, 0, style.RowSpacing),
                CornerRadius = new CornerRadius(4 * hr),
                // A real background — a transparent surface would be mouse-invisible,
                // and the header IS the drag/menu hit target. Shared with the row
                // backplate so they can't drift (SPEC Part III §Meter display defaults).
                Background = _headerBackplate = new SolidColorBrush(OverlayTheme.MeterBackplate),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(OverlayTheme.CalmBorder),
                Child = headerGrid
            };
            header.MouseLeftButtonDown += OnHeaderDrag;
            MouseWheel += OnScroll;   // window-wide: header and rows both scroll

            _menu = BuildMenu();
            SyncMenuChecks();             // AFTER the field assignment — the sync walks _menu.Items
            header.ContextMenu = _menu;   // WPF opens it on right-click
            _headerBackplate.Opacity = _opacity;

            _rowsPanel = new StackPanel();
            _root = new StackPanel { Width = style.RowWidth };
            _root.Children.Add(header);
            _root.Children.Add(_rowsPanel);

            // Transparent edge grips (right = width, bottom = visible rows). Top-left is
            // anchored — the window never moves during resize, so GetPosition(this) is a
            // stable DIP reference (SPEC Part III §The meter window). Reposition via header.
            _rightGrip = new System.Windows.Shapes.Rectangle
            {
                Width = 10,
                Fill = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Stretch,
                Cursor = Cursors.SizeWE,
            };
            _bottomGrip = new System.Windows.Shapes.Rectangle
            {
                Height = 10,
                Fill = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Bottom,
                Cursor = Cursors.SizeNS,
            };
            WireResize(_rightGrip, horizontal: true);
            WireResize(_bottomGrip, horizontal: false);

            var contentGrid = new Grid();
            contentGrid.Children.Add(_root);
            contentGrid.Children.Add(_rightGrip);
            contentGrid.Children.Add(_bottomGrip);
            Content = contentGrid;

            UpdateGrips();   // gate on the initial lock state
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
                    _cb.MetricPicked(key);
                };
                menu.Items.Add(item);
            }
            menu.Items.Add(new Separator());
            _lockItem = new MenuItem { Header = "Lock window", IsCheckable = true };
            _lockItem.Click += (s, e) =>
            {
                _locked = _lockItem.IsChecked;
                UpdateGrips();
                _cb.LockChanged(_locked);
            };
            menu.Items.Add(_lockItem);

            menu.Items.Add(new Separator());
            var newItem = new MenuItem { Header = "New meter window" };
            newItem.Click += (s, e) => _cb.NewWindow();
            menu.Items.Add(newItem);
            var closeItem = new MenuItem { Header = "Close this window" };
            closeItem.Click += (s, e) => _cb.CloseWindow();
            menu.Items.Add(closeItem);

            // The last window can't close (SPEC Part III §Multiple windows) — the tab
            // toggle is the master off-switch. Evaluated on open so it tracks the live count.
            menu.Opened += (s, e) => closeItem.IsEnabled = _cb.CanClose();

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

            // Per-item, NOT an ItemContainerStyle: a MenuItem-targeted style gets applied to
            // the Separators too and throws at render time ("style intended for MenuItem
            // cannot be applied to Separator"). Style the MenuItems directly; leave separators.
            foreach (var entry in menu.Items)
            {
                if (entry is MenuItem item) item.Foreground = new SolidColorBrush(OverlayTheme.Text);
            }
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
            _scrollOffset = Math.Max(0, Math.Min(_scrollOffset, total - _visibleRows));   // <= _visibleRows allies -> always 0
            int visible = Math.Min(_visibleRows, total);

            while (_slots.Count < visible)
            {
                var slot = new MeterRowVisual(_style, _opacity);
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

        private void OpenSettings()
        {
            if (_settings != null)
            {
                _settings.Activate();
                return;
            }
            _settings = new MeterSettingsWindow(_style.RowHeight, SetRowHeight, _opacity, SetOpacity,
                _style.Font?.Source, _style.BaseSize, SetFont, _secondaryKey, SetSecondary)
            {
                Left = Left + 20,
                Top = Top + 20,
            };
            _settings.Closed += (s, e) => _settings = null;
            _settings.Show();
        }

        /// Live opacity (SPEC Part III §Meter display defaults): applied to the header and
        /// every retained row, and persisted. Text stays at full opacity — always readable.
        public void SetOpacity(double opacity)
        {
            _opacity = opacity;
            _headerBackplate.Opacity = opacity;
            foreach (var slot in _slots) slot.SetOpacity(opacity);
            _cb.OpacityChanged(opacity);
        }

        /// Live row-height: resize every retained row in place and re-point _style so
        /// future slots build at the new height; the window's SizeToContent re-fits. Persisted.
        public void SetRowHeight(double rowHeight)
        {
            _style = new VisualStyle
            {
                RowWidth = _style.RowWidth,
                RowHeight = rowHeight,
                RadialSize = _style.RadialSize,
                RowSpacing = _style.RowSpacing,
                Font = _style.Font,
                BaseSize = _style.BaseSize,
            };
            foreach (var slot in _slots) slot.SetRowHeight(rowHeight);
            _cb.RowHeightChanged(rowHeight);
        }

        /// Live font: re-point _style (family + base size), re-stamp the header text and
        /// every retained row in place; new slots read the live _style. Persisted.
        public void SetFont(string fontFamily, double baseSize)
        {
            _style = new VisualStyle
            {
                RowWidth = _style.RowWidth,
                RowHeight = _style.RowHeight,
                RadialSize = _style.RadialSize,
                RowSpacing = _style.RowSpacing,
                Font = fontFamily != null ? new FontFamily(fontFamily) : null,
                BaseSize = baseSize,
            };
            ApplyHeaderFont();
            foreach (var slot in _slots) slot.SetFont(_style);
            _cb.FontChanged(fontFamily, baseSize);
        }

        /// Live secondary selection (SPEC Part III §Configuration): persist the per-window
        /// key; the next poll's Tick reads config.SecondaryKey and the column appears/clears
        /// from the frame data (same apply-on-next-poll path as the metric picker).
        public void SetSecondary(string key)
        {
            _secondaryKey = key;
            _cb.SecondaryPicked(key);
        }

        private void ApplyHeaderFont()
        {
            _style.ApplyFont(_durationText, _style.RowText);
            _style.ApplyFont(_titleText, _style.RowText);
            _style.ApplyFont(_metricText, _style.RowText);
            _style.ApplyFont(_totalText, _style.RowText);
            _style.ApplyFont(_affordance, _style.RowText);
            _totalText.Width = MeterColumns.NumberWidth(_style, _style.RowText);
        }

        /// Live width (right-edge drag): re-point _style, resize the root + window + every
        /// retained row in place. NOT persisted here — resize persists once at drag-end.
        private void SetRowWidth(double width)
        {
            _style = new VisualStyle
            {
                RowWidth = width,
                RowHeight = _style.RowHeight,
                RadialSize = _style.RadialSize,
                RowSpacing = _style.RowSpacing,
                Font = _style.Font,
                BaseSize = _style.BaseSize,
            };
            _root.Width = width;
            Width = width;
            foreach (var slot in _slots) slot.SetRowWidth(width);
        }

        /// Live visible-row count (bottom-edge drag): re-render at the new slot count; the
        /// window height re-fits via SizeToContent. NOT persisted here — see drag-end.
        private void SetVisibleRows(int visibleRows)
        {
            _visibleRows = visibleRows;
            if (_lastFrame != null) RenderSlots();
        }

        /// Right grip = width; bottom grip = visible-row count (snap to whole rows). Both
        /// anchor the top-left, so the window origin is fixed and GetPosition(this) is a
        /// stable reference. Live during drag; geometry persists once on release.
        private void WireResize(System.Windows.Shapes.Rectangle grip, bool horizontal)
        {
            grip.MouseLeftButtonDown += (s, e) =>
            {
                if (_locked) return;
                _resizing = true;
                _resizeStart = e.GetPosition(this);
                _startWidth = _style.RowWidth;
                // Anchor the bottom-drag to the rows actually SHOWN, not the raw cap: the
                // window sizes to min(visible-row count, ally count), so a default cap of 10
                // with 2 allies shows 2 — dragging up must start from 2 to hide a row on the
                // first row-height of travel (else you'd drag ~8 rows before anything moves).
                _startVisibleRows = _lastFrame != null
                    ? Math.Min(_visibleRows, _lastFrame.Rows.Count)
                    : _visibleRows;
                grip.CaptureMouse();
                e.Handled = true;
            };
            grip.MouseMove += (s, e) =>
            {
                if (!_resizing) return;
                var p = e.GetPosition(this);
                if (horizontal)
                {
                    SetRowWidth(ClampWidth(_startWidth + (p.X - _resizeStart.X)));
                }
                else
                {
                    int rows = ClampVisibleRows(_startVisibleRows + (int)Math.Round((p.Y - _resizeStart.Y) / _style.RowHeight));
                    if (rows != _visibleRows) SetVisibleRows(rows);
                }
            };
            grip.MouseLeftButtonUp += (s, e) =>
            {
                if (!_resizing) return;
                _resizing = false;
                grip.ReleaseMouseCapture();
                _cb.GeometryChanged(_style.RowWidth, _visibleRows);   // persist both at drag-end
            };
        }

        private static double ClampWidth(double w)
            => Math.Max(Settings.MinRowWidth, Math.Min(Settings.MaxRowWidth, w));

        private static int ClampVisibleRows(int n)
            => Math.Max(MeterSettings.MinVisibleRows, Math.Min(MeterSettings.MaxVisibleRows, n));

        /// Lock freezes geometry: grips only take the mouse when unlocked (SPEC Part III —
        /// lock freezes position + size; menu/scroll/settings still work).
        private void UpdateGrips()
        {
            _rightGrip.IsHitTestVisible = !_locked;
            _bottomGrip.IsHitTestVisible = !_locked;
        }

        protected override void OnClosed(EventArgs e)
        {
            _settings?.Close();
            base.OnClosed(e);
        }
    }
}
