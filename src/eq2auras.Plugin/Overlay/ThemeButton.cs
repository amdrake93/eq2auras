using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Eq2Auras.Plugin.Overlay
{
    /// A real, chromed button (SPEC §The theme system: "a button with real chrome —
    /// border + fill + hover — never muted text pretending to be clickable"). Border +
    /// TextBlock with rest/hover states; raises Click on left-button-up. Two variants —
    /// Default and a red Destructive (e.g. "Remove meter") whose fill each own their
    /// rest+hover brushes, so hover restores the variant's colour instead of reverting to
    /// the default fill — plus a disabled state (greyed, non-interactive) applied at
    /// construction so a gated action reads as blocked without a hover forcing the restyle.
    internal sealed class ThemeButton : Border
    {
        internal enum Variant { Default, Destructive }

        private static readonly Brush DefaultRestFill  = Theme.Surface(0x0D);   // subtle
        private static readonly Brush DefaultHoverFill = Theme.Surface(0x1A);
        private static readonly Brush DestructiveRestFill  = Frozen(Color.FromRgb(0xA2, 0x32, 0x32));
        private static readonly Brush DestructiveHoverFill = Frozen(Color.FromRgb(0xB8, 0x3A, 0x3A));
        private static readonly Brush DestructiveBorder    = Frozen(Color.FromRgb(0xB3, 0x40, 0x40));
        private static readonly Brush DisabledFill = Theme.Surface(0x0D);

        private readonly TextBlock _label;
        private readonly Brush _restFill;
        private readonly Brush _hoverFill;
        private readonly Brush _restBorder;
        private readonly Brush _hoverBorder;
        private bool _interactive;

        public event Action Click;

        public string Text { set { _label.Text = value; } }

        public ThemeButton(string text, Variant variant = Variant.Default, bool enabled = true)
        {
            _label = new TextBlock
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center
            };
            Child = _label;

            bool destructive = variant == Variant.Destructive;
            _restFill    = destructive ? DestructiveRestFill  : DefaultRestFill;
            _hoverFill   = destructive ? DestructiveHoverFill : DefaultHoverFill;
            _restBorder  = destructive ? DestructiveBorder    : Theme.Divider;
            _hoverBorder = destructive ? DestructiveBorder    : Theme.TextMuted;

            BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(4);
            Padding = new Thickness(11, 4, 11, 4);
            HorizontalAlignment = HorizontalAlignment.Left;

            MouseEnter += (s, e) => { if (_interactive) ApplyHover(); };
            MouseLeave += (s, e) => { if (_interactive) ApplyRest(); };
            // Swallow the press so a draggable parent (e.g. the settings title bar's DragMove)
            // does not intercept the click; Click then fires cleanly on release.
            MouseLeftButtonDown += (s, e) => e.Handled = true;
            MouseLeftButtonUp += (s, e) => { if (_interactive && Click != null) Click(); };

            SetEnabled(enabled);
        }

        /// Set interactivity + the matching visual. Called from the constructor so a gated
        /// action (e.g. Remove meter on the last meter) reads as blocked immediately, not
        /// only after a hover event forces the restyle.
        private void SetEnabled(bool enabled)
        {
            _interactive = enabled;
            Cursor = enabled ? Cursors.Hand : Cursors.Arrow;
            if (enabled)
            {
                ApplyRest();
                return;
            }

            Background = DisabledFill;
            BorderBrush = Theme.Divider;
            _label.Foreground = Theme.TextMuted;
        }

        private void ApplyRest()
        {
            Background = _restFill;
            BorderBrush = _restBorder;
            _label.Foreground = Theme.TextLabel;
        }

        private void ApplyHover()
        {
            Background = _hoverFill;
            BorderBrush = _hoverBorder;
            _label.Foreground = Theme.TextPrimary;
        }

        private static SolidColorBrush Frozen(Color c)
        {
            var brush = new SolidColorBrush(c);
            brush.Freeze();
            return brush;
        }
    }
}
