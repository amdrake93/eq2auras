using System;

namespace Eq2Auras.Core.Overlay
{
    /// A screen-space rectangle for hover placement (feature-agnostic — no WPF).
    public struct HoverRect
    {
        public double Left;
        public double Top;
        public double Width;
        public double Height;

        public double Right => Left + Width;
        public double Bottom => Top + Height;
    }

    /// The placed card's top-left in screen DIPs.
    public struct HoverPoint
    {
        public double Left;
        public double Top;
    }

    /// Where a hover card lands, as pure geometry (SPEC Part I §The hover surface). The whole
    /// never-off-screen guarantee: prefer a side of the host and flip on overflow; grow the
    /// preferred vertical direction and fall back to the other; then clamp the minimum amount so
    /// the whole card is on-screen. The chrome insets align a *row inside the card* to the hovered
    /// row (bottom inset in grow-up: the card's last row bottom on the row's bottom; top inset in
    /// grow-down: the card's first row top on the row's top). Any renderer calling this is safe by
    /// construction; Core drives nothing — it only answers.
    public static class HoverPlacement
    {
        public static HoverPoint Compute(
            HoverRect host, HoverRect anchor,
            double cardWidth, double cardHeight,
            double screenWidth, double screenHeight,
            double topInset, double bottomInset,
            double gap, bool preferLeft, bool growUp)
        {
            double leftX = host.Left - gap - cardWidth;
            double rightX = host.Right + gap;
            double x = preferLeft
                ? (leftX >= 0 ? leftX : (rightX + cardWidth <= screenWidth ? rightX : leftX))
                : (rightX + cardWidth <= screenWidth ? rightX : (leftX >= 0 ? leftX : rightX));

            double yUp = anchor.Bottom - cardHeight + bottomInset;   // card's last row bottom on the row's bottom
            double yDown = anchor.Top - topInset;                    // card's first row top on the row's top
            double y = growUp
                ? (yUp >= 0 ? yUp : yDown)
                : (yDown + cardHeight <= screenHeight ? yDown : yUp);

            x = Math.Max(0, Math.Min(x, screenWidth - cardWidth));
            y = Math.Max(0, Math.Min(y, screenHeight - cardHeight));
            return new HoverPoint { Left = x, Top = y };
        }
    }
}
