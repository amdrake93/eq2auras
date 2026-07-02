using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Eq2Auras.Plugin.Overlay
{
    public partial class TestWindow : Window
    {
        // ▼▼ RELOAD INDICATOR: change this colour + push to test live self-update ▼▼
        private static readonly Color BuildColor = Colors.Gold;
        // ▲▲

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_TRANSPARENT = 0x20;

        // Ex-style is a 32-bit value, so the int GetWindowLong/SetWindowLong are correct AND
        // portable on both 32- and 64-bit Windows. Do NOT use the ...Ptr variants here: they
        // are only needed for pointer-sized indices (GWLP_WNDPROC, etc.) and are not exported
        // on 32-bit hosts, so they throw EntryPointNotFoundException on a 32-bit ACT.
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        public TestWindow()
        {
            InitializeComponent();
            ((Rectangle)Pulse).Fill = new SolidColorBrush(BuildColor);
            SourceInitialized += MakeClickThrough;
        }

        private void MakeClickThrough(object sender, EventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_LAYERED | WS_EX_TRANSPARENT);
        }
    }
}
