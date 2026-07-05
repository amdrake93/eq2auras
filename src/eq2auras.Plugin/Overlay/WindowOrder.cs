using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Eq2Auras.Plugin.Overlay
{
    /// Deterministic stacking inside the topmost band: re-asserting HWND_TOPMOST
    /// moves a window to the TOP of the band. The grid never gets this call — being
    /// click-through it never activates — so it stays beneath the overlay windows
    /// (SPEC §Moving the overlay: the reference draws under the things being placed).
    internal static class WindowOrder
    {
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int x, int y, int cx, int cy, uint flags);

        public static void RaiseTopmost(Window window)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
    }
}
