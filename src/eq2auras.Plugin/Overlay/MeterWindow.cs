using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Eq2Auras.Core.Config;
using Eq2Auras.Core.Meter;
using Eq2Auras.Core.Overlay;

namespace Eq2Auras.Plugin.Overlay
{
    /// The Parse Meter window (SPEC Part III §The meter window): INTERACTIVE — never
    /// click-through — with the header as the interaction surface: drag handle,
    /// state display ((duration) + primary-metric identity; total + secondary-label cells right), and right-click popup
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
        private double _opacity;
        private double _backdropOpacity;
        private Border _backdrop;
        private Grid _rowsContainer;
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
        private readonly TextBlock _metricText;          // primary metric name — the header's left identity (white)
        private readonly TextBlock _secondaryLabelText;  // secondary metric label (right cluster, subordinate grey)
        private readonly TextBlock _totalText;
        private MeterScope _scope;
        private string _metricKey;
        private string _secondaryKey;   // null = no secondary; toggled from the right-click popup
        private bool _locked;
        private string _drilledCombatant;              // null = list mode; non-null = drilled into this combatant
        private MetricBreakdownSource _drillSource;    // resolved from the metric at EnterDrill
        private string _drillMetricLabel;              // the framing metric's identity label (selection label), shown in the header
        private string _drillDeathKey;                 // set when drilled into a death (Deaths metric) — which death (Victim#Ordinal)
        private List<MeterRow> _currentRows;           // the rows the slots currently render — list OR breakdown
        private string _hoverCombatant;                // list-mode row currently hovered, or null
        private string _hoverRecapSecond;              // recap-drill: the hovered second-row's tag (DrillKey), or null
        private MeterRowVisual _hoverSlot;             // the hovered slot — its screen rect anchors the card
        private HoverCard _hover;                      // the by-row hover card (recreated per appearance)
        private SegmentSelection _selection;           // the window's live segment (Current/Zonewide/Historical) — runtime (SPEC §Segments)
        private bool _pinned;                          // PinnedToSegment: true = don't auto-return to Current
        private Border _segmentChip;                   // header segment chip (SPEC §Header) — click opens the flyout
        private TextBlock _segmentChipText;
        private TextBlock _unavailableHint;            // Zonewide-with-PopulateAll-off dormant-body hint (SPEC §Availability)
        private SegmentFlyout _segmentFlyout;          // tracked so a prior flyout is closed before reopening

