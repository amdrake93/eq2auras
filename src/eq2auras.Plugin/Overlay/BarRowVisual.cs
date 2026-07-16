using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Eq2Auras.Plugin.Overlay
{
    /// The shared row/bar primitive (SPEC Part III §The shared rendering substrate):
    /// one configurable component — horizontal bar, animatable proportional fill, fill
    /// color, leading name, trailing value area. Optional features are row CONFIG:
    /// the timer's spark is `spark: true`, not a separate timer bar. The pluggable
    /// part is the animation target: AnimateDrain (wall-clock, timer) vs.
    /// AnimateToFraction (data-driven lerp, meter).
    internal sealed class BarRowVisual
    {
        public const double LerpSeconds = 0.35;   // meter catch-up rate (tunable constant)

        private readonly double _rowWidth;
        private readonly bool _spark;
        private readonly Border _root;
        private readonly Border _fill;
        private readonly TextBlock _name;
        private readonly TextBlock _trailing;
        private readonly StackPanel _trailingPanel;

        public UIElement Root => _root;
        public Border RootBorder => _root;
        public TextBlock NameText => _name;
        public TextBlock TrailingText => _trailing;
        public StackPanel TrailingPanel => _trailingPanel;
        public double UsableWidth => _rowWidth - 2;
        public double CurrentFillWidth => _fill.Width;   // reflects the animated value

        public BarRowVisual(VisualStyle style, bool spark)
        {
            _rowWidth = style.RowWidth;
            _spark = spark;
            double hr = style.HeightRatio;

            _fill = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                CornerRadius = new CornerRadius(3 * hr),
                // The spark: a bright right-edge border riding the animated fill width —
                // marks the moving edge. Width is a future knob. Row config: meter rows
                // ship spark-less (SPEC Part III — spark is a customization of the row).
                BorderThickness = spark ? new Thickness(0, 0, 3 * hr, 0) : new Thickness(0)
            };
            _name = new TextBlock
            {
                Foreground = new SolidColorBrush(OverlayTheme.Text),
                Margin = new Thickness(8 * hr, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            style.ApplyFont(_name, style.RowText);
            _trailing = new TextBlock
            {
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            style.ApplyFont(_trailing, style.RowText);
            _trailingPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8 * hr, 0)
            };
            _trailingPanel.Children.Add(_trailing);

            var grid = new Grid();
            grid.Children.Add(_fill);
            grid.Children.Add(_name);
            grid.Children.Add(_trailingPanel);

            _root = new Border
            {
                Width = _rowWidth,
                Height = style.RowHeight,
                Margin = new Thickness(0, 0, 0, style.RowSpacing),
                CornerRadius = new CornerRadius(4 * hr),
                Background = new SolidColorBrush(OverlayTheme.CalmBackground),
                BorderThickness = new Thickness(1),
                ClipToBounds = true,
                Child = grid
            };
        }

        public void SetFillColor(int argb)
        {
            var color = OverlayTheme.FromArgbInt(argb);
            _fill.Background = new SolidColorBrush(Color.FromArgb(90, color.R, color.G, color.B));
            if (_spark) _fill.BorderBrush = new SolidColorBrush(OverlayTheme.Spark(color));
        }

        /// Timer target model: one linear drain to zero over the remaining seconds.
        public void AnimateDrain(double fromWidth, double seconds)
        {
            var drain = new DoubleAnimation(fromWidth, 0, TimeSpan.FromSeconds(Math.Max(0.05, seconds)));
            _fill.BeginAnimation(FrameworkElement.WidthProperty, drain);
        }

        /// Meter target model: rate-limited catch-up toward a data-driven fraction,
        /// re-targeted each poll. First bind grows from zero (reads as a fade-in).
        public void AnimateToFraction(double fraction)
        {
            double target = Math.Max(0, Math.Min(1, fraction)) * UsableWidth;
            if (double.IsNaN(_fill.Width)) _fill.Width = 0;
            var lerp = new DoubleAnimation(target, TimeSpan.FromSeconds(LerpSeconds))
                { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
            _fill.BeginAnimation(FrameworkElement.WidthProperty, lerp);
        }
    }
}
