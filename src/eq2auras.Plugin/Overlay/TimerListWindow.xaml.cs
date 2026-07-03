using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Eq2Auras.Core.Timers;

namespace Eq2Auras.Plugin.Overlay
{
    public partial class TimerListWindow : Window
    {
        // Retained visuals keyed by timer identity — updated, never rebuilt, so the
        // drain animations run continuously at display refresh.
        private readonly Dictionary<string, TimerRowVisual> _rows = new Dictionary<string, TimerRowVisual>();
        private readonly Grid _moveChrome;
        private readonly Action<double, double> _persistPosition;

        public TimerListWindow(string moveLabel, double left, double top, Action<double, double> persistPosition)
        {
            InitializeComponent();
            Left = left;
            Top = top;
            _persistPosition = persistPosition;

            _moveChrome = MoveChrome.Build(moveLabel);
            RootGrid.Children.Add(_moveChrome);

            SourceInitialized += (s, e) => ClickThrough.Set(this, true);
            MouseLeftButtonDown += OnDragStart;
        }

        public void SetMoveMode(bool moving)
        {
            _moveChrome.Visibility = moving ? Visibility.Visible : Visibility.Collapsed;
            ClickThrough.Set(this, !moving);
        }

        private void OnDragStart(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_moveChrome.Visibility != Visibility.Visible) return;
            DragMove();                          // blocks until the button is released
            _persistPosition(Left, Top);         // crash-safe: saved on every drag-end
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
                    visual = new TimerRowVisual();
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
