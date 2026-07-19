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

        // Meter reads near-opaque, unlike the translucent timer (SPEC Part III §Meter
        // display defaults). Vivid fill; the backplate is OverlayTheme.MeterBackplate,
        // shared with the header so they can't drift. Tunable slice-1 constant.
        private const byte FillAlpha = 200;

        private readonly BarRowVisual _bar;
        private readonly TextBlock _percent;
        private readonly SolidColorBrush _backplate;
        private readonly TextBlock _secondary;

        public UIElement Root => _bar.Root;

        public MeterRowVisual(VisualStyle style, double opacity)
        {
            _bar = new BarRowVisual(style, spark: false, fillAlpha: FillAlpha);
            _backplate = new SolidColorBrush(OverlayTheme.MeterBackplate);
            _bar.RootBorder.Background = _backplate;
            _bar.RootBorder.BorderBrush = new SolidColorBrush(OverlayTheme.CalmBorder);

            double numberWidth = MeterColumns.NumberWidth(style, style.RowText);
            double percentWidth = MeterColumns.PercentWidth(style, style.RowText * 11.0 / 13.0);

            // The value is the shared trailing text: the MIDDLE number column (secondary to
            // its left, percent to its right — SPEC §Rows), right-aligned in its fixed cell.
            _bar.TrailingText.Width = numberWidth;
            _bar.TrailingText.TextAlignment = TextAlignment.Right;
            _bar.TrailingText.Margin = new Thickness(MeterColumns.ColumnGap, 0, 0, 0);

            _percent = new TextBlock
            {
                Foreground = Theme.TextLabel,
                VerticalAlignment = VerticalAlignment.Center,
                Width = percentWidth,
                TextAlignment = TextAlignment.Right,
                Margin = new Thickness(MeterColumns.ColumnGap, 0, 0, 0)
            };
            style.ApplyFont(_percent, style.RowText * 11.0 / 13.0);   // dimmer, slightly smaller

            _secondary = new TextBlock
            {
                Foreground = Theme.TextMuted,
                VerticalAlignment = VerticalAlignment.Center,
                Width = numberWidth,
                TextAlignment = TextAlignment.Right,
                Margin = new Thickness(MeterColumns.ColumnGap, 0, 0, 0),
                Visibility = Visibility.Collapsed   // shown only when a secondary is selected
            };
            style.ApplyFont(_secondary, style.RowText);

            // Panel holds [value]; append percent and prepend secondary -> [secondary][value][percent]
            // (value in the middle, percent rightmost — SPEC §Rows).
            _bar.TrailingPanel.Children.Add(_percent);
            _bar.TrailingPanel.Children.Insert(0, _secondary);

            SetOpacity(opacity);
        }

        public void Update(MeterRow row)
        {
            _bar.NameText.Text = row.Name;
            _bar.TrailingText.Text = row.FormattedValue;
            _percent.Text = row.FormattedPercent;

            if (row.Secondaries != null && row.Secondaries.Count > 0)
            {
                _secondary.Text = row.Secondaries[0].FormattedValue;
                _secondary.Visibility = Visibility.Visible;
            }
            else
            {
                _secondary.Visibility = Visibility.Collapsed;
            }

            _bar.SetFillColor(row.FillArgb);
            _bar.AnimateToFraction(row.BarFraction);
        }

        /// One knob scales the fill and the backplate together (SPEC Part III
        /// §Meter display defaults). Element/brush opacity multiplies the baked alphas,
        /// so 1.0 = today's look; text is left at full opacity, always readable.
        public void SetOpacity(double opacity)
        {
            _bar.FillOpacity = opacity;
            _backplate.Opacity = opacity;
        }

        /// Live row-height (SPEC Part III §Configuration): resize the retained row in place
        /// via the shared primitive's border — no recreation, no fade, animations intact.
        public void SetRowHeight(double rowHeight)
        {
            _bar.RootBorder.Height = rowHeight;
        }

        /// Live font (SPEC Part III §Configuration): re-stamp the retained row's text via
        /// ApplyFont — the shared primitive exposes NameText/TrailingText; percent stays the
        /// dimmer, slightly-smaller role. No recreation.
        public void SetFont(VisualStyle style)
        {
            style.ApplyFont(_bar.NameText, style.RowText);
            style.ApplyFont(_bar.TrailingText, style.RowText);
            style.ApplyFont(_percent, style.RowText * 11.0 / 13.0);
            style.ApplyFont(_secondary, style.RowText);

            double numberWidth = MeterColumns.NumberWidth(style, style.RowText);
            _bar.TrailingText.Width = numberWidth;
            _secondary.Width = numberWidth;
            _percent.Width = MeterColumns.PercentWidth(style, style.RowText * 11.0 / 13.0);
        }

        public void SetRowWidth(double rowWidth)
        {
            _bar.SetRowWidth(rowWidth);
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
