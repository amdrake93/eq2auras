using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Eq2Auras.Core.Timers;

namespace Eq2Auras.Plugin.Overlay
{
    public partial class CenterZoneWindow : Window
    {
        private const double ZoneVerticalScreenFraction = 0.38; // zone top ≈ 38% down the screen

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_TRANSPARENT = 0x20;

        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        private sealed class RetainedElement
        {
            public CenterElementKind Kind;
            public PieVisual Pie;
            public LateVisual Late;
            public UIElement Root => Kind == CenterElementKind.Pie ? Pie.Root : Late.Root;
        }

        // Retained visuals keyed by timer identity — updated, never rebuilt, so drains
        // and pulses run continuously at display refresh.
        private readonly Dictionary<string, RetainedElement> _elements = new Dictionary<string, RetainedElement>();

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
            var seen = new HashSet<string>();
            ElementsPanel.Children.Clear();   // same instances re-added in order — animations continue
            foreach (var element in elements)
            {
                var key = element.Name + "|" + element.Combatant;
                seen.Add(key);

                if (!_elements.TryGetValue(key, out var retained) || retained.Kind != element.Kind)
                {
                    retained = element.Kind == CenterElementKind.Pie
                        ? new RetainedElement { Kind = CenterElementKind.Pie, Pie = new PieVisual() }
                        : new RetainedElement { Kind = CenterElementKind.Late, Late = new LateVisual() };
                    _elements[key] = retained;
                }

                if (retained.Kind == CenterElementKind.Pie) retained.Pie.Update(element);
                else retained.Late.Update(element);

                ElementsPanel.Children.Add(retained.Root);
            }

            foreach (var stale in _elements.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                _elements.Remove(stale);
            }
        }
    }
}
