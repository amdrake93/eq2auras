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

        private double _rowWidth;
        private readonly bool _spark;
        private readonly byte _fillAlpha;
        private readonly Border _root;
        private readonly Border _fill;
        private readonly Border _background;   // full-width class-color ground behind the fill (two-tone recap rows); transparent otherwise
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

        // Meter-only, floor-bracket of the convergence guardrail: the fill lives here, so
        // a consumer that needs to dim it does so through the primitive. Element opacity
        // (not the brush) so it survives SetFillColor's per-poll brush rebuild and never
        // touches the text. The timer never sets it (stays 1.0).
        public double FillOpacity { get => _fill.Opacity; set => _fill.Opacity = value; }
        public double BackgroundOpacity { get => _background.Opacity; set => _background.Opacity = value; }

        // Meter-only, same floor-bracket as FillOpacity: the width lives here, so a
        // consumer that resizes it does so through the primitive. UsableWidth reads
        // _rowWidth, so the next AnimateToFraction lerps to the new width. Timer never calls it.
        public void SetRowWidth(double rowWidth)
        {
            _rowWidth = rowWidth;
            _root.Width = rowWidth;
        }

        // fillAlpha defaults to the timer's translucent value (90); the meter passes a
        // higher, vivid value for at-a-glance readability (SPEC Part III §Meter display
        // defaults). A construction parameter, not a blanket change — the timer is
        // untouched by the default.
        public BarRowVisual(VisualStyle style, bool spark, byte fillAlpha = 90, bool nameOutline = false)
        {
            _rowWidth = style.RowWidth;
            _spark = spark;
            _fillAlpha = fillAlpha;
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
            if (nameOutline)   // meter-only (SPEC §Meter display defaults — parameterized, not a blanket timer change): keeps names legible over light class fills
                _name.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    // A darker, tighter black halo — the field found the prior 2px/0.9 too faint over
                    // light class fills (field-2026-08-03). ShadowDepth 0 = centered outline, not a shadow.
                    Color = Colors.Black, ShadowDepth = 0, BlurRadius = 3, Opacity = 1.0, RenderingBias = System.Windows.Media.Effects.RenderingBias.Quality,
                };
            _trailing = new TextBlock
            {
                // Readable-light default: the meter relies on it; the timer overrides
                // per-urgency every tick, so this never changes the timer (SPEC Part III).
                Foreground = new SolidColorBrush(OverlayTheme.Text),
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            style.ApplyFont(_trailing, style.RowText, FontWeights.SemiBold);   // value column accent
            _trailingPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8 * hr, 0)
            };
            _trailingPanel.Children.Add(_trailing);

            _background = new Border { HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brushes.Transparent };

            var grid = new Grid();
            grid.Children.Add(_background);   // behind the fill — the two-tone class ground (transparent unless set)
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
            _fill.Background = new SolidColorBrush(Color.FromArgb(_fillAlpha, color.R, color.G, color.B));
            if (_spark) _fill.BorderBrush = new SolidColorBrush(OverlayTheme.Spark(color));
        }

        /// The two-tone ground behind the fill (SPEC §Class colors — Death Recap): null → transparent
        /// (ordinary single-tone rows), set → the victim's class color under the dark current-HP bar.
        public void SetBackgroundColor(int? argb)
        {
            if (argb == null) { _background.Background = Brushes.Transparent; return; }
            var c = OverlayTheme.FromArgbInt(argb.Value);
            _background.Background = new SolidColorBrush(Color.FromArgb(_fillAlpha, c.R, c.G, c.B));
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
