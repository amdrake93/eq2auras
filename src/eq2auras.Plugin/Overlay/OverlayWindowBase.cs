using System;
using System.Windows;
using Eq2Auras.Core.Config;

namespace Eq2Auras.Plugin.Overlay
{
    /// The shared overlay-window base (SPEC Part III §The shared rendering substrate):
    /// geometry + position persistence, grow-direction anchoring, drag-and-persist, and
    /// the click-through baseline axis of the three-axis interaction model. Timer
    /// windows pass clickThroughBaseline: true (move mode grants interactivity);
    /// the meter window passes false (interactive; its own lock gates dragging).
    public abstract class OverlayWindowBase : Window
    {
        private readonly Action<double, double> _persistPosition;
        private GrowDirection _growDirection;
        private bool _dragging;

        protected OverlayWindowBase(double left, double top, GrowDirection grow,
            Action<double, double> persistPosition, bool clickThroughBaseline)
        {
            _growDirection = grow;
            // Initial Top = the stored ANCHOR for both directions: the first
            // SizeChanged compensates the full initial height under Up, landing the
            // bottom edge on the anchor (SPEC §Window growth).
            Left = left;
            Top = top;
            _persistPosition = persistPosition;

            if (clickThroughBaseline)
            {
                SourceInitialized += (s, e) => ClickThrough.Set(this, true);
            }
            SizeChanged += OnSizeChanged;
        }

        /// The persisted vertical coordinate (SPEC §Window growth): the edge that
        /// doesn't move — top when growing down, bottom when growing up.
        public double AnchorY => _growDirection == GrowDirection.Up ? Top + ActualHeight : Top;

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Growing up = bottom edge anchored: compensate Top by the height delta.
            // Suppressed mid-drag (DragMove owns Top then); drag-end persists whatever
            // the user chose, which IS the reconciliation.
            if (_growDirection != GrowDirection.Up || _dragging) return;
            Top -= e.NewSize.Height - e.PreviousSize.Height;
        }

        /// Knob flip: converts and persists the anchored edge from the window's actual
        /// on-screen geometry — even from a null stored position. The knob changes how
        /// the window GROWS, never where it IS (SPEC §Window growth).
        public void SetGrowDirection(GrowDirection direction)
        {
            if (direction == _growDirection) return;
            _growDirection = direction;
            _persistPosition(Left, AnchorY);
        }

        /// Blocks until the button is released; persists the anchored edge — crash-safe.
        protected void BeginDragAndPersist()
        {
            _dragging = true;
            DragMove();
            _dragging = false;
            _persistPosition(Left, AnchorY);
        }

        protected bool GrowsUp => _growDirection == GrowDirection.Up;

        protected void SetClickThrough(bool clickThrough) => ClickThrough.Set(this, clickThrough);
    }
}
