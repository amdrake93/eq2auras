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
    public partial class TimerListWindow : OverlayWindowBase
    {
        private const double WindowSlack = 10;

        // Retained visuals keyed by timer identity — updated, never rebuilt, so the
        // drain animations run continuously at display refresh.
        private readonly Dictionary<string, TimerRowVisual> _rows = new Dictionary<string, TimerRowVisual>();
        private readonly Grid _moveChrome;
        private VisualStyle _style;

        public TimerListWindow(string moveLabel, double left, double top, VisualStyle style,
            GrowDirection grow, Action<double, double> persistPosition)
            : base(left, top, grow, persistPosition, clickThroughBaseline: true)
        {
            InitializeComponent();
            _style = style;
            Width = style.RowWidth + WindowSlack;

            _moveChrome = MoveChrome.Build(moveLabel);
            RootGrid.Children.Add(_moveChrome);

            MouseLeftButtonDown += OnDragStart;
        }

        public void SetMoveMode(bool moving)
        {
            _moveChrome.Visibility = moving ? Visibility.Visible : Visibility.Collapsed;
            SetClickThrough(!moving);
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
            BeginDragAndPersist();
        }

        /// Called on the overlay's dispatcher thread with a fresh sorted snapshot.
        /// Row order anchors with the window (SPEC §Window growth): soonest-to-expire
        /// sits nearest the anchored edge, so grow-up reverses the visual order.
        public void RenderRows(List<TimerRow> rows)
        {
            var ordered = GrowsUp
                ? Enumerable.Reverse(rows)
                : rows;

            var seen = new HashSet<string>();
            RowsPanel.Children.Clear();   // same element instances re-added in sort order — animations continue
            foreach (var row in ordered)
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