        public MeterWindow(double left, double top, VisualStyle style, MeterScope scope, string metricKey, string secondaryKey, bool locked, double opacity, double backdropOpacity, int visibleRows,
            SegmentMode segmentMode, bool pinnedToSegment,
            MeterWindowCallbacks callbacks)
            : base(left, top, GrowDirection.Down, callbacks.PersistPosition, clickThroughBaseline: false)
        {
            _cb = callbacks;
            _style = style;
            _scope = scope;
            _metricKey = metricKey;   // raw: null = cleared (shows nothing); resolution is the engine's (ResolvePrimary)
            _secondaryKey = secondaryKey;                         // null/unknown -> None (no Resolve; off, not DPS)
            _locked = locked;
            _opacity = opacity;
            _backdropOpacity = backdropOpacity;
            _visibleRows = visibleRows;
            _selection = SegmentRules.FromMode(segmentMode);
            _pinned = pinnedToSegment;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height;
            Width = style.RowWidth;   // no horizontal slack: the right edge = the visible edge, so the resize grip sits on it (matches the bottom)

            // Header HEIGHT stays fixed (DefaultRowHeight, below) so the row-height knob thickens
            // data rows only — but its HORIZONTAL insets must track the rows' HeightRatio. Rows scale
            // their 8px trailing inset by HeightRatio (BarRowVisual); a header pinned at 1.0 drifts
            // its total/cog off the value/percent columns by 8*(HeightRatio-1) as rows thicken. Tracking
            // it keeps the total capping the value column at any row height (SPEC Part III §Header).
            double hr = style.HeightRatio;
            _durationText = HeaderBlock(style, dim: true);
            _metricText = HeaderBlock(style, dim: false);          // primary metric NAME — white, the header's left identity
            _metricText.FontWeight = IdentityWeight;
            _metricText.TextTrimming = TextTrimming.CharacterEllipsis;
            _secondaryLabelText = HeaderBlock(style, dim: true);   // secondary label — subordinate grey, matches the row's secondary column
            _totalText = HeaderBlock(style, dim: false);
            _totalText.FontWeight = IdentityWeight;

            // Left cluster: (duration) + primary metric NAME (the meter's identity — SPEC §Header).
            // A DockPanel docks the duration left and lets the metric name fill the remaining width
            // and ellipsis-trim to it — the layout system bounds it (column 0 is the grid's star
            // column), so no manual reserve/UpdateTitleMaxWidth is needed. The encounter/mob name is
            // gone (SPEC §Header — two title-length strings competed).
            var leftCluster = new DockPanel
            {
                LastChildFill = true,
                VerticalAlignment = VerticalAlignment.Center,
            };
            DockPanel.SetDock(_durationText, Dock.Left);
            leftCluster.Children.Add(_durationText);
            leftCluster.Children.Add(_metricText);   // last child fills column 0's remaining width and trims

            _affordance = HeaderBlock(style, dim: true);
            var affordance = _affordance;
            affordance.Text = "⚙";   // ⚙ — opens the settings window (SPEC Part III §Header). No leading space: it pushed the cog off-center in its cell.
            affordance.Cursor = System.Windows.Input.Cursors.Hand;
            affordance.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;   // don't let the header drag fire under the cog
                OpenSettings();
            };
            // Right cluster mirrors the rows' columns 1:1 (SPEC §Header): [secondary label][total] as
            // fixed NumberWidth cells over the secondary + value columns, cog in the PercentWidth slot
            // over the percent column. The primary label is NOT here — it's the left identity now.
            _totalText.Width = MeterColumns.NumberWidth(style, style.RowText);
            _totalText.TextAlignment = TextAlignment.Right;
            _totalText.Margin = new Thickness(0, 0, MeterColumns.ColumnGap, 0);          // gap to the cog
            _secondaryLabelText.Width = MeterColumns.NumberWidth(style, style.RowText);  // over the secondary column
            _secondaryLabelText.TextAlignment = TextAlignment.Right;
            _secondaryLabelText.Margin = new Thickness(0, 0, MeterColumns.ColumnGap, 0); // gap to the total
            affordance.Width = MeterColumns.PercentWidth(style, style.RowText * 11.0 / 13.0);
            affordance.TextAlignment = TextAlignment.Right;

            var metricCluster = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            metricCluster.Children.Add(_secondaryLabelText);
            metricCluster.Children.Add(_totalText);

