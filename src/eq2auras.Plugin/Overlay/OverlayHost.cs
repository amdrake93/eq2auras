using System;
using System.Threading;
using System.Windows.Threading;

namespace Eq2Auras.Plugin.Overlay
{
    public sealed class OverlayHost : IDisposable
    {
        private Thread _thread;
        private Dispatcher _dispatcher;
        private TestWindow _window;

        public void Start()
        {
            var ready = new ManualResetEventSlim(false);
            _thread = new Thread(() =>
            {
                _dispatcher = Dispatcher.CurrentDispatcher;
                _window = new TestWindow();
                _window.Show();
                ready.Set();
                Dispatcher.Run();
            });
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.IsBackground = true;
            _thread.Start();
            ready.Wait(TimeSpan.FromSeconds(5));
        }

        // Thread-model note: this uses a dedicated STA thread + its own Dispatcher (the spec
        // left ACT-UI-thread vs. dedicated-STA open). If the window misbehaves over the game
        // (topmost/focus loss), the fallback is to create it on ACT's own WinForms UI thread
        // (already STA) via ActGlobals.oFormActMain.BeginInvoke(...). The spike settles which.

        public void Dispose()
        {
            if (_dispatcher == null) return;
            _dispatcher.Invoke(() =>
            {
                _window?.Close();
                _window = null;
            });
            _dispatcher.InvokeShutdown();
            _thread?.Join(TimeSpan.FromSeconds(2));
        }
    }
}
