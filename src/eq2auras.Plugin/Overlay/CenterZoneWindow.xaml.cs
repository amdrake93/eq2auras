using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Eq2Auras.Core.Timers;

namespace Eq2Auras.Plugin.Overlay
{
    public partial class CenterZoneWindow : Window
    {
        // Phase-1 constants (future config knobs).
        private const double PieDiameter = 110;
        private const double ZoneVerticalScreenFraction = 0.38; // zone top ≈ 38% down the screen

        private static readonly Color LateColor = Colors.Crimson;
        private static readonly Color TextColor = Colors.WhiteSmoke;
        private static readonly Color PieBackplate = Color.FromArgb(120, 18, 24, 34);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_TRANSPARENT = 0x20;

        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        public CenterZoneWindow()
        {
            InitializeComponent();
            Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
            Top = SystemParameters.PrimaryScreenHeight * ZoneVerticalScreenFraction;
            SourceInitialized += MakeClickThrough;
        }

        private void MakeClickThrough(object sender, EventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_LAYERED | WS_EX_TRANSPARENT);
        }

        /// Called on the overlay's dispatcher thread with a fresh snapshot.
        public void RenderElements(List<CenterElement> elements)
        {
            ElementsPanel.Children.Clear();
            foreach (var element in elements)
            {
                ElementsPanel.Children.Add(element.Kind == CenterElementKind.Late
                    ? BuildLateCard(element)
                    : BuildPie(element));
            }
        }

        private static UIElement BuildPie(CenterElement element)
        {
            var color = Soften(ColorFromArgb(element.FillArgb));
            double r = PieDiameter / 2;
            var center = new Point(r, r);

            var canvas = new Canvas { Width = PieDiameter, Height = PieDiameter };
            canvas.Children.Add(new Ellipse
            {
                Width = PieDiameter,
                Height = PieDiameter,
                Fill = new SolidColorBrush(PieBackplate),
                Stroke = new SolidColorBrush(Color.FromArgb(200, color.R, color.G, color.B)),
                StrokeThickness = 2
            });
            canvas.Children.Add(new Path
            {
                Fill = new SolidColorBrush(Color.FromArgb(170, color.R, color.G, color.B)),
                Data = BuildPieGeometry(center, r - 4, element.PieFraction)
            });

            var seconds = new TextBlock
            {
                Text = element.SecondsLeft.ToString(),
                FontSize = 34,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(TextColor),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var overlayText = new Grid { Width = PieDiameter, Height = PieDiameter };
            overlayText.Children.Add(seconds);

            var name = new TextBlock
            {
                Text = element.Name,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(TextColor),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 190
            };

            var pieStack = new Grid();
            pieStack.Children.Add(canvas);
            pieStack.Children.Add(overlayText);

            var root = new StackPanel
            {
                Margin = new Thickness(0, 0, 0, 10),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            root.Children.Add(pieStack);
            root.Children.Add(name);

            Pulse(root, from: 1.0, to: 0.75, seconds: 0.6);
            return root;
        }

        private static UIElement BuildLateCard(CenterElement element)
        {
            var card = new Border
            {
                Width = 170,
                Margin = new Thickness(0, 0, 0, 10),
                Padding = new Thickness(10, 6, 10, 6),
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromArgb(200, 58, 20, 20)),
                BorderBrush = new SolidColorBrush(LateColor),
                BorderThickness = new Thickness(2),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = "LATE +" + element.LateSeconds + "s",
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(TextColor),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            stack.Children.Add(new TextBlock
            {
                Text = element.Name,
                FontSize = 12,
                Foreground = new SolidColorBrush(TextColor),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            card.Child = stack;

            Pulse(card, from: 1.0, to: 0.55, seconds: 0.35);
            return card;
        }

        private static void Pulse(UIElement element, double from, double to, double seconds)
        {
            var animation = new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            element.BeginAnimation(OpacityProperty, animation);
        }

        /// Filled wedge spanning `fraction` of the circle, from 12 o'clock, clockwise —
        /// full at escalation, draining to empty as the warning window runs out.
        private static Geometry BuildPieGeometry(Point center, double radius, double fraction)
        {
            fraction = Math.Max(0, Math.Min(1, fraction));
            if (fraction >= 0.999) return new EllipseGeometry(center, radius, radius);
            if (fraction <= 0.001) return Geometry.Empty;

            double theta = fraction * 2 * Math.PI;
            var start = new Point(center.X, center.Y - radius);
            var end = new Point(center.X + radius * Math.Sin(theta), center.Y - radius * Math.Cos(theta));

            var figure = new PathFigure { StartPoint = center, IsClosed = true };
            figure.Segments.Add(new LineSegment(start, false));
            figure.Segments.Add(new ArcSegment(end, new Size(radius, radius), 0,
                fraction > 0.5, SweepDirection.Clockwise, false));

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            return geometry;
        }

        private static Color ColorFromArgb(int argb) => Color.FromArgb(
            (byte)((argb >> 24) & 0xFF), (byte)((argb >> 16) & 0xFF),
            (byte)((argb >> 8) & 0xFF), (byte)(argb & 0xFF));

        private static Color Soften(Color c)
        {
            const double keep = 0.65;
            const byte slateR = 110, slateG = 118, slateB = 130;
            return Color.FromArgb(255,
                (byte)(c.R * keep + slateR * (1 - keep)),
                (byte)(c.G * keep + slateG * (1 - keep)),
                (byte)(c.B * keep + slateB * (1 - keep)));
        }
    }
}
