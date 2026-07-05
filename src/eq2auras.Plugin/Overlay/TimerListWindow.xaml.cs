using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Eq2Auras.Core.Config;
using Eq2Auras.Core.Timers;

namespace Eq2Auras.Plugin.Overlay
{
    public partial class TimerListWindow : Window
    {
        private const double WindowSlack = 10;

        // Retained visuals keyed by timer identity — updated, never rebuilt, so the
        // drain animations run continuously at display refresh.
        private readonly Dictionary<string, TimerRowVisual> _rows = new Dictionary<string, TimerRowVisual>();
        private readonly Grid _moveChrome;
        private readonly Action<double, double> _persistPosition;
        private VisualStyle _style;
        private GrowDirection _growDirection;
        private bool _dragging;

        public TimerListWindow(string moveLabel, double left, double top, VisualStyle style,
            GrowDirection grow, Action<double, double> persistPosition)
        {
            InitializeComponent();
            _growDirection = grow;
            // Initial Top = the stored ANCHOR for both directions: the first
            // SizeChanged compensates the full initial height under Up, landing the
            // bottom edge on the anchor (SPEC §Window growth).
            Left = left;
            Top = top;
            _style = style;
            Width = style.RowWidth + WindowSlack;
            _persistPosition = persistPosition;

            _moveChrome = MoveChrome.Build(moveLabel);
            RootGrid.Children.Add(_moveChrome);

            SourceInitialized += (s, e) => ClickThrough.Set(this, true);
            MouseLeftButtonDown += OnDragStart;
            SizeChanged += OnSizeChanged;
        }

        /// The persisted vertical coordinate (SPEC §Window growth): the edge that
        /// doesn't move — top when growing down, bottom when growing up.
        public double AnchorY => _growDirection == GrowDirection.Up ? Top + ActualHeight : Top;

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Growing up = bottom edge anchored: compensate Top by the height delta.
            // Suppressed mid-drag (DragMove owns Top then); drag-end persists whatever
            // the user chose, which IS the reconciliation.
            if (_growDirection != GrowDirection.Up || _dragging) return;
            Top -= e.NewSize.Height - e.PreviousSize.Height;
        }

        /// Knob flip: converts and persists the anchored edge from the window's actual
        /// on-screen geometry — even from a null stored position. The knob changes how
        /// the window GROWS, never where it IS (SPEC §Window growth).
        public void SetGrowDirection(GrowDirection direction)
        {
            if (direction == _growDirection) return;
            _growDirection = direction;
            _persistPosition(Left, AnchorY);
        }

        public void SetMoveMode(bool moving)
        {
            _moveChrome.Visibility = moving ? Visibility.Visible : Visibility.Collapsed;
            ClickThrough.Set(this, !moving);
        }

        /// Knob change (font/dimensions): drop the retained visuals once; the next tick
        /// recreates them under the new style. Pulses/drains restart once — accepted.
        public void SetStyle(VisualStyle style)
        {
            _style = style;
            Width = style.RowWidth + WindowSlack;
            _rows.Clear();
            RowsPanel.Children.Clear();
        }

        private void OnDragStart(object sender, MouseButtonEventArgs e)
        {
            if (_moveChrome.Visibility != Visibility.Visible) return;
            _dragging = true;
            DragMove();                          // blocks until the button is released
            _dragging = false;
            _persistPosition(Left, AnchorY);     // anchored edge, not raw Top; crash-safe
        }

        /// Called on the overlay's dispatcher thread with a fresh sorted snapshot.
        public void RenderRows(List<TimerRow> rows)
        {
            var seen = new HashSet<string>();
            RowsPanel.Children.Clear();   // same element instances re-added in sort order — animations continue
            foreach (var row in rows)
            {
                var key = row.Name + "|" + row.Combatant;
                seen.Add(key);
                if (!_rows.TryGetValue(key, out var visual))
                {
                    visual = new TimerRowVisual(_style);
                    _rows[key] = visual;
                }
                visual.Update(row);
                RowsPanel.Children.Add(visual.Root);
            }

            foreach (var stale in _rows.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                _rows.Remove(stale);
            }
        }
    }
}
