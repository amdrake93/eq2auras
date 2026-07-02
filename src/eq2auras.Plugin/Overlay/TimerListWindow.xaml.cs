using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Eq2Auras.Core.Timers;

namespace Eq2Auras.Plugin.Overlay
{
    public partial class TimerListWindow : Window
    {
        // Phase-1 constants (future config knobs): row size, list width, colors.
        private const double RowHeight = 26;
        private const double RowWidth = 250;

        private static readonly Color CalmBackground = Color.FromArgb(150, 18, 24, 34);
        private static readonly Color CalmBorder = Color.FromArgb(200, 51, 64, 79);
        private static readonly Color ImminentBorder = Colors.Gold;
        private static readonly Color OverdueBorder = Colors.Crimson;

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_TRANSPARENT = 0x20;

        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        public TimerListWindow()
        {
            InitializeComponent();
            SourceInitialized += MakeClickThrough;
        }

        private void MakeClickThrough(object sender, EventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_LAYERED | WS_EX_TRANSPARENT);
        }

        /// Called on the overlay's dispatcher thread with a fresh sorted snapshot.
        public void RenderRows(List<TimerRow> rows)
        {
            RowsPanel.Children.Clear();
            foreach (var row in rows)
            {
                RowsPanel.Children.Add(BuildRow(row));
            }
        }

        private static UIElement BuildRow(TimerRow row)
        {
            var timerColor = Soften(ColorFromArgb(row.FillArgb));

            var border = new Border
            {
                Width = RowWidth,
                Height = RowHeight,
                Margin = new Thickness(0, 0, 0, 4),
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(CalmBackground),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(BorderColorFor(row.Urgency)),
                ClipToBounds = true
            };

            var grid = new Grid();

            // Draining fill, tinted by the timer's own ACT FillColor.
            grid.Children.Add(new Border
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = Math.Max(0, (RowWidth - 2) * row.FillFraction),
                Background = new SolidColorBrush(
                    Color.FromArgb(90, timerColor.R, timerColor.G, timerColor.B)),
                CornerRadius = new CornerRadius(3)
            });

            grid.Children.Add(new TextBlock
            {
                Text = row.Name,
                Foreground = Brushes.WhiteSmoke,
                FontSize = 13,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            grid.Children.Add(new TextBlock
            {
                Text = TimeText(row),
                Foreground = new SolidColorBrush(BorderColorFor(row.Urgency)),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 8, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            });

            border.Child = grid;
            return border;
        }

        private static string TimeText(TimerRow row) =>
            row.Urgency == TimerUrgency.Overdue ? "LATE +" + (-row.TimeLeft) + "s" : row.TimeLeft + "s";

        private static Color BorderColorFor(TimerUrgency urgency)
        {
            switch (urgency)
            {
                case TimerUrgency.Overdue: return OverdueBorder;
                case TimerUrgency.Imminent: return ImminentBorder;
                default: return CalmBorder;
            }
        }

        private static Color ColorFromArgb(int argb) => Color.FromArgb(
            (byte)((argb >> 24) & 0xFF), (byte)((argb >> 16) & 0xFF),
            (byte)((argb >> 8) & 0xFF), (byte)(argb & 0xFF));

        /// ACT timer colors are user data and default to maximum-saturation primaries
        /// (pure #0000FF blue) — blend 35% toward slate so the fill reads pleasant
        /// while keeping the timer's hue recognizable. A future config knob.
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
