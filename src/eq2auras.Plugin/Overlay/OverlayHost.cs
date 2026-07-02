using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Threading;
using Eq2Auras.Core.Timers;

namespace Eq2Auras.Plugin.Overlay
{
    public sealed class OverlayHost : IDisposable
    {
        private Thread _thread;
        private Dispatcher _dispatcher;
        private TimerListWindow _window;

        public void Start()
        {
            var ready = new ManualResetEventSlim(false);
            _thread = new Thread(() =>
            {
                _dispatcher = Dispatcher.CurrentDispatcher;
                _window = new TimerListWindow();
                _window.Show();
                ready.Set();
                Dispatcher.Run();
            });
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.IsBackground = true;
            _thread.Start();
            ready.Wait(TimeSpan.FromSeconds(5));
        }

        /// Callable from any thread (the poll runs on ACT's UI thread).
        public void UpdateRows(List<TimerRow> rows)
        {
            var dispatcher = _dispatcher;
            if (dispatcher == null) return;
            dispatcher.BeginInvoke((Action)(() => _window?.RenderRows(rows)));
        }

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
