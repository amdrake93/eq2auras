using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Eq2Auras.Core.Config;
using Eq2Auras.Core.Timers;

namespace Eq2Auras.Plugin.Overlay
{
    public partial class CenterZoneWindow : Window
    {
        private const double BaseWindowWidth = 200;

        private sealed class RetainedElement
        {
            public CenterElementKind Kind;
            public PieVisual Pie;
            public LateVisual Late;
            public UIElement Root => Kind == CenterElementKind.Pie ? Pie.Root : Late.Root;
        }

        // Retained visuals keyed by timer identity — updated, never rebuilt, so drains
        // and pulses run continuously at display refresh.
        private readonly Dictionary<string, RetainedElement> _elements = new Dictionary<string, RetainedElement>();
        private readonly MoveChrome.Chrome _chrome;
        private readonly Action<double, double> _persistPosition;
        private readonly Action<double> _persistScale;
        private VisualStyle _style;
        private Point _gripStart;
        private double _dragStartScale;
        private bool _scaling;

        public double CurrentScale => _style.Scale;

        public CenterZoneWindow(string moveLabel, double left, double top, VisualStyle style,
            Action<double, double> persistPosition, Action<double> persistScale)
        {
            InitializeComponent();
            Left = left;
            Top = top;
            _style = style;
            Width = BaseWindowWidth * style.Scale;
            _persistPosition = persistPosition;
            _persistScale = persistScale;

            _chrome = MoveChrome.Build(moveLabel);
            RootGrid.Children.Add(_chrome.Root);
            _chrome.Grip.MouseLeftButtonDown += OnGripDown;
            _chrome.Grip.MouseMove += OnGripMove;
            _chrome.Grip.MouseLeftButtonUp += OnGripUp;

            SourceInitialized += (s, e) => ClickThrough.Set(this, true);
            MouseLeftButtonDown += OnDragStart;
        }

        public void SetMoveMode(bool moving)
        {
            _chrome.Root.Visibility = moving ? Visibility.Visible : Visibility.Collapsed;
            ClickThrough.Set(this, !moving);
        }

        /// Knob change (font/scale): drop the retained visuals once; the next tick
        /// recreates them under the new style. Pulses/drains restart once — accepted.
        public void SetStyle(VisualStyle style)
        {
            _style = style;
            Width = BaseWindowWidth * style.Scale;
            _elements.Clear();
            ElementsPanel.Children.Clear();
        }

        private void OnDragStart(object sender, MouseButtonEventArgs e)
        {
            if (_chrome.Root.Visibility != Visibility.Visible) return;
            DragMove();
            _persistPosition(Left, Top);
        }

        private void OnGripDown(object sender, MouseButtonEventArgs e)
        {
            _scaling = true;
            _dragStartScale = _style.Scale;
            _gripStart = PointToScreen(e.GetPosition(this));
            _chrome.Grip.CaptureMouse();
            e.Handled = true;                    // never fall through to DragMove
        }

        private void OnGripMove(object sender, MouseEventArgs e)
        {
            if (!_scaling) return;
            var now = PointToScreen(e.GetPosition(this));
            double factor = ProposedScale(now) / _style.Scale;
            RootGrid.LayoutTransform = new ScaleTransform(factor, factor);   // preview only
        }

        private void OnGripUp(object sender, MouseButtonEventArgs e)
        {
            if (!_scaling) return;
            _scaling = false;
            _chrome.Grip.ReleaseMouseCapture();
            RootGrid.LayoutTransform = null;
            double newScale = ProposedScale(PointToScreen(e.GetPosition(this)));
            SetStyle(new VisualStyle { Scale = newScale, Font = _style.Font, BaseSize = _style.BaseSize });
            _persistScale(newScale);
            e.Handled = true;
        }

        private double ProposedScale(Point now)
        {
            double delta = ((now.X - _gripStart.X) + (now.Y - _gripStart.Y)) / 2.0;
            return Math.Min(Settings.MaxScale, Math.Max(Settings.MinScale, _dragStartScale + delta / 250.0));
        }

        /// Called on the overlay's dispatcher thread with a fresh snapshot.
        public void RenderElements(List<CenterElement> elements)
        {
            var seen = new HashSet<string>();
            ElementsPanel.Children.Clear();   // same instances re-added in order — animations continue
            foreach (var element in elements)
            {
                var key = element.Name + "|" + element.Combatant;
                seen.Add(key);

                if (!_elements.TryGetValue(key, out var retained) || retained.Kind != element.Kind)
                {
                    retained = element.Kind == CenterElementKind.Pie
                        ? new RetainedElement { Kind = CenterElementKind.Pie, Pie = new PieVisual(_style) }
                        : new RetainedElement { Kind = CenterElementKind.Late, Late = new LateVisual(_style) };
                    _elements[key] = retained;
                }

                if (retained.Kind == CenterElementKind.Pie) retained.Pie.Update(element);
                else retained.Late.Update(element);

                ElementsPanel.Children.Add(retained.Root);
            }

            foreach (var stale in _elements.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                _elements.Remove(stale);
            }
        }
    }
}
