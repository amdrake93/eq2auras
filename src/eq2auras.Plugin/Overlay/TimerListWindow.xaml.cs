using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Eq2Auras.Core.Timers;

namespace Eq2Auras.Plugin.Overlay
{
    public partial class TimerListWindow : Window
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_TRANSPARENT = 0x20;

        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        // Retained visuals keyed by timer identity — updated, never rebuilt, so the
        // drain animations run continuously at display refresh.
        private readonly Dictionary<string, TimerRowVisual> _rows = new Dictionary<string, TimerRowVisual>();

        // TEMPORARY feedback aid: shows the candidate palette as labeled boxes under the
        // list so the guild can judge the colors live, in context. Flip off after verdict.
        private const bool ShowPalettePreview = true;

        public TimerListWindow()
        {
            InitializeComponent();
            SourceInitialized += MakeClickThrough;
            if (ShowPalettePreview) BuildPalettePreview();
        }

        private void BuildPalettePreview()
        {
            for (int i = 0; i < OverlayTheme.Palette.Length; i++)
            {
                var swatch = new System.Windows.Controls.Border
                {
                    Width = 44,
                    Height = 30,
                    Margin = new Thickness(0, 0, 6, 0),
                    CornerRadius = new CornerRadius(4),
                    Background = new System.Windows.Media.SolidColorBrush(OverlayTheme.Palette[i]),
                    Child = new System.Windows.Controls.TextBlock
                    {
                        Text = (i + 1).ToString(),
                        FontSize = 12,
                        FontWeight = FontWeights.Bold,
                        Foreground = System.Windows.Media.Brushes.Black,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };
                PreviewPanel.Children.Add(swatch);
            }
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
            var seen = new HashSet<string>();
            RowsPanel.Children.Clear();   // same element instances re-added in sort order — animations continue
            foreach (var row in rows)
            {
                var key = row.Name + "|" + row.Combatant;
                seen.Add(key);
                if (!_rows.TryGetValue(key, out var visual))
                {
                    visual = new TimerRowVisual();
                    _rows[key] = visual;
                }
                visual.Update(row);
                RowsPanel.Children.Add(visual.Root);
            }

            foreach (var stale in _rows.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                _rows.Remove(stale);
            }
        }
    }
}
