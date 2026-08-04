using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Eq2Auras.Plugin.Overlay
{
    /// The kit's checkbox (SPEC §The theme system — the sixth control-kit primitive): a 13px box +
    /// label, amber ✓ when checked, toggles on left-click. First consumer: the segment flyout's
    /// "Return to Current" knob. Mirrors MetricGridItem's normal/selected state idiom.
    internal sealed class ThemeCheckbox : Border
    {
        private readonly Border _box;
        private readonly TextBlock _check;
        private readonly TextBlock _label;
        private bool _checked;

        public event Action<bool> Toggled;

        public bool Checked
        {
            get { return _checked; }
            set { _checked = value; Apply(); }
        }

        public ThemeCheckbox(string label, bool initial)
        {
            _check = new TextBlock
            {
                Text = "✓",
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Theme.AccentAmber,
            };
            _box = new Border
            {
                Width = 13,
                Height = 13,
                CornerRadius = new CornerRadius(3),
                BorderThickness = new Thickness(1),
                Background = Theme.Surface(0x0D),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
                Child = _check,
            };
            _label = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Foreground = Theme.TextLabel };

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(_box);
            row.Children.Add(_label);

            Child = row;
            Padding = new Thickness(6, 4, 8, 4);
            CornerRadius = new CornerRadius(3);
            Cursor = Cursors.Hand;
            Background = Brushes.Transparent;

            _checked = initial;
            Apply();

            MouseLeftButtonUp += (s, e) => { Checked = !_checked; if (Toggled != null) Toggled(_checked); };
        }

        private void Apply()
        {
            _check.Visibility = _checked ? Visibility.Visible : Visibility.Hidden;
            _box.BorderBrush = _checked ? Theme.AccentAmber : Theme.Divider;
        }
    }
}
