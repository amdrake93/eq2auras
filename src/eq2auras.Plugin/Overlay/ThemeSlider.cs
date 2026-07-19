using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;

namespace Eq2Auras.Plugin.Overlay
{
    /// A dark slider with an editable type-in value (SPEC §The theme system: "a slider
    /// with an editable type-in value — drag for coarse, keyboard for precise; a bordered
    /// input box, not a bare number"). Wraps the native Slider (owns drag/clamp/snap) under
    /// a dark ControlTemplate, paired with a bordered TextBox synced two-way to the value.
    internal sealed class ThemeSlider : Grid
    {
        private static readonly ControlTemplate DarkTemplate = BuildTemplate();
        private const double TrackWidth = 150;   // fixed so the slider has width inside a horizontal StackPanel (matches the pre-overhaul slider)

        private readonly Slider _slider;
        private readonly TextBox _box;
        private readonly Func<double, string> _format;
        private readonly Func<string, double?> _parse;
        private bool _syncing;

        public event Action<double> ValueChanged;

        public double Value
        {
            get { return _slider.Value; }
            set { _slider.Value = value; }
        }

        public ThemeSlider(double min, double max, double step, double value,
            Func<double, string> format, Func<string, double?> parse)
        {
            _format = format;
            _parse = parse;

            // The track column is a FIXED width, not Star: this control sits in a horizontal
            // StackPanel (the settings-window rows), which measures children at their desired
            // width — a Star column has zero desired width there, so the slider collapsed to
            // nothing (field bug, 2026-07-19). A definite width gives the Grid a real size in
            // the StackPanel; the native Slider then lays out and drags normally.
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(TrackWidth) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _slider = new Slider
            {
                Minimum = min,
                Maximum = max,
                Value = value,
                SmallChange = step,
                LargeChange = step,
                IsSnapToTickEnabled = true,
                TickFrequency = step,
                Template = DarkTemplate,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            SetColumn(_slider, 0);
            Children.Add(_slider);

            _box = new TextBox
            {
                Width = 58,
                Text = format(value),
                Foreground = Theme.TextPrimary,
                Background = Theme.Surface(0xFF),
                CaretBrush = Theme.TextPrimary,
                BorderBrush = Theme.Divider,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 2, 6, 2),
                TextAlignment = TextAlignment.Right,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            SetColumn(_box, 1);
            Children.Add(_box);

            _slider.ValueChanged += (s, e) =>
            {
                if (_syncing) return;
                _syncing = true;
                _box.Text = _format(_slider.Value);
                _syncing = false;
                if (ValueChanged != null) ValueChanged(_slider.Value);
            };

            _box.LostFocus += (s, e) => CommitBox();
            _box.KeyDown += (s, e) => { if (e.Key == Key.Enter) CommitBox(); };
        }

        private void CommitBox()
        {
            if (_syncing) return;
            double? parsed = _parse(_box.Text);
            _syncing = true;
            if (parsed.HasValue) _slider.Value = parsed.Value;   // native Slider clamps + snaps
            _box.Text = _format(_slider.Value);                  // reflect the resolved value
            _syncing = false;
            if (parsed.HasValue && ValueChanged != null) ValueChanged(_slider.Value);
        }

        private static ControlTemplate BuildTemplate()
        {
            const string xaml =
                "<ControlTemplate TargetType=\"{x:Type Slider}\" " +
                "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">" +
                "<Grid VerticalAlignment=\"Center\" Height=\"18\" Background=\"Transparent\">" +
                "<Border Height=\"4\" VerticalAlignment=\"Center\" Background=\"#FF2A303B\" CornerRadius=\"2\"/>" +
                "<Track x:Name=\"PART_Track\">" +
                "<Track.Thumb>" +
                "<Thumb>" +
                "<Thumb.Template>" +
                "<ControlTemplate TargetType=\"{x:Type Thumb}\">" +
                "<Border Width=\"12\" Height=\"12\" CornerRadius=\"6\" Background=\"#FFC4CAD6\"/>" +
                "</ControlTemplate>" +
                "</Thumb.Template>" +
                "</Thumb>" +
                "</Track.Thumb>" +
                "</Track>" +
                "</Grid>" +
                "</ControlTemplate>";
            return (ControlTemplate)XamlReader.Parse(xaml);
        }
    }
}
