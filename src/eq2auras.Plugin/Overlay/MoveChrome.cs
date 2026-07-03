using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Eq2Auras.Plugin.Overlay
{
    /// The unlock-mode chrome (SPEC §Moving the overlay): dashed outline + translucent
    /// fill + label chip. The fill is what makes the window mouse-hittable at all — a
    /// transparent WPF window has no hit-test surface — and its MinHeight gives empty
    /// windows (a quiet list, an idle center zone) a grabbable footprint.
    internal static class MoveChrome
    {
        public static Grid Build(string label)
        {
            var outline = new Rectangle
            {
                Stroke = new SolidColorBrush(Color.FromArgb(230, 86, 180, 233)),
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                RadiusX = 4,
                RadiusY = 4,
                Fill = new SolidColorBrush(Color.FromArgb(70, 86, 180, 233))
            };
            var chip = new TextBlock
            {
                Text = label,
                Foreground = new SolidColorBrush(Colors.WhiteSmoke),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var chrome = new Grid { MinHeight = 60, Visibility = Visibility.Collapsed };
            chrome.Children.Add(outline);
            chrome.Children.Add(chip);
            return chrome;
        }
    }
}
