using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Eq2Auras.Plugin.Overlay
{
    /// A selectable metric row in the popup grid (SPEC Part III §Configuration): normal · hover ·
    /// selected. Selected = amber dot + bright text + a subtle highlight; click TOGGLES (click a
    /// lit one to clear — no "None"). The state vocabulary the kit's list-item carries.
    internal sealed class MetricGridItem : Border
    {
        private readonly Ellipse _dot;
        private readonly TextBlock _label;
        private bool _selected;

        public event Action Toggled;

        public bool Selected
        {
            get { return _selected; }
            set { _selected = value; Apply(); }
        }

        public MetricGridItem(string label, bool selected)
        {
            _dot = new Ellipse { Width = 6, Height = 6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            _label = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(_dot);
            row.Children.Add(_label);

            Child = row;
            Padding = new Thickness(6, 4, 8, 4);
            CornerRadius = new CornerRadius(3);
            Cursor = Cursors.Hand;
            _selected = selected;
            Apply();

            MouseEnter += (s, e) => { if (!_selected) Background = Theme.ItemSelected; };
            MouseLeave += (s, e) => Apply();
            MouseLeftButtonUp += (s, e) => { if (Toggled != null) Toggled(); };
        }

        private void Apply()
        {
            Background = _selected ? Theme.ItemSelected : Brushes.Transparent;
            _label.Foreground = _selected ? Theme.TextPrimary : Theme.TextLabel;
            _label.FontWeight = _selected ? FontWeights.SemiBold : FontWeights.Normal;
            _dot.Fill = _selected ? Theme.AccentAmber : Brushes.Transparent;
        }
    }
}
