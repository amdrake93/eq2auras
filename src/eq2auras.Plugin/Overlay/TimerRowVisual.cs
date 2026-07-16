using System;
using System.Windows;
using System.Windows.Media;
using Eq2Auras.Core.Timers;

namespace Eq2Auras.Plugin.Overlay
{
    /// One retained list row: the shared bar primitive configured timer-style
    /// (spark on, wall-clock drain target), plus the timer-only urgency styling.
    /// Created once per timer and UPDATED across ticks — never rebuilt.
    internal sealed class TimerRowVisual
    {
        // Wall-clock targets keep drift ~0; only a genuine reset (new frame/instance)
        // should re-target the drain, so the tolerance is generous — in SECONDS.
        private const double DriftToleranceSeconds = 0.75;

        private readonly BarRowVisual _bar;
        private int _fillArgb = int.MinValue;
        private TimerUrgency _urgency = (TimerUrgency)(-1);

        public UIElement Root => _bar.Root;

        public TimerRowVisual(VisualStyle style)
        {
            _bar = new BarRowVisual(style, spark: true);
        }

        public void Update(TimerRow row)
        {
            _bar.NameText.Text = row.Name;
            // Wall-clock seconds so the text agrees with the smooth fill; overdue rows
            // (HighlightInPlace mode, linger-configured timers) count up as LATE.
            _bar.TrailingText.Text = row.Urgency == TimerUrgency.Overdue
                ? "LATE +" + (-row.TimeLeft) + "s"
                : (int)Math.Max(0, Math.Ceiling(row.PreciseTimeLeft)) + "s";

            if (row.Urgency != _urgency)
            {
                _urgency = row.Urgency;
                _bar.RootBorder.BorderBrush = new SolidColorBrush(OverlayTheme.AccentFor(row.Urgency));
                // Calm's accent is dark slate — invisible as text on the dark backplate.
                _bar.TrailingText.Foreground = new SolidColorBrush(row.Urgency == TimerUrgency.Calm
                    ? OverlayTheme.Text
                    : OverlayTheme.AccentFor(row.Urgency));
            }

            if (row.FillArgb != _fillArgb)
            {
                _fillArgb = row.FillArgb;
                _bar.SetFillColor(row.FillArgb);   // Core resolved it
            }

            if (row.TotalSeconds <= 0) return;
            double pxPerSecond = _bar.UsableWidth / row.TotalSeconds;
            double desired = Math.Max(0, Math.Min(1, row.PreciseTimeLeft / row.TotalSeconds)) * _bar.UsableWidth;
            double current = _bar.CurrentFillWidth;
            if (double.IsNaN(current) || Math.Abs(current - desired) > pxPerSecond * DriftToleranceSeconds)
            {
                _bar.AnimateDrain(desired, row.PreciseTimeLeft);
            }
        }
    }
}
