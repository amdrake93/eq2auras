using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Eq2Auras.Core.Config;
using Eq2Auras.Core.Meter;
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
        private GridOverlayWindow _grid;
        private readonly MeterEngine _meterEngine = new MeterEngine();
        private readonly Dictionary<MeterWindowConfig, MeterWindow> _meterWindows =
            new Dictionary<MeterWindowConfig, MeterWindow>();
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
                _grid = new GridOverlayWindow();   // hidden until move mode
                if (_settings.Meter.Enabled) CreateMeterWindows();
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
                StyleFor(panel),
                panel.ListGrowDirection,
                (left, top) => SettingsStore.Update(_settings, () => { panel.ListLeft = left; panel.ListTop = top; }));
            list.Show();
            _listWindows.Add(list);

            var center = new CenterZoneWindow(
                name + " — escalation",
                panel.CenterLeft ?? DefaultCenterLeft(),
                panel.CenterTop ?? DefaultCenterTop(index),
                StyleFor(panel),
                panel.CenterGrowDirection,
                (left, top) => SettingsStore.Update(_settings, () => { panel.CenterLeft = left; panel.CenterTop = top; }));
            center.Show();
            _centerWindows.Add(center);
        }

        /// One style per panel — it carries both windows' element dimensions.
        private static VisualStyle StyleFor(PanelSettings panel)
        {
            return new VisualStyle
            {
                RowWidth = panel.RowWidth ?? VisualStyle.DefaultRowWidth,
                RowHeight = panel.RowHeight ?? VisualStyle.DefaultRowHeight,
                RadialSize = panel.RadialSize ?? VisualStyle.DefaultRadialSize,
                RowSpacing = panel.RowSpacing ?? 4.0,
                Font = panel.FontFamily != null ? new System.Windows.Media.FontFamily(panel.FontFamily) : null,
                BaseSize = panel.FontBaseSize ?? 13.0
            };
        }

        private void CreateMeterWindows()
        {
            foreach (var config in _settings.Meter.Windows) AddMeterWindow(config);
        }

        private void AddMeterWindow(MeterWindowConfig config)
        {
            var style = MeterStyle(config);
            var window = new MeterWindow(
                config.Left ?? DefaultMeterLeft(style),
                config.Top ?? DefaultMeterTop,
                style,
                config.MetricKey,
                config.Locked,
                config.Opacity ?? MeterSettings.DefaultOpacity,
                (left, top) => SettingsStore.Update(_settings, () => { config.Left = left; config.Top = top; }),
                key => SettingsStore.Update(_settings, () => config.MetricKey = key),
                locked => SettingsStore.Update(_settings, () => config.Locked = locked),
                opacity => SettingsStore.Update(_settings, () => config.Opacity = opacity),
                rowHeight => SettingsStore.Update(_settings, () => config.RowHeight = rowHeight),
                () => AddClonedWindow(config),
                () => CloseMeterWindow(config),
                () => _meterWindows.Count > 1);
            _meterWindows[config] = window;
            window.Show();
        }

        /// New meter window: clone the invoked window's config, offset + clamped on-screen
        /// (SPEC Part III §Multiple windows). Re-pointed at another metric from its own menu.
        private void AddClonedWindow(MeterWindowConfig source)
        {
            var style = MeterStyle(source);
            double baseLeft = source.Left ?? DefaultMeterLeft(style);
            double baseTop = source.Top ?? DefaultMeterTop;
            var clone = new MeterWindowConfig
            {
                MetricKey = source.MetricKey,
                Locked = source.Locked,
                Opacity = source.Opacity,
                RowHeight = source.RowHeight,
                Left = ClampMeterX(baseLeft + MeterCascadeOffset, style),
                Top = ClampMeterY(baseTop + MeterCascadeOffset),
            };
            SettingsStore.Update(_settings, () => _settings.Meter.Windows.Add(clone));
            AddMeterWindow(clone);
        }

        /// The last window can't close — the tab toggle is the master off-switch (SPEC Part III).
        private void CloseMeterWindow(MeterWindowConfig config)
        {
            if (_meterWindows.Count <= 1) return;
            if (_meterWindows.TryGetValue(config, out var window))
            {
                window.Close();
                _meterWindows.Remove(config);
            }
            SettingsStore.Update(_settings, () => _settings.Meter.Windows.Remove(config));
        }

        // Meter rows touch (SPEC Part III §Meter display defaults); per-window size/font/
        // opacity knobs arrive in later increments, so increment 1 uses baked defaults.
        private static VisualStyle MeterStyle(MeterWindowConfig config)
            => new VisualStyle { RowSpacing = 0, RowHeight = config.RowHeight ?? VisualStyle.DefaultRowHeight };

        private const double MeterCascadeOffset = 30;
        private const double MeterWindowSlack = 10;   // matches MeterWindow's window slack
        private const double DefaultMeterTop = 320;

        private static double DefaultMeterLeft(VisualStyle style)
            => SystemParameters.PrimaryScreenWidth - style.RowWidth - 60;

        private static double ClampMeterX(double x, VisualStyle style)
            => Math.Max(0, Math.Min(x, SystemParameters.PrimaryScreenWidth - (style.RowWidth + MeterWindowSlack)));

        private static double ClampMeterY(double y)
            => Math.Max(0, Math.Min(y, SystemParameters.PrimaryScreenHeight - 100));

        /// Tab toggle, applied live. The meter window is NOT part of move mode:
        /// its interactivity makes a separate unlock unnecessary (SPEC Part III).
        public void SetMeterEnabled(bool enabled)
        {
            var dispatcher = _dispatcher;
            if (dispatcher == null) return;
            dispatcher.BeginInvoke((Action)(() =>
            {
                // The tab's SettingsStore.Update(enabled = true) has already run Normalize,
                // which seeds one default window into Meter.Windows if the list was empty.
                if (enabled && _meterWindows.Count == 0) CreateMeterWindows();
                else if (!enabled && _meterWindows.Count > 0)
                {
                    foreach (var window in _meterWindows.Values) window.Close();
                    _meterWindows.Clear();   // configs persist in Meter.Windows for the next enable
                }
            }));
        }

        /// Callable from any thread (the sample runs on ACT's UI thread). Fans the one
        /// shared snapshot to each window's metric through the one shared engine/palette —
        /// an ally reads the same color in every window (SPEC Part III §Multiple windows).
        public void UpdateMeterSample(EncounterReading encounter, List<CombatantReading> combatants,
            IReadOnlyList<int> paletteArgb)
        {
            var dispatcher = _dispatcher;
            if (dispatcher == null) return;
            dispatcher.BeginInvoke((Action)(() =>
            {
                foreach (var pair in _meterWindows)
                {
                    var frame = _meterEngine.Tick(encounter, combatants, pair.Key.MetricKey, paletteArgb);
                    pair.Value.Render(frame);
                }
            }));
        }

        /// Tab knob changed: each window converts-and-persists via SetGrowDirection.
        public void ApplyGrowDirections()
        {
            var dispatcher = _dispatcher;
            if (dispatcher == null) return;
            dispatcher.BeginInvoke((Action)(() =>
            {
                for (int i = 0; i < _settings.Panels.Count && i < _listWindows.Count; i++)
                {
                    _listWindows[i].SetGrowDirection(_settings.Panels[i].ListGrowDirection);
                    _centerWindows[i].SetGrowDirection(_settings.Panels[i].CenterGrowDirection);
                }
            }));
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
                    _listWindows[i].SetStyle(StyleFor(_settings.Panels[i]));
                    _centerWindows[i].SetStyle(StyleFor(_settings.Panels[i]));
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
                if (moving)
                {
                    _grid?.Show();
                    // The grid must sit BENEATH the windows being placed: re-asserting
                    // HWND_TOPMOST lifts each overlay window to the top of the topmost
                    // band, above the just-shown grid (SPEC §Moving the overlay).
                    foreach (var window in _listWindows) WindowOrder.RaiseTopmost(window);
                    foreach (var window in _centerWindows) WindowOrder.RaiseTopmost(window);
                }
                else
                {
                    _grid?.Hide();
                }

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
                    panel.ListTop = _listWindows[i].AnchorY;
                    panel.CenterLeft = _centerWindows[i].Left;
                    panel.CenterTop = _centerWindows[i].AnchorY;
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
                foreach (var window in _meterWindows.Values) window.Close();
                _meterWindows.Clear();
                _grid?.Close();
                _grid = null;
            });
            _dispatcher.InvokeShutdown();
            _thread?.Join(TimeSpan.FromSeconds(2));
        }
    }
}
