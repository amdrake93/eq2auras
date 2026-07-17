using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Eq2Auras.Core.Config;

namespace Eq2Auras.Plugin.Overlay
{
    /// Per-window meter settings (SPEC Part III §Configuration) — Details' options window,
    /// dark and custom-chromed, modeless and live-applying: row height, font, and opacity,
    /// per window.
    internal sealed class MeterSettingsWindow : Window
    {
        private readonly Action<double> _onOpacityChanged;
        private readonly Slider _opacity;
        private readonly TextBlock _opacityValue;
        private readonly Action<double> _onRowHeightChanged;
        private readonly Slider _rowHeight;
        private readonly TextBlock _rowHeightValue;
        private readonly Action<string, double> _onFontChanged;
        private readonly Action<string> _onSecondaryChanged;
        private string _fontFamily;
        private double _fontBaseSize;

        public MeterSettingsWindow(double rowHeight, Action<double> onRowHeightChanged, double opacity, Action<double> onOpacityChanged,
            string fontFamily, double fontBaseSize, Action<string, double> onFontChanged,
            string secondaryKey, Action<string> onSecondaryChanged)
        {
            _onOpacityChanged = onOpacityChanged;
            _onRowHeightChanged = onRowHeightChanged;
            _onFontChanged = onFontChanged;
            _onSecondaryChanged = onSecondaryChanged;
            _fontFamily = fontFamily;
            _fontBaseSize = fontBaseSize;

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

            var rowHeightLabel = new TextBlock
            {
                Text = "Row height",
                Foreground = new SolidColorBrush(Color.FromArgb(255, 0xC4, 0xCA, 0xD6)),
                VerticalAlignment = VerticalAlignment.Center,
                Width = 60
            };
            _rowHeight = new Slider
            {
                Minimum = Settings.MinRowHeight,
                Maximum = Settings.MaxRowHeight,
                Value = rowHeight,
                Width = 150,
                IsSnapToTickEnabled = true,
                TickFrequency = 1,
                VerticalAlignment = VerticalAlignment.Center
            };
            _rowHeightValue = new TextBlock
            {
                Text = Px(rowHeight),
                Foreground = new SolidColorBrush(OverlayTheme.Text),
                VerticalAlignment = VerticalAlignment.Center,
                Width = 42,
                TextAlignment = TextAlignment.Right,
                Margin = new Thickness(8, 0, 0, 0)
            };
            _rowHeight.ValueChanged += (s, e) =>
            {
                _rowHeightValue.Text = Px(_rowHeight.Value);
                _onRowHeightChanged(_rowHeight.Value);
            };
            var rowHeightRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            rowHeightRow.Children.Add(rowHeightLabel);
            rowHeightRow.Children.Add(_rowHeight);
            rowHeightRow.Children.Add(_rowHeightValue);

            var fontLabel = new TextBlock
            {
                Text = "Font",
                Foreground = new SolidColorBrush(Color.FromArgb(255, 0xC4, 0xCA, 0xD6)),
                VerticalAlignment = VerticalAlignment.Center,
                Width = 60
            };
            var fontValue = new TextBlock
            {
                Text = FontLabel(fontFamily, fontBaseSize),
                Foreground = new SolidColorBrush(OverlayTheme.Text),
                VerticalAlignment = VerticalAlignment.Center
            };
            var choose = new TextBlock
            {
                Text = "  Choose…",
                Foreground = new SolidColorBrush(Color.FromArgb(255, 0x8B, 0x93, 0xA3)),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };
            choose.MouseLeftButtonDown += (s, e) =>
            {
                using (var dialog = new System.Windows.Forms.FontDialog())
                {
                    var currentFamily = _fontFamily ?? System.Drawing.SystemFonts.MessageBoxFont.Name;
                    dialog.Font = new System.Drawing.Font(currentFamily, (float)(_fontBaseSize * 72.0 / 96.0));
                    if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                    _fontFamily = dialog.Font.Name;
                    _fontBaseSize = dialog.Font.SizeInPoints * 96.0 / 72.0;   // points -> DIPs
                    fontValue.Text = FontLabel(_fontFamily, _fontBaseSize);
                    _onFontChanged(_fontFamily, _fontBaseSize);
                }
            };
            var fontRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            fontRow.Children.Add(fontLabel);
            fontRow.Children.Add(fontValue);
            fontRow.Children.Add(choose);

            var secondaryLabel = new TextBlock
            {
                Text = "Secondary",
                Foreground = new SolidColorBrush(Color.FromArgb(255, 0xC4, 0xCA, 0xD6)),
                VerticalAlignment = VerticalAlignment.Center,
                Width = 60
            };
            var secondary = new ComboBox { Width = 150, VerticalAlignment = VerticalAlignment.Center };
            secondary.Items.Add(new ComboBoxItem { Content = "None", Tag = null });
            foreach (var metric in Eq2Auras.Core.Meter.MetricRegistry.All)
            {
                secondary.Items.Add(new ComboBoxItem { Content = metric.Label, Tag = metric.Key });
            }
            secondary.SelectedIndex = 0;
            for (int i = 0; i < secondary.Items.Count; i++)
            {
                if ((string)((ComboBoxItem)secondary.Items[i]).Tag == secondaryKey) { secondary.SelectedIndex = i; break; }
            }
            secondary.SelectionChanged += (s, e) =>
                _onSecondaryChanged((string)((ComboBoxItem)secondary.SelectedItem).Tag);
            var secondaryRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            secondaryRow.Children.Add(secondaryLabel);
            secondaryRow.Children.Add(secondary);

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
                // Small steps on a 0.3–1.0 range: a track-click moves LargeChange (5%), the
                // arrows SmallChange (1%), snapped to 1% for clean values. Default LargeChange
                // is 1.0, which on this narrow range slammed a click straight to min/max.
                SmallChange = 0.01,
                LargeChange = 0.05,
                IsSnapToTickEnabled = true,
                TickFrequency = 0.01,
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
            reset.MouseLeftButtonDown += (s, e) =>
            {
                // Reset every knob to its default. Each slider's ValueChanged applies + persists;
                // font is reset explicitly (no slider) via its own callback.
                _rowHeight.Value = VisualStyle.DefaultRowHeight;
                _opacity.Value = MeterSettings.DefaultOpacity;
                _fontFamily = null;
                _fontBaseSize = 13.0;
                fontValue.Text = FontLabel(_fontFamily, _fontBaseSize);
                _onFontChanged(_fontFamily, _fontBaseSize);
            };

            var body = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };
            body.Children.Add(rowHeightRow);
            body.Children.Add(fontRow);
            body.Children.Add(secondaryRow);
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

        private static string Px(double rowHeight) => Math.Round(rowHeight) + " px";

        private static string FontLabel(string family, double dip)
            => (family ?? "default") + " · " + Math.Round(dip * 72.0 / 96.0) + " pt";
    }
}
