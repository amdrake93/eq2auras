using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Eq2Auras.Core.Timers;

namespace Eq2Auras.Plugin.Overlay
{
    /// One retained center-zone pie. The wedge Fraction is a single linear animation
    /// to zero (re-targeted only on drift/reset); the pulse starts once and lives for
    /// the element's lifetime.
    internal sealed class PieVisual
    {
        private const double PieDiameter = 110;
        // Wall-clock targets keep drift ~0; only a genuine reset should re-target — in SECONDS.
        private const double DriftToleranceSeconds = 0.75;

        private readonly StackPanel _root;
        private readonly PieSlice _slice;
        private readonly Ellipse _ring;
        private readonly TextBlock _seconds;
        private readonly TextBlock _name;
        private int _fillArgb = int.MinValue;

        public UIElement Root => _root;

        public PieVisual()
        {
            _ring = new Ellipse
            {
                Width = PieDiameter,
                Height = PieDiameter,
                Fill = new SolidColorBrush(Color.FromArgb(120, 18, 24, 34)),
                StrokeThickness = 2
            };
            _slice = new PieSlice { Width = PieDiameter, Height = PieDiameter };
            _seconds = new TextBlock
            {
                FontSize = 34,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(OverlayTheme.Text),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            _name = new TextBlock
            {
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(OverlayTheme.Text),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 190
            };

            var pieStack = new Grid();
            pieStack.Children.Add(_ring);
            pieStack.Children.Add(_slice);
            pieStack.Children.Add(_seconds);

            _root = new StackPanel
            {
                Margin = new Thickness(0, 0, 0, 10),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _root.Children.Add(pieStack);
            _root.Children.Add(_name);

            Pulses.Slow(_root);
        }

        public void Update(CenterElement element)
        {
            _seconds.Text = element.SecondsLeft.ToString();
            _name.Text = element.Name;

            if (element.FillArgb != _fillArgb)
            {
                _fillArgb = element.FillArgb;
                var color = OverlayTheme.FromArgbInt(element.FillArgb);   // Core resolved it
                _ring.Stroke = new SolidColorBrush(Color.FromArgb(200, color.R, color.G, color.B));
                _slice.Fill = new SolidColorBrush(Color.FromArgb(170, color.R, color.G, color.B));
            }

            double current = _slice.Fraction;   // reflects the animated value
            double toleranceFraction = element.WarningSeconds > 0
                ? DriftToleranceSeconds / element.WarningSeconds
                : 0.1;
            if (Math.Abs(current - element.PieFraction) > toleranceFraction)
            {
                var drain = new DoubleAnimation(element.PieFraction, 0,
                    TimeSpan.FromSeconds(Math.Max(0.05, element.PreciseSecondsLeft)));
                _slice.BeginAnimation(PieSlice.FractionProperty, drain);
            }
        }
    }

    /// One retained LATE card — pulse starts once; only the count-up text changes.
    internal sealed class LateVisual
    {
        private readonly Border _root;
        private readonly TextBlock _late;
        private readonly TextBlock _name;

        public UIElement Root => _root;

        public LateVisual()
        {
            _late = new TextBlock
            {
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(OverlayTheme.Text),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _name = new TextBlock
            {
                FontSize = 12,
                Foreground = new SolidColorBrush(OverlayTheme.Text),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            var stack = new StackPanel();
            stack.Children.Add(_late);
            stack.Children.Add(_name);

            _root = new Border
            {
                Width = 170,
                Margin = new Thickness(0, 0, 0, 10),
                Padding = new Thickness(10, 6, 10, 6),
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromArgb(200, 58, 20, 20)),
                BorderBrush = new SolidColorBrush(OverlayTheme.OverdueAccent),
                BorderThickness = new Thickness(2),
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = stack
            };

            Pulses.Fast(_root);
        }

        public void Update(CenterElement element)
        {
            _late.Text = "LATE +" + element.LateSeconds + "s";
            _name.Text = element.Name;
        }
    }

    internal static class Pulses
    {
        public static void Slow(UIElement element) => Begin(element, 0.75, 0.6);
        public static void Fast(UIElement element) => Begin(element, 0.55, 0.35);

        private static void Begin(UIElement element, double to, double seconds)
        {
            var animation = new DoubleAnimation(1.0, to, TimeSpan.FromSeconds(seconds))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            element.BeginAnimation(UIElement.OpacityProperty, animation);
        }
    }
}
