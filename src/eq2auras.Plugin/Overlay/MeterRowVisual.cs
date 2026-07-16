using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Eq2Auras.Core.Meter;

namespace Eq2Auras.Plugin.Overlay
{
    /// One slot of the meter's retained row pool: the shared bar primitive configured
    /// meter-style (no spark, data-driven lerp target) plus a dimmer percent block.
    /// Slot-keyed by design (SPEC Part III §Row animation): combatants re-bind to
    /// slots as sort order changes; the width convergence masks the rebind swap —
    /// row-reorder position animation is deliberately absent, not missing.
    internal sealed class MeterRowVisual
    {
        private const double FadeSeconds = 0.15;

        private readonly BarRowVisual _bar;
        private readonly TextBlock _percent;

        public UIElement Root => _bar.Root;

        public MeterRowVisual(VisualStyle style)
        {
            _bar = new BarRowVisual(style, spark: false);
            _bar.RootBorder.BorderBrush = new SolidColorBrush(OverlayTheme.CalmBorder);

            _percent = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromArgb(255, 0xC4, 0xCA, 0xD6)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0)
            };
            style.ApplyFont(_percent, style.RowText * 11.0 / 13.0);   // dimmer, slightly smaller
            _bar.TrailingPanel.Children.Add(_percent);
        }

        public void Update(MeterRow row)
        {
            _bar.NameText.Text = row.Name;
            _bar.TrailingText.Text = row.FormattedValue;
            _percent.Text = row.FormattedPercent;
            _bar.SetFillColor(row.FillArgb);
            _bar.AnimateToFraction(row.BarFraction);
        }

        public void FadeIn()
        {
            _bar.Root.Opacity = 0;
            _bar.Root.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(1, TimeSpan.FromSeconds(FadeSeconds)));
        }

        public void FadeOutAndRemove(Panel parent)
        {
            var fade = new DoubleAnimation(0, TimeSpan.FromSeconds(FadeSeconds));
            fade.Completed += (s, e) => parent.Children.Remove(_bar.Root);
            _bar.Root.BeginAnimation(UIElement.OpacityProperty, fade);
        }
    }
}
