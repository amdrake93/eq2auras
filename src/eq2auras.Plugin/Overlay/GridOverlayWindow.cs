using System.Windows;
using System.Windows.Media;

namespace Eq2Auras.Plugin.Overlay
{
    /// The unlock-mode placement grid (SPEC §Moving the overlay): a full-screen,
    /// PERMANENTLY click-through reference pinned to the primary monitor — no chrome,
    /// no drag handling, WS_EX_TRANSPARENT set once and never toggled. It cannot be
    /// moved because it is not movable. Drawn once; shown/hidden with move mode.
    public sealed class GridOverlayWindow : Window
    {
        public GridOverlayWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            Left = 0;
            Top = 0;
            Width = SystemParameters.PrimaryScreenWidth;
            Height = SystemParameters.PrimaryScreenHeight;
            Content = new GridLines();
            SourceInitialized += (s, e) => ClickThrough.Set(this, true);
        }
    }

    /// Draw-once line grid: majors every 1 logical cm, fainter minors at half-cm.
    /// Aliased 1-DIP lines (device-pixel-exact only at 100% scaling — Phase-1 DPI
    /// stance). Pens frozen; nothing here ever re-renders after layout.
    internal sealed class GridLines : FrameworkElement
    {
        private const double CmInDips = 96.0 / 2.54;          // 1 logical cm ≈ 37.8 DIPs
        private static readonly Pen MajorPen = MakePen(90);
        private static readonly Pen MinorPen = MakePen(40);

        public GridLines()
        {
            IsHitTestVisible = false;
            RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
        }

        private static Pen MakePen(byte alpha)
        {
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(alpha, 86, 180, 233)), 1.0);
            pen.Freeze();
            return pen;
        }

        protected override void OnRender(DrawingContext dc)
        {
            double half = CmInDips / 2.0;
            int i = 0;
            for (double x = 0; x <= ActualWidth; x += half, i++)
            {
                dc.DrawLine(i % 2 == 0 ? MajorPen : MinorPen, new Point(x, 0), new Point(x, ActualHeight));
            }
            i = 0;
            for (double y = 0; y <= ActualHeight; y += half, i++)
            {
                dc.DrawLine(i % 2 == 0 ? MajorPen : MinorPen, new Point(0, y), new Point(ActualWidth, y));
            }
        }
    }
}
