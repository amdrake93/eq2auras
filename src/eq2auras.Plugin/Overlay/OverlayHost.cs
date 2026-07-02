using System;
using System.Threading;
using System.Windows.Threading;
using Eq2Auras.Core.Timers;

namespace Eq2Auras.Plugin.Overlay
{
    public sealed class OverlayHost : IDisposable
    {
        private Thread _thread;
        private Dispatcher _dispatcher;
        private TimerListWindow _listWindow;
        private CenterZoneWindow _centerWindow;

        public void Start()
        {
            var ready = new ManualResetEventSlim(false);
            _thread = new Thread(() =>
            {
                _dispatcher = Dispatcher.CurrentDispatcher;
                _listWindow = new TimerListWindow();
                _listWindow.Show();
                _centerWindow = new CenterZoneWindow();
                _centerWindow.Show();
                ready.Set();
                Dispatcher.Run();
            });
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.IsBackground = true;
            _thread.Start();
            ready.Wait(TimeSpan.FromSeconds(5));
        }

        /// Callable from any thread (the poll runs on ACT's UI thread).
        public void UpdateFrame(OverlayFrame frame)
        {
            var dispatcher = _dispatcher;
            if (dispatcher == null) return;
            dispatcher.BeginInvoke((Action)(() =>
            {
                _listWindow?.RenderRows(frame.ListRows);
                _centerWindow?.RenderElements(frame.CenterElements);
            }));
        }

        public void Dispose()
        {
            if (_dispatcher == null) return;
            _dispatcher.Invoke(() =>
            {
                _listWindow?.Close();
                _listWindow = null;
                _centerWindow?.Close();
                _centerWindow = null;
            });
            _dispatcher.InvokeShutdown();
            _thread?.Join(TimeSpan.FromSeconds(2));
        }
    }
}
