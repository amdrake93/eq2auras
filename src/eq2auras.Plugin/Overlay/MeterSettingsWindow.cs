using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Eq2Auras.Core.Config;

namespace Eq2Auras.Plugin.Overlay
{
    /// Per-window meter settings (SPEC Part III §Configuration) — Details' options window,
    /// dark and custom-chromed, modeless and live-applying. Increment 2 carries one knob
    /// (opacity); row height (inc 3) and font (inc 4) land here next.
    internal sealed class MeterSettingsWindow : Window
    {
        private readonly Action<double> _onOpacityChanged;
        private readonly Slider _opacity;
        private readonly TextBlock _opacityValue;

        public MeterSettingsWindow(double opacity, Action<double> onOpacityChanged)
        {
            _onOpacityChanged = onOpacityChanged;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;

            var title = new TextBlock
            {
                Text = "Meter window · Settings",
                Foreground = new SolidColorBrush(OverlayTheme.Text),
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0)
            };
            var close = new TextBlock
            {
                Text = "✕",
                Foreground = new SolidColorBrush(Color.FromArgb(255, 0x8B, 0x93, 0xA3)),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            };
            close.MouseLeftButtonDown += (s, e) => Close();

            var titleBar = new DockPanel { Height = 34, Background = Brushes.Transparent };
            DockPanel.SetDock(close, Dock.Right);
            titleBar.Children.Add(close);
            titleBar.Children.Add(title);
            titleBar.MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };

            var opacityLabel = new TextBlock
            {
                Text = "Opacity",
                Foreground = new SolidColorBrush(Color.FromArgb(255, 0xC4, 0xCA, 0xD6)),
                VerticalAlignment = VerticalAlignment.Center,
                Width = 60
            };
            _opacity = new Slider
            {
                Minimum = MeterSettings.MinOpacity,
                Maximum = MeterSettings.MaxOpacity,
                Value = opacity,
                Width = 150,
                VerticalAlignment = VerticalAlignment.Center
            };
            _opacityValue = new TextBlock
            {
                Text = Percent(opacity),
                Foreground = new SolidColorBrush(OverlayTheme.Text),
                VerticalAlignment = VerticalAlignment.Center,
                Width = 42,
                TextAlignment = TextAlignment.Right,
                Margin = new Thickness(8, 0, 0, 0)
            };
            _opacity.ValueChanged += (s, e) =>
            {
                _opacityValue.Text = Percent(_opacity.Value);
                _onOpacityChanged(_opacity.Value);
            };
            var opacityRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            opacityRow.Children.Add(opacityLabel);
            opacityRow.Children.Add(_opacity);
            opacityRow.Children.Add(_opacityValue);

            var reset = new TextBlock
            {
                Text = "Reset to defaults",
                Foreground = new SolidColorBrush(Color.FromArgb(255, 0x8B, 0x93, 0xA3)),
                Cursor = Cursors.Hand
            };
            reset.MouseLeftButtonDown += (s, e) => _opacity.Value = MeterSettings.DefaultOpacity;   // fires ValueChanged -> applies + persists

            var body = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };
            body.Children.Add(opacityRow);
            body.Children.Add(reset);

            var stack = new StackPanel();
            stack.Children.Add(titleBar);
            stack.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(OverlayTheme.CalmBorder) });
            stack.Children.Add(body);

            Content = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(252, 20, 23, 29)),
                BorderBrush = new SolidColorBrush(OverlayTheme.CalmBorder),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Child = stack
            };
        }

        private static string Percent(double opacity) => Math.Round(opacity * 100) + "%";
    }
}
