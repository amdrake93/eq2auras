using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Eq2Auras.Core.Config;

namespace Eq2Auras.Plugin.Overlay
{
    /// Per-window meter settings (SPEC Part III §Configuration) — Details' options window,
    /// dark and custom-chromed on the Theme kit, modeless and live-applying: row height,
    /// window opacity, and font, per window. (The Secondary selection lives in the
    /// right-click popup, not here.)
    internal sealed class MeterSettingsWindow : Window
    {
        private readonly Action<double> _onOpacityChanged;
        private readonly Action<double> _onBackdropOpacityChanged;
        private readonly Action<double> _onRowHeightChanged;
        private readonly Action<string, double> _onFontChanged;
        private string _fontFamily;
        private double _fontBaseSize;

        public MeterSettingsWindow(double rowHeight, Action<double> onRowHeightChanged, double opacity, Action<double> onOpacityChanged,
            double backdropOpacity, Action<double> onBackdropOpacityChanged,
            string fontFamily, double fontBaseSize, Action<string, double> onFontChanged)
        {
            _onOpacityChanged = onOpacityChanged;
            _onBackdropOpacityChanged = onBackdropOpacityChanged;
            _onRowHeightChanged = onRowHeightChanged;
            _onFontChanged = onFontChanged;
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
                Foreground = Theme.TextPrimary,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0)
            };
            var close = new ThemeButton("✕") { Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            close.Click += Close;

            var titleBar = new DockPanel { Height = 34, Background = Brushes.Transparent };
            DockPanel.SetDock(close, Dock.Right);
            titleBar.Children.Add(close);
            titleBar.Children.Add(title);
            titleBar.MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };

            var rowHeightLabel = new TextBlock
            {
                Text = "Row height",
                Foreground = Theme.TextLabel,
                VerticalAlignment = VerticalAlignment.Center,
                Width = 112
            };
            var rowHeightSlider = new ThemeSlider(
                Settings.MinRowHeight, Settings.MaxRowHeight, 1, rowHeight,
                v => Math.Round(v) + " px",
                t => TryParseNumber(t.Replace("px", ""), out double px) ? (double?)px : null);
            rowHeightSlider.ValueChanged += v => _onRowHeightChanged(v);
            var rowHeightRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
            rowHeightRow.Children.Add(rowHeightLabel);
            rowHeightRow.Children.Add(rowHeightSlider);

            var fontLabel = new TextBlock
            {
                Text = "Font",
                Foreground = Theme.TextLabel,
                VerticalAlignment = VerticalAlignment.Center,
                Width = 112
            };
            var fontValue = new TextBlock
            {
                Text = FontLabel(fontFamily, fontBaseSize),
                Foreground = Theme.TextPrimary,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var choose = new ThemeButton("Choose…") { Margin = new Thickness(10, 0, 0, 0) };
            choose.Click += () =>
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
            // Control region matches the sliders' width so Choose… right-aligns with the type-in
            // value boxes; the font-name value fills the space to its left and ellipsis-trims.
            var fontControls = new DockPanel { Width = ThemeSlider.ContentWidth };
            DockPanel.SetDock(choose, Dock.Right);
            fontControls.Children.Add(choose);
            fontControls.Children.Add(fontValue);
            var fontRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
            fontRow.Children.Add(fontLabel);
            fontRow.Children.Add(fontControls);

            var opacityLabel = new TextBlock
            {
                Text = "Window opacity",
                Foreground = Theme.TextLabel,
                VerticalAlignment = VerticalAlignment.Center,
                Width = 112
            };
            var opacitySlider = new ThemeSlider(
                MeterSettings.MinOpacity, MeterSettings.MaxOpacity, 0.01, opacity,
                v => Math.Round(v * 100) + "%",
                t => TryParseNumber(t.Replace("%", ""), out double pct) ? (double?)(pct / 100.0) : null);
            opacitySlider.ValueChanged += v => _onOpacityChanged(v);
            var opacityRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
            opacityRow.Children.Add(opacityLabel);
            opacityRow.Children.Add(opacitySlider);

            var backdropLabel = new TextBlock
            {
                Text = "Backdrop opacity",
                Foreground = Theme.TextLabel,
                VerticalAlignment = VerticalAlignment.Center,
                Width = 112
            };
            var backdropSlider = new ThemeSlider(
                MeterSettings.MinBackdropOpacity, MeterSettings.MaxBackdropOpacity, 0.01, backdropOpacity,
                v => Math.Round(v * 100) + "%",
                t => TryParseNumber(t.Replace("%", ""), out double pct) ? (double?)(pct / 100.0) : null);
            backdropSlider.ValueChanged += v => _onBackdropOpacityChanged(v);
            var backdropRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
            backdropRow.Children.Add(backdropLabel);
            backdropRow.Children.Add(backdropSlider);

            var reset = new ThemeButton("Reset to defaults");
            reset.Click += () =>
            {
                // Each slider's ValueChanged applies + persists; font is reset via its own callback.
                rowHeightSlider.Value = VisualStyle.DefaultRowHeight;
                opacitySlider.Value = MeterSettings.DefaultOpacity;
                backdropSlider.Value = MeterSettings.DefaultBackdropOpacity;
                _fontFamily = null;
                _fontBaseSize = 13.0;
                fontValue.Text = FontLabel(_fontFamily, _fontBaseSize);
                _onFontChanged(_fontFamily, _fontBaseSize);
            };

            var body = new StackPanel { Margin = new Thickness(14, 12, 14, 12) };
            body.Children.Add(rowHeightRow);
            body.Children.Add(fontRow);
            body.Children.Add(opacityRow);
            body.Children.Add(backdropRow);
            body.Children.Add(reset);

            var stack = new StackPanel();
            stack.Children.Add(titleBar);
            stack.Children.Add(new Border { Height = 1, Background = Theme.Divider });
            stack.Children.Add(body);

            Content = new Border
            {
                Background = Theme.Surface(0xFC),
                BorderBrush = Theme.Divider,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Child = stack
            };
        }

        private static bool TryParseNumber(string text, out double value)
            => double.TryParse((text ?? "").Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value);

        private static string FontLabel(string family, double dip)
            => (family ?? "default") + " · " + Math.Round(dip * 72.0 / 96.0) + " pt";
    }
}
