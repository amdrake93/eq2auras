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

    /// Draw-once reference lattice: FIXED line counts with cell size calculated from
    /// the screen, so the grid fits edge to edge exactly at any resolution. Three
    /// brightness tiers tell you where you are — the true center cross brightest, the
    /// four quarter-center lines second, everything else uniformly faint. Counts stay
    /// divisible by 4 so center and quarters always land on lines. Aliased 1-DIP
    /// lines; pens frozen; nothing here ever re-renders after layout.
    internal sealed class GridLines : FrameworkElement
    {
        private const int Columns = 64;   // divisible by 4 — center + quarter lines exist
        private const int Rows = 32;
        private static readonly Pen CenterPen = MakePen(230);
        private static readonly Pen QuarterPen = MakePen(150);
        private static readonly Pen RegularPen = MakePen(70);

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

        private static Pen PenFor(int index, int count)
        {
            if (index * 2 == count) return CenterPen;                            // W/2 or H/2
            if (index * 4 == count || index * 4 == count * 3) return QuarterPen; // quarter centers
            return RegularPen;
        }

        protected override void OnRender(DrawingContext dc)
        {
            for (int i = 0; i <= Columns; i++)
            {
                double x = ActualWidth * i / Columns;
                dc.DrawLine(PenFor(i, Columns), new Point(x, 0), new Point(x, ActualHeight));
            }
            for (int i = 0; i <= Rows; i++)
            {
                double y = ActualHeight * i / Rows;
                dc.DrawLine(PenFor(i, Rows), new Point(0, y), new Point(ActualWidth, y));
            }
        }
    }
}
