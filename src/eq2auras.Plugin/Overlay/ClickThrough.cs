using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Eq2Auras.Plugin.Overlay
{
    /// WS_EX_TRANSPARENT toggling shared by all overlay windows: on = clicks pass
    /// through to the game (normal play); off = the window is mouse-hittable (move mode).
    internal static class ClickThrough
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_TRANSPARENT = 0x20;

        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        public static void Set(Window window, bool clickThrough)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            int ex = GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_LAYERED;
            SetWindowLong(hwnd, GWL_EXSTYLE,
                clickThrough ? ex | WS_EX_TRANSPARENT : ex & ~WS_EX_TRANSPARENT);
        }
    }
}
