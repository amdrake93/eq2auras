using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Eq2Auras.Core.Timers;

namespace Eq2Auras.Plugin.Overlay
{
    /// One retained list row. Created once per timer and UPDATED across ticks — never
    /// rebuilt — so WPF animations (the smooth fill drain) survive and run at display
    /// refresh. The poll only re-targets the animation when reality drifts (reset,
    /// clock skew), detected by comparing the animated width with the expected one.
    /// Geometry comes from the style's element dimensions; text sizes come from the
    /// font knob only (SPEC: text never changes with element dimensions).
    internal sealed class TimerRowVisual
    {
        // Wall-clock targets keep drift ~0; only a genuine reset (new frame/instance)
        // should re-target the drain, so the tolerance is generous — in SECONDS.
        private const double DriftToleranceSeconds = 0.75;

        private readonly double _rowWidth;
        private readonly Border _root;
        private readonly Border _fill;
        private readonly TextBlock _name;
        private readonly TextBlock _time;
        private readonly VisualStyle _style;
        private int _fillArgb = int.MinValue;
        private TimerUrgency _urgency = (TimerUrgency)(-1);

        public UIElement Root => _root;

        public TimerRowVisual(VisualStyle style)
        {
            _style = style;
            _rowWidth = style.RowWidth;
            double hr = style.HeightRatio;

            _fill = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                CornerRadius = new CornerRadius(3 * hr),
                // The spark: a bright right-edge border riding the animated fill width —
                // marks the moving edge of the countdown. Width is a future knob.
                BorderThickness = new Thickness(0, 0, 3 * hr, 0)
            };
            _name = new TextBlock
            {
                Foreground = new SolidColorBrush(OverlayTheme.Text),
                Margin = new Thickness(8 * hr, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            style.ApplyFont(_name, style.RowText);
            _time = new TextBlock
            {
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 8 * hr, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            style.ApplyFont(_time, style.RowText);

            var grid = new Grid();
            grid.Children.Add(_fill);
            grid.Children.Add(_name);
            grid.Children.Add(_time);

            _root = new Border
            {
                Width = _rowWidth,
                Height = style.EffectiveRowHeight,
                Margin = new Thickness(0, 0, 0, 4 * hr),
                CornerRadius = new CornerRadius(4 * hr),
                Background = new SolidColorBrush(OverlayTheme.CalmBackground),
                BorderThickness = new Thickness(1),
                ClipToBounds = true,
                Child = grid
            };
        }

        public void Update(TimerRow row)
        {
            _name.Text = row.Name;
            // Wall-clock seconds so the text agrees with the smooth fill; overdue rows
            // (HighlightInPlace mode, linger-configured timers) count up as LATE.
            _time.Text = row.Urgency == TimerUrgency.Overdue
                ? "LATE +" + (-row.TimeLeft) + "s"
                : (int)Math.Max(0, Math.Ceiling(row.PreciseTimeLeft)) + "s";

            if (row.Urgency != _urgency)
            {
                _urgency = row.Urgency;
                _root.BorderBrush = new SolidColorBrush(OverlayTheme.AccentFor(row.Urgency));
                // Calm's accent is dark slate — invisible as text on the dark backplate.
                // The countdown is the row's most important glyph: light when calm,
                // accent-colored only when the accent is bright (gold/crimson).
                _time.Foreground = new SolidColorBrush(row.Urgency == TimerUrgency.Calm
                    ? OverlayTheme.Text
                    : OverlayTheme.AccentFor(row.Urgency));
            }

            if (row.FillArgb != _fillArgb)
            {
                _fillArgb = row.FillArgb;
                var color = OverlayTheme.FromArgbInt(row.FillArgb);   // Core resolved it
                _fill.Background = new SolidColorBrush(Color.FromArgb(90, color.R, color.G, color.B));
                _fill.BorderBrush = new SolidColorBrush(OverlayTheme.Spark(color));
            }

            if (row.TotalSeconds <= 0) return;
            double pxPerSecond = (_rowWidth - 2) / row.TotalSeconds;
            double desired = Math.Max(0, Math.Min(1, row.PreciseTimeLeft / row.TotalSeconds)) * (_rowWidth - 2);
            double current = _fill.Width;   // reflects the animated value
            if (double.IsNaN(current) || Math.Abs(current - desired) > pxPerSecond * DriftToleranceSeconds)
            {
                var drain = new DoubleAnimation(desired, 0,
                    TimeSpan.FromSeconds(Math.Max(0.05, row.PreciseTimeLeft)));
                _fill.BeginAnimation(FrameworkElement.WidthProperty, drain);
            }
        }
    }
}
