using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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
        private readonly List<TextBlock> _secondaries = new List<TextBlock>();   // 0..N right-aligned columns left of the value
        private VisualStyle _style;
        private double _numberWidth;

        public UIElement Root => _bar.Root;

        /// The last-bound row's name — the window resolves a left-click into a drill target
        /// from this (SPEC Part III §Row drill-down).
        public string CurrentName { get; private set; }

        /// The last-bound row — the window drills from it (name/detail/time for the recap header,
        /// DrillKey for the death identity; SPEC §Deaths).
        public MeterRow CurrentRow { get; private set; }

        public MeterRowVisual(VisualStyle style, double opacity)
        {
            _bar = new BarRowVisual(style, spark: false, fillAlpha: FillAlpha);
            _backplate = new SolidColorBrush(OverlayTheme.MeterBackplate);
            _bar.RootBorder.Background = _backplate;
            _bar.RootBorder.BorderBrush = new SolidColorBrush(OverlayTheme.CalmBorder);

            double numberWidth = MeterColumns.NumberWidth(style, style.RowText);
            double percentWidth = MeterColumns.PercentWidth(style, style.RowText * 11.0 / 13.0);
            _style = style;
            _numberWidth = numberWidth;

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

            // Panel holds [value]; append percent -> [value][percent]. Secondary columns (0..N) are
            // created on demand in Update and inserted BEFORE the value -> [sec0][sec1]…[value][percent]
            // (SPEC §Rows; the recap uses two colored secondaries — dmg/heals).
            _bar.TrailingPanel.Children.Add(_percent);

            SetOpacity(opacity);
        }

        public void Update(MeterRow row)
        {
            CurrentName = row.Name;
            CurrentRow = row;
            if (!string.IsNullOrEmpty(row.Detail))
            {
                // Two-tone leading label (SPEC §Deaths): white victim name + subordinate-grey killing blow.
                _bar.NameText.Inlines.Clear();
                _bar.NameText.Inlines.Add(new Run(row.Name) { Foreground = new SolidColorBrush(OverlayTheme.Text) });
                _bar.NameText.Inlines.Add(new Run(" " + row.Detail) { Foreground = Theme.TextLabel });
            }
            else
            {
                _bar.NameText.Text = row.Name;   // single white run (resets any prior inlines)
            }
            // An empty value collapses its column (no reserved gap) — the recap drops the raw health
            // number and lets the bar + hp% carry it (SPEC §Death Recap). Meter-only; the timer drives
            // BarRowVisual directly and never sets an empty trailing value.
            _bar.TrailingText.Text = row.FormattedValue ?? "";
            _bar.TrailingText.Visibility = string.IsNullOrEmpty(row.FormattedValue) ? Visibility.Collapsed : Visibility.Visible;
            _percent.Text = row.FormattedPercent;

            int need = row.Secondaries?.Count ?? 0;
            while (_secondaries.Count < need)
            {
                var tb = new TextBlock
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Width = _numberWidth,
                    TextAlignment = TextAlignment.Right,
                    Margin = new Thickness(MeterColumns.ColumnGap, 0, 0, 0)
                };
                _style.ApplyFont(tb, _style.RowText);
                _bar.TrailingPanel.Children.Insert(_secondaries.Count, tb);   // before the value column
                _secondaries.Add(tb);
            }
            for (int i = 0; i < _secondaries.Count; i++)
            {
                if (i < need)
                {
                    var sv = row.Secondaries[i];
                    _secondaries[i].Text = sv.FormattedValue;
                    _secondaries[i].Foreground = sv.Argb.HasValue
                        ? new SolidColorBrush(OverlayTheme.FromArgbInt(sv.Argb.Value))
                        : Theme.TextLabel;
                    _secondaries[i].Visibility = Visibility.Visible;
                }
                else
                {
                    _secondaries[i].Visibility = Visibility.Collapsed;
                }
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
            _style = style;
            style.ApplyFont(_bar.NameText, style.RowText);
            style.ApplyFont(_bar.TrailingText, style.RowText);
            style.ApplyFont(_percent, style.RowText * 11.0 / 13.0);

            double numberWidth = MeterColumns.NumberWidth(style, style.RowText);
            _numberWidth = numberWidth;
            _bar.TrailingText.Width = numberWidth;
            _percent.Width = MeterColumns.PercentWidth(style, style.RowText * 11.0 / 13.0);
            foreach (var tb in _secondaries) { style.ApplyFont(tb, style.RowText); tb.Width = numberWidth; }
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