            // The segment chip (SPEC §Header): a compact, bounded, clickable element naming the
            // window's selection; its own auto-sized column so it never steals the metric's width.
            _segmentChipText = new TextBlock
            {
                Text = "▾ " + SegmentLabel(_selection),
                FontSize = 10,
                Foreground = Theme.TextLabel,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 130,
            };
            _segmentChip = new Border
            {
                Child = _segmentChipText,
                Background = Theme.Surface(0x0D),
                BorderBrush = Theme.Divider,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(7, 1, 7, 1),
                Margin = new Thickness(6, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
            };
            // Open on DOWN with Handled — like the cog — so the header's drag handler (which captures
            // the mouse on button-down when unlocked) can never swallow the chip's click.
            _segmentChip.MouseLeftButtonDown += (s, e) => { e.Handled = true; OpenSegmentFlyout(); };

            // Outer header: [ (dur) metric (star) ] [segment chip (auto)] [secondary label + total (auto)] [cog (auto)].
            var headerGrid = new Grid { Margin = new Thickness(8 * hr, 0, 8 * hr, 0) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });  // (dur) metric
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });          // segment chip
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });          // metric cluster (total above the value column)
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });          // cog (far right, above the percent column)
            Grid.SetColumn(leftCluster, 0);
            Grid.SetColumn(_segmentChip, 1);
            Grid.SetColumn(metricCluster, 2);
            Grid.SetColumn(affordance, 3);
            headerGrid.Children.Add(leftCluster);
            headerGrid.Children.Add(_segmentChip);
            headerGrid.Children.Add(metricCluster);
            headerGrid.Children.Add(affordance);

            var header = new Border
            {
                Height = VisualStyle.DefaultRowHeight,
                Margin = new Thickness(0, 0, 0, style.RowSpacing),
                CornerRadius = new CornerRadius(4 * hr),
                // A real background — a transparent surface would be mouse-invisible,
                // and the header IS the drag/popup hit target. Shared with the row
                // backplate so they can't drift (SPEC Part III §Meter display defaults).
                Background = _headerBackplate = new SolidColorBrush(OverlayTheme.MeterBackplate),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(OverlayTheme.CalmBorder),
                Child = headerGrid
            };
            header.MouseLeftButtonDown += OnHeaderDrag;
            MouseWheel += OnScroll;   // window-wide: header and rows both scroll

            _headerBackplate.Opacity = _opacity;

            _rowsPanel = new StackPanel();
            _backdrop = new Border
            {
                Background = Theme.Surface(0xFF),                 // SurfaceTint; opacity via Border.Opacity below
                Opacity = _opacity * _backdropOpacity,            // window × backdrop (they compound, SPEC §Meter display defaults)
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            _rowsContainer = new Grid { MinHeight = ReservedRowsHeight() };
            _rowsContainer.Children.Add(_backdrop);              // behind
            _rowsContainer.Children.Add(_rowsPanel);             // on top; empty area shows the backdrop
            _unavailableHint = new TextBlock
            {
                Text = "Enable ACT’s “Zone All listing” for Zonewide",
                Foreground = Theme.TextMuted,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12),
                Visibility = Visibility.Collapsed,
            };
            _rowsContainer.Children.Add(_unavailableHint);       // over the dormant backdrop when Zonewide is unavailable
            _root = new StackPanel { Width = style.RowWidth };
            _root.Children.Add(header);
            _root.Children.Add(_rowsContainer);

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

            // Right-click = up one layer (SPEC §Row drill-down): list mode opens the popup (anchored to
            // the header, as before); drill mode returns to the list. One window-level handler so it fires
            // over the header AND the body ("right-click anywhere", SPEC §Configuration).
            contentGrid.MouseRightButtonUp += (s, e) =>
            {
                if (_drilledCombatant != null) ExitDrill();
                else OpenPopup(header);
                e.Handled = true;
            };

            UpdateGrips();   // gate on the initial lock state
        }

        private TextBlock HeaderBlock(VisualStyle style, bool dim)
        {
            var block = new TextBlock
            {
                Foreground = dim ? Theme.TextLabel : Theme.TextPrimary,
                VerticalAlignment = VerticalAlignment.Center
            };
            style.ApplyFont(block, style.RowText);
            return block;
        }

        /// A header label that vanishes when blank: an empty label — no secondary selected, or a
        /// cleared primary (no metric name, no total) — collapses so it reserves no cluster width,
        /// and a cleared primary leaves just the duration on the left and the cog on the right (SPEC §Header).
        private static void SetHeaderLabel(TextBlock block, string text)
        {
            block.Text = text ?? "";
            block.Visibility = block.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // The header identity accents (metric name, total) read SemiBold by default but honor a
        // user Bold-font choice (SPEC §Typography — style respected).
        private FontWeight IdentityWeight => _style.FontWeight == FontWeights.Bold ? FontWeights.Bold : FontWeights.SemiBold;

        /// Right-click opens the themed popup (SPEC Part III §Configuration): metric/secondary
        /// toggle-grids + lifecycle. A fresh popup per open reflects current state; toggles route
        /// through the callbacks (a cleared primary passes null — the meter shows nothing).
        private void OpenPopup(UIElement target)
        {
            var popup = new MeterPopup(target, _scope, _metricKey, _secondaryKey, _cb.CanClose, _locked, new MeterPopup.Callbacks
            {
                PrimarySelected = (scope, key) => { _scope = scope; _metricKey = key; _cb.PrimaryPicked(scope, key); },
                SecondaryToggled = SetSecondary,
                Lock = () => { _locked = !_locked; UpdateGrips(); _cb.LockChanged(_locked); },
                NewMeter = () => _cb.NewWindow(),
                RemoveMeter = () => _cb.CloseWindow(),
            });
            popup.Show();
        }

        private void OnHeaderDrag(object sender, MouseButtonEventArgs e)
        {
            if (_locked) return;   // lock freezes geometry ONLY — the popup keeps working
            BeginDragAndPersist();
        }

        /// One wheel notch = one rank. No lock check — scrolling is content
        /// interaction, same side of the lock axis as the popup (SPEC Part III).
        private void OnScroll(object sender, MouseWheelEventArgs e)
        {
            if (_currentRows == null) return;
            _scrollOffset += e.Delta < 0 ? 1 : -1;
            RenderSlots();   // immediate re-bind from the retained frame — no waiting for the next poll
        }

        /// Called on the overlay's dispatcher thread with a fresh frame. Slot-keyed
        /// pool: visual i shows rank (_scrollOffset + i); grow with fade-in, shrink
        /// with fade-out; the offset clamps to the data on every render.
        public void Render(MeterFrame frame)
        {
            _lastFrame = frame;
            _currentRows = frame.Rows;
            if (_segmentChip != null) _segmentChip.Visibility = Visibility.Visible;   // list layer shows the chip (hidden while drilled)
            if (_unavailableHint != null) _unavailableHint.Visibility = Visibility.Collapsed;
            _durationText.Text = "(" + frame.DurationText + ") ";
            SetHeaderLabel(_secondaryLabelText, frame.SecondaryLabel);
            SetHeaderLabel(_metricText, frame.MetricLabel);
            SetHeaderLabel(_totalText, frame.TotalText);

            RenderSlots();
        }

        private void RenderSlots()
        {
            var rows = _currentRows ?? new List<MeterRow>();
            int total = rows.Count;
            _scrollOffset = Math.Max(0, Math.Min(_scrollOffset, total - _visibleRows));   // <= _visibleRows allies -> always 0
            int visible = Math.Min(_visibleRows, total);

            while (_slots.Count < visible)
            {
                var slot = new MeterRowVisual(_style, _opacity);
                slot.Root.MouseLeftButtonUp += (s, e) =>
                {
                    // Left-click a combatant row drills in; left-click an ability row (drill mode)
                    // is reserved (no-op) for the future per-ability detail window (SPEC §Row drill-down).
                    if (_drilledCombatant == null)
                    {
                        EnterDrill(slot.CurrentRow);
                        e.Handled = true;
                    }
                };
                slot.Root.MouseEnter += (s, e) => OnRowHoverEnter(slot);
                slot.Root.MouseLeave += (s, e) => OnRowHoverLeave();
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

        /// The window's current drill request, or null in list mode — the host reads this to build
        /// the probe's drill-request set and to route list-vs-drill rendering (SPEC §Row drill-down).
        public DrillRequest DrillTarget => _drilledCombatant == null
            ? null
            : new DrillRequest { CombatantName = _drilledCombatant, Source = _drillSource, DeathKey = _drillDeathKey, Selection = _selection };

        /// The window's current hover request, or null when drilled / not hovering / the primary is
        /// cleared or an event metric (Deaths — no by-counterpart hover). The host reads this to build
        /// the probe's request set and to route the card's reading (SPEC §Row drill-down — the by-row
        /// mouseover). Grouping = ByCounterpart distinguishes it from the by-ability DrillTarget.
        public DrillRequest HoverTarget
        {
            get
            {
                if (_drilledCombatant != null || _hoverCombatant == null) return null;
                var metric = MetricRegistry.ResolvePrimary(_metricKey);
                if (metric == null || metric.IsEvent) return null;
                return new DrillRequest { CombatantName = _hoverCombatant, Source = metric.BreakdownSource, Grouping = BreakdownGrouping.ByCounterpart, Selection = _selection };
            }
        }

        /// Enter drill mode for a combatant (left-click a row). Resolves the framing metric, swaps
        /// the header's left identity to "‹ Name — metric" (back-hint chevron; SPEC §Header while
        /// drilled), hides the secondary-label cell, clears the body until the next poll's breakdown,
        /// and publishes the new drill state so the host requests the deep-read.
        public void EnterDrill(MeterRow row)
        {
            if (row == null) return;
            var metric = MetricRegistry.ResolvePrimary(_metricKey);
            if (metric == null) return;   // cleared primary shows no rows — nothing to drill

            // Set _metricText directly (not via SetHeaderLabel): the drill label is never empty, so
            // the helper's collapse-when-blank behavior isn't wanted here — the identity always shows.
            if (metric.IsEvent)   // Deaths → the recap drill (SPEC §Death Recap)
            {
                if (string.IsNullOrEmpty(row.DrillKey)) return;
                _drilledCombatant = row.Name;
                _drillSource = metric.BreakdownSource;   // Deaths
                _drillDeathKey = row.DrillKey;
                // Header mirrors the clicked row: "‹ Name (N) · killing blow + dmg"; time-of-death in the total cell.
                _metricText.Text = "‹ " + row.Name + " " + (row.Detail ?? "");
                _metricText.Visibility = Visibility.Visible;
                SetHeaderLabel(_secondaryLabelText, "");
                SetHeaderLabel(_totalText, row.FormattedValue);   // the death's time — the view's value, right-aligned
            }
            else                  // by-ability drill (unchanged, now with the shared middot)
            {
                if (string.IsNullOrEmpty(row.Name)) return;
                _drilledCombatant = row.Name;
                _drillSource = metric.BreakdownSource;
                _drillDeathKey = null;
                var selection = MeterSelections.Resolve(_scope, _metricKey);
                _drillMetricLabel = selection?.Label ?? metric.Label;
                _metricText.Text = "‹ " + row.Name + " · " + _drillMetricLabel;   // shared drill-header middot (SPEC §Row drill-down)
                _metricText.Visibility = Visibility.Visible;
                SetHeaderLabel(_secondaryLabelText, "");   // no secondary cell while drilled (SPEC §Header while drilled)
                SetHeaderLabel(_totalText, "");            // filled by the next RenderDrill from the combatant's own total
            }

            _currentRows = new List<MeterRow>();
            _scrollOffset = 0;
            RenderSlots();
            _hoverCombatant = null;
            _hoverSlot = null;
            HideHover();                        // a live hover card must not linger over the drilled body
            if (_segmentChip != null) _segmentChip.Visibility = Visibility.Collapsed;   // segment is a list-layer action (SPEC §Header)
            _cb.DrillChanged?.Invoke();
        }

        /// The window's live segment (SPEC §Segments). The host reads it to build the probe's
        /// segment-request set and to route each window to its own segment's snapshot.
        public SegmentSelection CurrentSelection => _selection;

        /// Apply a new segment selection — from the flyout, or the host's new-combat snap / culled
        /// fallback (Task 6). Updates the chip label and republishes so the host rebuilds its request
        /// sets. A historical pick is runtime-only; Current/Zonewide persist via the flyout callbacks.
        public void ApplySelection(SegmentSelection selection, string label)
        {
            _selection = selection ?? SegmentSelection.Current();
            if (_segmentChipText != null) _segmentChipText.Text = "▾ " + label;
            _cb.SegmentChanged?.Invoke();
        }

        private void OpenSegmentFlyout()
        {
            var listing = _cb.EnumerateSegments?.Invoke();
            if (listing == null) return;
            _segmentFlyout?.Close();   // no stale Popup left to swallow the next click
            _segmentFlyout = new SegmentFlyout(_segmentChip, _style, listing, _selection, returnToCurrent: !_pinned,
                onPick: (sel, label) =>
                {
                    ApplySelection(sel, label);
                    if (SegmentRules.ClearsKnobOnPick(sel)) { _pinned = true; _cb.PinnedChanged?.Invoke(true); }   // Zonewide pins in one gesture
                    if (sel.Kind == SegmentKind.Current) _cb.SegmentModeChanged?.Invoke(SegmentMode.Current);
                    else if (sel.Kind == SegmentKind.Zonewide) _cb.SegmentModeChanged?.Invoke(SegmentMode.Zonewide);
                    // Historical picks are runtime-only — the persisted mode is unchanged (SPEC §Segments Persistence).
                },
                onKnobToggled: knobChecked => { _pinned = !knobChecked; _cb.PinnedChanged?.Invoke(_pinned); });
            _segmentFlyout.Show();
        }

        private static string SegmentLabel(SegmentSelection sel)
        {
            switch (sel.Kind)
            {
                case SegmentKind.Zonewide: return "Zonewide";
                case SegmentKind.Historical: return "Segment";
                default: return "Current";
            }
        }

        /// Leave drill mode. Clears state and republishes; the host's next Render(listFrame) restores
        /// the list header + rows. Called by the user (right-click) or the host on auto-exit.
        public void ExitDrill()
        {
            if (_drilledCombatant == null) return;
            _drilledCombatant = null;
            _hoverRecapSecond = null;
            _hoverSlot = null;
            HideHover();                        // a recap-second hover card must not linger past the recap
            _cb.DrillChanged?.Invoke();
        }

        /// Render the drilled combatant's by-ability rows (host, each poll). The header's left identity
        /// was set at EnterDrill; here we set the combatant's own total (from the list frame's row) and
        /// swap the body. Reuses the same slot pool as the list (SPEC §Row drill-down).
        public void RenderDrill(List<MeterRow> rows, string ownTotalText)
        {
            SetHeaderLabel(_secondaryLabelText, "");
            SetHeaderLabel(_totalText, ownTotalText);
            _currentRows = rows ?? new List<MeterRow>();
            _scrollOffset = Math.Max(0, Math.Min(_scrollOffset, _currentRows.Count - _visibleRows));
            RenderSlots();
        }

        /// Zonewide selected but the current zone has no "Zone All listing" (SPEC §Availability): the
        /// dormant backdrop body + a one-line hint; the header keeps its duration, chip, and cog.
        public void RenderUnavailable()
        {
            if (_segmentChip != null) _segmentChip.Visibility = Visibility.Visible;
            _durationText.Text = "(—) ";   // keep the header's duration slot (SPEC §The meter window — dormant header)
            SetHeaderLabel(_metricText, "");
            SetHeaderLabel(_secondaryLabelText, "");
            SetHeaderLabel(_totalText, "");
            _currentRows = new List<MeterRow>();
            _scrollOffset = 0;
            RenderSlots();   // empty → the dormant backdrop shows
            if (_unavailableHint != null) _unavailableHint.Visibility = Visibility.Visible;
        }

        // ─── The hover surface (SPEC Part I §The hover surface) ───────────────────────────
        // List-mode row mouseover → the by-counterpart breakdown in a floating card beside the window,
        // anchored to the row, placed by Core HoverPlacement (SPEC §Row drill-down — the by-row
        // mouseover). Enter reads SYNCHRONOUSLY for an instant first paint (ReadHoverNow) AND publishes
        // a request (DrillChanged) so the per-poll path keeps the card live while hovered.

        private void OnRowHoverEnter(MeterRowVisual slot)
        {
            if (_drilledCombatant != null)
            {
                if (_drillDeathKey != null) OnRecapSecondHoverEnter(slot);   // recap drill → per-second hover
                return;                                                       // by-ability drill → no hover (deferred seam)
            }
            var row = slot?.CurrentRow;
            if (row == null || string.IsNullOrEmpty(row.Name)) return;
            var metric = MetricRegistry.ResolvePrimary(_metricKey);
            if (metric == null || metric.IsEvent) return;       // cleared primary / event metric (Deaths) → no hover
            if (row.Name == _hoverCombatant) return;            // already the hovered row
            HideHover();                                        // clean switch: drop the prior card
            _hoverCombatant = row.Name;
            _hoverSlot = slot;
            _cb.DrillChanged?.Invoke();                         // publish the request → keeps the card live at the poll rate
            var rows = _cb.ReadHoverNow?.Invoke(HoverTarget);   // instant first paint: synchronous read of this one combatant
            if (rows != null) RenderHover(rows);
        }

        /// Recap-drill row mouseover → that second's event log, synchronous + static (a past second's
        /// events are historical, so no poll request/refresh — SPEC §Deaths — the recap-second hover).
        private void OnRecapSecondHoverEnter(MeterRowVisual slot)
        {
            var row = slot?.CurrentRow;
            if (row == null || string.IsNullOrEmpty(row.DrillKey)) return;   // needs the second tag
            if (row.DrillKey == _hoverRecapSecond) return;                   // already this second
            if (!int.TryParse(row.DrillKey, out int second)) return;
            HideHover();
            _hoverRecapSecond = row.DrillKey;
            _hoverSlot = slot;
            var request = new DrillRequest { DeathKey = _drillDeathKey, Second = second, Grouping = BreakdownGrouping.RecapSecond };
            var rows = _cb.ReadHoverNow?.Invoke(request);
            if (rows != null) ShowHoverRows(_drilledCombatant + " · " + row.Name, rows);
        }

        private void OnRowHoverLeave()
        {
            if (_hoverSlot == null) return;
            _hoverCombatant = null;
            _hoverRecapSecond = null;
            _hoverSlot = null;
            HideHover();
            _cb.DrillChanged?.Invoke();   // drops the by-counterpart poll request; a no-op for the sync-only recap-second
        }

        /// Render the hovered combatant's by-counterpart breakdown into the card (host, each poll while
        /// hovered). Creates the card fresh on the first reading of an appearance — a reused hidden WPF
        /// window flashes its stale composited frame on re-show, so a fresh one only composites current
        /// content — then updates in place across polls. Title: "by source" for incoming metrics (who
        /// hit/healed me), "by target" otherwise (SPEC §Row drill-down — the by-row mouseover).
        public void RenderHover(List<MeterRow> rows)
        {
            if (_hoverCombatant == null) return;                 // left already
            var metric = MetricRegistry.ResolvePrimary(_metricKey);
            if (metric == null) return;
            string suffix = BreakdownDirection.IsIncoming(metric.BreakdownSource) ? " — by source" : " — by target";
            ShowHoverRows(_hoverCombatant + suffix, rows ?? new List<MeterRow>());
        }

        /// Create the card fresh per appearance (a reused hidden WPF window flashes its stale composited
        /// frame on re-show), then Update in place; place via Core HoverPlacement. Shared by the
        /// by-counterpart hover (live) and the recap-second hover (static).
        private void ShowHoverRows(string title, List<MeterRow> rows)
        {
            if (_hover == null) _hover = new HoverCard(_style, _opacity);
            _hover.Update(title, rows ?? new List<MeterRow>());
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

        /// The window reserves its configured row count as a persistent backdrop regardless of
        /// how many allies are present (SPEC §Configuration): the dark region is always this tall,
        /// so an empty meter shows its size and can be sized up past the present rows.
        private double ReservedRowsHeight() => _visibleRows * _style.RowHeight;

        private void OpenSettings()
        {
            if (_settings != null)
            {
                _settings.Activate();
                return;
            }
            double settingsLeft = Left - 310;
            if (settingsLeft < 0) settingsLeft = Left + Width + 10;   // beside the window, not over it (field-2026-08-03)
            _settings = new MeterSettingsWindow(_style.RowHeight, SetRowHeight, _opacity, SetOpacity,
                _backdropOpacity, SetBackdropOpacity,
                _style.Font?.Source, _style.BaseSize,
                _style.FontWeight == FontWeights.Bold, _style.FontStyle == FontStyles.Italic, SetFont)
            {
                Left = settingsLeft,
                Top = Top,
            };
            _settings.Closed += (s, e) => _settings = null;
            _settings.Show();
        }

        /// Window opacity (SPEC Part III §Meter display defaults): the whole window's fill/backplates
        /// — header + rows + the backdrop (which also takes backdrop opacity, compounded). Text stays
        /// at full opacity — always readable. Persisted.
        public void SetOpacity(double opacity)
        {
            _opacity = opacity;
            _headerBackplate.Opacity = opacity;
            foreach (var slot in _slots) slot.SetOpacity(opacity);
            _backdrop.Opacity = _opacity * _backdropOpacity;
            _cb.OpacityChanged(opacity);
        }

        /// Backdrop opacity (SPEC Part III §Meter display defaults): scales just the persistent
        /// backdrop, compounded with window opacity — faint backdrop + vivid bars is low here, high
        /// on the window/rows. Persisted.
        public void SetBackdropOpacity(double backdropOpacity)
        {
            _backdropOpacity = backdropOpacity;
            _backdrop.Opacity = _opacity * _backdropOpacity;
            _cb.BackdropOpacityChanged(backdropOpacity);
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
                FontWeight = _style.FontWeight,
                FontStyle = _style.FontStyle,
            };
            _rowsContainer.MinHeight = ReservedRowsHeight();
            foreach (var slot in _slots) slot.SetRowHeight(rowHeight);
            _cb.RowHeightChanged(rowHeight);
        }

        /// Live font: re-point _style (family + base size), re-stamp the header text and
        /// every retained row in place; new slots read the live _style. Persisted.
        public void SetFont(string fontFamily, double baseSize, bool bold, bool italic)
        {
            _style = new VisualStyle
            {
                RowWidth = _style.RowWidth,
                RowHeight = _style.RowHeight,
                RadialSize = _style.RadialSize,
                RowSpacing = _style.RowSpacing,
                Font = fontFamily != null ? new FontFamily(fontFamily) : null,
                BaseSize = baseSize,
                FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
                FontStyle = italic ? FontStyles.Italic : FontStyles.Normal,
            };
            ApplyHeaderFont();
            foreach (var slot in _slots) slot.SetFont(_style);
            _cb.FontChanged(fontFamily, baseSize, bold, italic);
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
            _style.ApplyFont(_metricText, _style.RowText);
            _style.ApplyFont(_secondaryLabelText, _style.RowText);
            _style.ApplyFont(_totalText, _style.RowText);
            _style.ApplyFont(_affordance, _style.RowText);
            _metricText.FontWeight = IdentityWeight;   // ApplyFont set the base weight; restore the identity accent
            _totalText.FontWeight = IdentityWeight;
            _totalText.Width = MeterColumns.NumberWidth(_style, _style.RowText);
            _totalText.Margin = new Thickness(0, 0, MeterColumns.ColumnGap, 0);
            _secondaryLabelText.Width = MeterColumns.NumberWidth(_style, _style.RowText);
            _affordance.Width = MeterColumns.PercentWidth(_style, _style.RowText * 11.0 / 13.0);
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
                FontWeight = _style.FontWeight,
                FontStyle = _style.FontStyle,
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
            _rowsContainer.MinHeight = ReservedRowsHeight();
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
                // The window reserves the full visible-row count as a backdrop (persistent,
                // §Configuration), so the bottom drag anchors to the raw cap — a size-up past the
                // present allies now works (previously it anchored to min(cap, allies) and snapped).
                _startVisibleRows = _visibleRows;
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
        /// lock freezes position + size; popup/scroll/settings still work).
        private void UpdateGrips()
        {
            _rightGrip.IsHitTestVisible = !_locked;
            _bottomGrip.IsHitTestVisible = !_locked;
        }

        protected override void OnClosed(EventArgs e)
        {
            _settings?.Close();
            _hover?.Close();
            _segmentFlyout?.Close();
            base.OnClosed(e);
        }
    }
}
