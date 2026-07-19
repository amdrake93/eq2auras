using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Eq2Auras.Plugin.Overlay
{
    /// A real, chromed button (SPEC §The theme system: "a button with real chrome —
    /// border + fill + hover — never muted text pretending to be clickable"). Border +
    /// TextBlock with rest/hover states; raises Click on left-button-up. Replaces the
    /// settings window's hand-rolled TextBlock+handler "links".
    internal sealed class ThemeButton : Border
    {
        private static readonly Brush RestFill  = Theme.Surface(0x0D);   // subtle
        private static readonly Brush HoverFill = Theme.Surface(0x1A);

        private readonly TextBlock _label;

        public event Action Click;

        public string Text { set { _label.Text = value; } }

        public ThemeButton(string text)
        {
            _label = new TextBlock
            {
                Text = text,
                Foreground = Theme.TextLabel,
                VerticalAlignment = VerticalAlignment.Center
            };

            Child = _label;
            Background = RestFill;
            BorderBrush = Theme.Divider;
            BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(4);
            Padding = new Thickness(11, 4, 11, 4);
            Cursor = Cursors.Hand;
            HorizontalAlignment = HorizontalAlignment.Left;

            MouseEnter += (s, e) => { Background = HoverFill; BorderBrush = Theme.TextMuted; _label.Foreground = Theme.TextPrimary; };
            MouseLeave += (s, e) => { Background = RestFill;  BorderBrush = Theme.Divider;   _label.Foreground = Theme.TextLabel; };
            // Swallow the press so a draggable parent (e.g. the settings title bar's DragMove)
            // does not intercept the click; Click then fires cleanly on release.
            MouseLeftButtonDown += (s, e) => e.Handled = true;
            MouseLeftButtonUp += (s, e) => { if (Click != null) Click(); };
        }
    }
}
