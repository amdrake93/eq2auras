using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Eq2Auras.Core.Config;
using Eq2Auras.Core.Timers;
using Eq2Auras.Plugin.SelfUpdate;

namespace Eq2Auras.Plugin.Overlay
{
    /// Hosts one window pair (list + center zone) per timer group on a dedicated STA
    /// thread. Positions come from PanelSettings (null -> built-in defaults, laid out
    /// non-overlapping); drag-end and re-lock persist them back via SettingsStore.
    public sealed class OverlayHost : IDisposable
    {
        private static readonly string[] PanelNames = { "Panel A", "Panel B" };

        private readonly Settings _settings;
        private readonly List<TimerListWindow> _listWindows = new List<TimerListWindow>();
        private readonly List<CenterZoneWindow> _centerWindows = new List<CenterZoneWindow>();
        private Thread _thread;
        private Dispatcher _dispatcher;

        public OverlayHost(Settings settings)
        {
            _settings = settings;
        }

        public void Start()
        {
            var ready = new ManualResetEventSlim(false);
            _thread = new Thread(() =>
            {
                _dispatcher = Dispatcher.CurrentDispatcher;
                for (int i = 0; i < _settings.Panels.Count; i++)
                {
                    CreatePanelWindows(i, _settings.Panels[i]);
                }
                ready.Set();
                Dispatcher.Run();
            });
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.IsBackground = true;
            _thread.Start();
            ready.Wait(TimeSpan.FromSeconds(5));
        }

        private void CreatePanelWindows(int index, PanelSettings panel)
        {
            string name = index < PanelNames.Length ? PanelNames[index] : "Panel " + (index + 1);

            var list = new TimerListWindow(
                name + " — list",
                panel.ListLeft ?? DefaultListLeft(index),
                panel.ListTop ?? DefaultListTop,
                StyleFor(panel, isCenter: false),
                (left, top) => SettingsStore.Update(_settings, () => { panel.ListLeft = left; panel.ListTop = top; }),
                (scale) => SettingsStore.Update(_settings, () => panel.ListScale = scale));
            list.Show();
            _listWindows.Add(list);

            var center = new CenterZoneWindow(
                name + " — escalation",
                panel.CenterLeft ?? DefaultCenterLeft(),
                panel.CenterTop ?? DefaultCenterTop(index),
                StyleFor(panel, isCenter: true),
                (left, top) => SettingsStore.Update(_settings, () => { panel.CenterLeft = left; panel.CenterTop = top; }),
                (scale) => SettingsStore.Update(_settings, () => panel.CenterScale = scale));
            center.Show();
            _centerWindows.Add(center);
        }

        private static VisualStyle StyleFor(PanelSettings panel, bool isCenter)
        {
            return new VisualStyle
            {
                Scale = (isCenter ? panel.CenterScale : panel.ListScale) ?? 1.0,
                Font = panel.FontFamily != null ? new System.Windows.Media.FontFamily(panel.FontFamily) : null,
                BaseSize = panel.FontBaseSize ?? 13.0
            };
        }

        /// Re-resolves every window's style from PanelSettings (font knob changed).
        public void RefreshStyles()
        {
            var dispatcher = _dispatcher;
            if (dispatcher == null) return;
            dispatcher.BeginInvoke((Action)(() =>
            {
                for (int i = 0; i < _settings.Panels.Count && i < _listWindows.Count; i++)
                {
                    _listWindows[i].SetStyle(StyleFor(_settings.Panels[i], isCenter: false));
                    _centerWindows[i].SetStyle(StyleFor(_settings.Panels[i], isCenter: true));
                }
            }));
        }

        // Defaults (WPF DIPs, primary monitor): Panel A exactly where it has always
        // been; Panel B beside/below, non-overlapping. Rough placement is fine —
        // dragging is the real positioning mechanism (SPEC §Moving the overlay).
        private static double DefaultListLeft(int index) => 160 + index * 290;   // list width 260 + gap
        private const double DefaultListTop = 320;
        private static double DefaultCenterLeft() => (SystemParameters.PrimaryScreenWidth - 200) / 2;  // center width 200
        private static double DefaultCenterTop(int index) => SystemParameters.PrimaryScreenHeight * (0.38 + index * 0.18);

        /// Callable from any thread (the poll runs on ACT's UI thread).
        public void UpdateFrames(List<OverlayFrame> frames)
        {
            var dispatcher = _dispatcher;
            if (dispatcher == null) return;
            dispatcher.BeginInvoke((Action)(() =>
            {
                for (int i = 0; i < frames.Count && i < _listWindows.Count; i++)
                {
                    _listWindows[i].RenderRows(frames[i].ListRows);
                    _centerWindows[i].RenderElements(frames[i].CenterElements);
                }
            }));
        }

        /// Unlock shows EVERY window regardless of each group's EscalationStyle, so an
        /// unused center zone can be positioned before styles are flipped (SPEC).
        public void SetMoveMode(bool moving)
        {
            var dispatcher = _dispatcher;
            if (dispatcher == null) return;
            dispatcher.BeginInvoke((Action)(() =>
            {
                foreach (var window in _listWindows) window.SetMoveMode(moving);
                foreach (var window in _centerWindows) window.SetMoveMode(moving);
                if (!moving) SaveAllPositions();   // re-lock persists everything
            }));
        }

        private void SaveAllPositions()
        {
            SettingsStore.Update(_settings, () =>
            {
                for (int i = 0; i < _settings.Panels.Count && i < _listWindows.Count; i++)
                {
                    var panel = _settings.Panels[i];
                    panel.ListLeft = _listWindows[i].Left;
                    panel.ListTop = _listWindows[i].Top;
                    panel.ListScale = _listWindows[i].CurrentScale;
                    panel.CenterLeft = _centerWindows[i].Left;
                    panel.CenterTop = _centerWindows[i].Top;
                    panel.CenterScale = _centerWindows[i].CurrentScale;
                }
            });
        }

        public void Dispose()
        {
            if (_dispatcher == null) return;
            _dispatcher.Invoke(() =>
            {
                foreach (var window in _listWindows) window.Close();
                _listWindows.Clear();
                foreach (var window in _centerWindows) window.Close();
                _centerWindows.Clear();
            });
            _dispatcher.InvokeShutdown();
            _thread?.Join(TimeSpan.FromSeconds(2));
        }
    }
}
