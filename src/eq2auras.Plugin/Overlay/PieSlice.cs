using System;
using System.Windows;
using System.Windows.Media;

namespace Eq2Auras.Plugin.Overlay
{
    /// A filled wedge spanning Fraction of a circle, from 12 o'clock clockwise.
    /// Fraction is a dependency property so WPF can ANIMATE it at display refresh —
    /// the smooth drain comes from one linear animation, not per-tick redraws.
    internal sealed class PieSlice : FrameworkElement
    {
        public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(
            nameof(Fraction), typeof(double), typeof(PieSlice),
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public double Fraction
        {
            get => (double)GetValue(FractionProperty);
            set => SetValue(FractionProperty, value);
        }

        public Brush Fill { get; set; } = Brushes.SlateGray;

        protected override void OnRender(DrawingContext dc)
        {
            double fraction = Math.Max(0, Math.Min(1, Fraction));
            if (fraction <= 0.001) return;

            double radius = Math.Min(ActualWidth, ActualHeight) / 2 - 4;
            if (radius <= 0) return;
            var center = new Point(ActualWidth / 2, ActualHeight / 2);

            if (fraction >= 0.999)
            {
                dc.DrawEllipse(Fill, null, center, radius, radius);
                return;
            }

            double theta = fraction * 2 * Math.PI;
            var start = new Point(center.X, center.Y - radius);
            var end = new Point(center.X + radius * Math.Sin(theta), center.Y - radius * Math.Cos(theta));

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(center, true, true);
                ctx.LineTo(start, false, false);
                ctx.ArcTo(end, new Size(radius, radius), 0, fraction > 0.5,
                    SweepDirection.Clockwise, false, false);
            }
            geometry.Freeze();
            dc.DrawGeometry(Fill, null, geometry);
        }
    }
}
