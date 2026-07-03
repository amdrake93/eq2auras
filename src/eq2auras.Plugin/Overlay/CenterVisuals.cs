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
    /// the element's lifetime. Geometry × scale; text sizes from the font knob only.
    internal sealed class PieVisual
    {
        // Wall-clock targets keep drift ~0; only a genuine reset should re-target — in SECONDS.
        private const double DriftToleranceSeconds = 0.75;

        private readonly StackPanel _root;
        private readonly PieSlice _slice;
        private readonly Ellipse _ring;
        private readonly TextBlock _seconds;
        private readonly TextBlock _name;
        private int _fillArgb = int.MinValue;

        public UIElement Root => _root;

        public PieVisual(VisualStyle style)
        {
            double diameter = style.RadialSize;
            _ring = new Ellipse
            {
                Width = diameter,
                Height = diameter,
                Fill = new SolidColorBrush(Color.FromArgb(120, 18, 24, 34)),
                StrokeThickness = 2
            };
            _slice = new PieSlice { Width = diameter, Height = diameter };
            _seconds = new TextBlock
            {
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(OverlayTheme.Text),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            style.ApplyFont(_seconds, style.PieSeconds);
            _name = new TextBlock
            {
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(OverlayTheme.Text),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 190 * style.RadialRatio
            };
            style.ApplyFont(_name, style.PieName);

            var pieStack = new Grid();
            pieStack.Children.Add(_ring);
            pieStack.Children.Add(_slice);
            pieStack.Children.Add(_seconds);

            _root = new StackPanel
            {
                Margin = new Thickness(0, 0, 0, 10 * style.RadialRatio),
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

        public LateVisual(VisualStyle style)
        {
            double rr = style.RadialRatio;
            _late = new TextBlock
            {
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(OverlayTheme.Text),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            style.ApplyFont(_late, style.LateTag);
            _name = new TextBlock
            {
                Foreground = new SolidColorBrush(OverlayTheme.Text),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            style.ApplyFont(_name, style.LateName);

            var stack = new StackPanel();
            stack.Children.Add(_late);
            stack.Children.Add(_name);

            _root = new Border
            {
                Width = 170 * rr,
                Margin = new Thickness(0, 0, 0, 10 * rr),
                Padding = new Thickness(10 * rr, 6 * rr, 10 * rr, 6 * rr),
                CornerRadius = new CornerRadius(6 * rr),
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
