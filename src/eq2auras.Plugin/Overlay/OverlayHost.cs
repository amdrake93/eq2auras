using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Eq2Auras.Core.Config;
using Eq2Auras.Core.Meter;
using Eq2Auras.Core.Timers;
using Eq2Auras.Plugin.Act;
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
        private readonly ClassInferenceEngine _inference = new ClassInferenceEngine();
        private bool _wasEncounterActive;   // encounter start/end edge for class-inference reset/flush (SPEC §Class colors)
        private readonly Dictionary<MeterWindowConfig, MeterWindow> _meterWindows =
            new Dictionary<MeterWindowConfig, MeterWindow>();
        private volatile IReadOnlyList<DrillRequest> _drillRequests = new List<DrillRequest>();
        private volatile IReadOnlyList<SegmentSelection> _segmentRequests = new List<SegmentSelection>();
        private long _lastCurrentStartTicks;   // the new-combat edge signal (SPEC §Segments — Return to Current)
        private Thread _thread;
        private Dispatcher _dispatcher;

        public OverlayHost(Settings settings)
        {
            _settings = settings;
            _inference.Import(ClassCacheStore.Load().Entries);   // warm-start the learned colors (SPEC §Class colors)
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
                config.Scope,
                config.MetricKey,
                config.SecondaryKey,
                config.Locked,
                config.Opacity ?? MeterSettings.DefaultOpacity,
                config.BackdropOpacity ?? MeterSettings.DefaultBackdropOpacity,
                config.VisibleRows ?? MeterWindow.DefaultVisibleRows,
                config.SegmentMode,
                config.PinnedToSegment,
                new MeterWindowCallbacks
                {
                    PersistPosition = (left, top) => SettingsStore.Update(_settings, () => { config.Left = left; config.Top = top; }),
                    PrimaryPicked = (scope, key) => SettingsStore.Update(_settings, () => { config.Scope = scope; config.MetricKey = key; }),
                    SecondaryPicked = key => SettingsStore.Update(_settings, () => config.SecondaryKey = key),
                    LockChanged = locked => SettingsStore.Update(_settings, () => config.Locked = locked),
                    OpacityChanged = opacity => SettingsStore.Update(_settings, () => config.Opacity = opacity),
                    BackdropOpacityChanged = v => SettingsStore.Update(_settings, () => config.BackdropOpacity = v),
                    RowHeightChanged = rowHeight => SettingsStore.Update(_settings, () => config.RowHeight = rowHeight),
                    FontChanged = (family, size) => SettingsStore.Update(_settings, () => { config.FontFamily = family; config.FontBaseSize = size; }),
                    GeometryChanged = (width, rows) => SettingsStore.Update(_settings, () => { config.Width = width; config.VisibleRows = rows; }),
                    NewWindow = () => AddNewWindow(config),
                    CloseWindow = () => CloseMeterWindow(config),
                    CanClose = () => _meterWindows.Count > 1,
                    DrillChanged = RebuildDrillRequests,
                    ReadHoverNow = request => ReadHoverRowsNow(config, request),
                    SegmentModeChanged = mode => SettingsStore.Update(_settings, () => config.SegmentMode = mode),
                    PinnedChanged = pinned => SettingsStore.Update(_settings, () => config.PinnedToSegment = pinned),
                    EnumerateSegments = () => SegmentResolver.Enumerate(Advanced_Combat_Tracker.ActGlobals.oFormActMain),
                    SegmentChanged = () => { RebuildSegmentRequests(); RebuildDrillRequests(); },
                });
            _meterWindows[config] = window;
            RebuildSegmentRequests();
            window.Show();
        }

        /// Recompute the per-window segment-selection set the probe resolves each poll. Runs on the
        /// STA thread; the assignment is a lock-free reference swap read via CurrentSegmentRequests().
        /// (Task 10 upgrades the per-window read to the window's live selection; here it derives from
        /// the persisted mode, so windows request their persisted segment.)
        private void RebuildSegmentRequests()
        {
            var list = new List<SegmentSelection>();
            foreach (var window in _meterWindows.Values) list.Add(window.CurrentSelection);
            _segmentRequests = list;
        }

        /// Read by EncounterProbe on ACT's UI thread each poll (SPEC §Segments): the latest lock-free
        /// snapshot of every window's segment selection; the probe dedups + resolves them under the lock.
        public IReadOnlyList<SegmentSelection> CurrentSegmentRequests() => _segmentRequests;

        /// New meter window: inherits the source's **appearance** (row height, font, opacity
        /// — the settings-window knobs, a personal preference held constant across windows;
        /// field call 2026-07-17), but NOT its metric, lock, size (width/visible-rows), or
        /// position — those are per-window and start fresh at a cascade offset, so a new
        /// window is never a locked clone landing on its source (SPEC Part III §Multiple windows).
        private void AddNewWindow(MeterWindowConfig source)
        {
            var created = new MeterWindowConfig
            {
                MetricKey = MetricRegistry.DefaultKey,   // seed so a New meter shows DPS, and null stays "user-cleared"
                RowHeight = source.RowHeight,
                FontFamily = source.FontFamily,
                FontBaseSize = source.FontBaseSize,
                Opacity = source.Opacity,
                BackdropOpacity = source.BackdropOpacity,
                // SecondaryKey intentionally omitted -> null = None: the secondary is a data
                // choice, not inherited (SPEC Part III §Multiple windows — new window -> None).
            };
            var style = MeterStyle(created);
            double baseLeft = source.Left ?? DefaultMeterLeft(style);
            double baseTop = source.Top ?? DefaultMeterTop;
            created.Left = ClampMeterX(baseLeft + MeterCascadeOffset, style);
            created.Top = ClampMeterY(baseTop + MeterCascadeOffset);
            SettingsStore.Update(_settings, () => _settings.Meter.Windows.Add(created));
            AddMeterWindow(created);
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
            RebuildDrillRequests();   // a closed window drops out of the drill-request set
            RebuildSegmentRequests();
        }

        // Per-window style resolved from the config: zero row spacing (meter rows touch —
        // SPEC Part III §Meter display defaults) plus the configurable width, row height,
        // and font.
        private static VisualStyle MeterStyle(MeterWindowConfig config)
            => new VisualStyle
            {
                RowSpacing = 0,
                RowWidth = config.Width ?? VisualStyle.DefaultRowWidth,
                RowHeight = config.RowHeight ?? VisualStyle.DefaultRowHeight,
                Font = config.FontFamily != null ? new System.Windows.Media.FontFamily(config.FontFamily) : null,
                BaseSize = config.FontBaseSize ?? 13.0,
            };

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
                RebuildSegmentRequests();
            }));
        }

        /// Recompute the drill-request set from every window's current DrillTarget. Runs on the STA
        /// thread (a window's DrillChanged callback fires there); the assignment is a lock-free
        /// reference swap the probe reads via CurrentDrillRequests().
        private void RebuildDrillRequests()
        {
            var list = new List<DrillRequest>();
            foreach (var window in _meterWindows.Values)
            {
                var target = window.DrillTarget;
                if (target != null) list.Add(target);
                var hover = window.HoverTarget;
                if (hover != null) list.Add(hover);
            }
            _drillRequests = list;
        }

        /// Read by EncounterProbe on ACT's UI thread each poll (SPEC §Assembly split). Returns the
        /// latest lock-free snapshot — the probe deep-reads each requested combatant under the lock.
        public IReadOnlyList<DrillRequest> CurrentDrillRequests() => _drillRequests;

        /// Read by EncounterProbe on ACT's UI thread (SPEC §Class colors): skip the ability-name read for
        /// allies confirmed this encounter. Thread-safe via the inference engine's internal lock.
        public bool IsClassCommitted(string name) => _inference.IsCommitted(name);

        /// The hover card's synchronous first paint (SPEC §Row drill-down — the by-row mouseover):
        /// on mouse-enter, read the hovered combatant's by-counterpart entries NOW (adapter, under the
        /// lock — one combatant) and rank them, so the card opens instantly instead of a poll later.
        /// Null → the per-poll path fills it a beat on. Runs on the overlay thread; the request the
        /// window also publishes keeps the card live thereafter.
        private List<MeterRow> ReadHoverRowsNow(MeterWindowConfig config, DrillRequest request)
        {
            if (request != null && request.Grouping == BreakdownGrouping.RecapSecond)
            {
                if (!EncounterProbe.TryReadRecapSecondNow(request, out var events)) return null;
                return RecapSecondEngine.Build(events);
            }
            var metric = MetricRegistry.ResolvePrimary(config.MetricKey);
            if (metric == null) return null;
            if (!EncounterProbe.TryReadNow(request, out var entries, out double duration)) return null;
            return BreakdownEngine.Build(entries, metric, duration, _inference.ColorForName);   // by-counterpart hover — each row its counterpart's color
        }

        /// Callable from any thread (the sample runs on ACT's UI thread). Each window renders from
        /// the snapshot of ITS resolved segment (SPEC §Segments — every read follows the window's
        /// segment); a drilled window renders its combatant's by-ability breakdown instead of the list.
        public void UpdateMeterSample(SegmentSampleSet set, List<BreakdownReading> breakdowns, List<RecapReading> recaps)
        {
            var dispatcher = _dispatcher;
            if (dispatcher == null) return;
            dispatcher.BeginInvoke((Action)(() =>
            {
                var byKey = new Dictionary<string, SegmentSample>();
                if (set?.Samples != null)
                    foreach (var s in set.Samples) byKey[s.Key] = s;
                byKey.TryGetValue("C", out var currentSample);

                // Class inference (SPEC §Class colors) rides the CURRENT sample only — it learns who is
                // what class independent of what any window displays; the color LOOKUP follows the rows.
                bool encounterActive = currentSample?.Encounter != null && currentSample.Encounter.Active;
                if (encounterActive && !_wasEncounterActive) _inference.ResetEncounter();
                if (currentSample?.Combatants != null)
                    foreach (var c in currentSample.Combatants)
                        if (c.AbilityNames != null) _inference.Observe(c.Name, c.AbilityNames);
                if (_wasEncounterActive && !encounterActive && _inference.HasDirty)
                {
                    ClassCacheStore.Save(new ClassCache { Entries = new List<ClassCacheEntry>(_inference.Export()) });
                    _inference.ClearDirty();
                }
                _wasEncounterActive = encounterActive;

                string currentZoneKey = set?.CurrentZoneKey;

                // New-combat edge (SPEC §Segments — Return to Current): a new active current encounter
                // snaps every non-pinned window's selection back to Current.
                long curTicks = (currentSample != null && currentSample.Encounter != null && currentSample.Encounter.Active) ? currentSample.EncounterStartTicks : 0;
                bool newCombat = curTicks != 0 && curTicks != _lastCurrentStartTicks;
                if (curTicks != 0) _lastCurrentStartTicks = curTicks;

                foreach (var pair in _meterWindows)
                {
                    var config = pair.Key;
                    var window = pair.Value;

                    if (newCombat && !config.PinnedToSegment)
                    {
                        var snapped = SegmentRules.OnNewCombat(window.CurrentSelection, pinned: false);
                        if (!snapped.Equals(window.CurrentSelection)) window.ApplySelection(snapped, "Current");
                    }

                    var key = SegmentKeys.Of(window.CurrentSelection, currentZoneKey);
                    if (!byKey.TryGetValue(key, out var sample))
                    {
                        // The window's segment has no sample this poll — a culled historical handle.
                        if (window.CurrentSelection.Kind == SegmentKind.Historical) window.ApplySelection(SegmentSelection.Current(), "Current");
                        sample = currentSample;
                    }
                    if (sample == null) continue;
                    if (sample.Unavailable) { window.RenderUnavailable(); continue; }   // Zonewide + PopulateAll off (SPEC §Availability)

                    double duration = MeterEngine.DurationSeconds(sample.Encounter);
                    var metric = MetricRegistry.ResolvePrimary(config.MetricKey);
                    // Deaths (the event metric) builds an event timeline from the segment's death records, not Tick.
                    var listFrame = metric != null && metric.IsEvent
                        ? DeathsEngine.BuildList(sample.Deaths, duration, _inference.ColorForName)
                        : _meterEngine.Tick(sample.Encounter, sample.Combatants, config.MetricKey, config.SecondaryKey, config.Scope, _inference.ColorForName);

                    var target = window.DrillTarget;
                    if (target == null || metric == null)
                    {
                        window.Render(listFrame);

                        // Hover (list mode, real metric): keep the card (opened instantly by the
                        // synchronous on-enter read, §Row drill-down) live. Refresh it when this poll's
                        // by-counterpart reading is present; if the reading is momentarily absent (the
                        // request was just published — a one-tick race) leave the sync-painted card as
                        // is, NOT hide it (hiding here flickered the instant card). Hide only when the
                        // hovered combatant has genuinely left the list (encounter reset / dropped
                        // scope), the auto-exit analog to drill.
                        var hover = window.HoverTarget;
                        if (hover != null)
                        {
                            bool stillListed = false;
                            foreach (var r in listFrame.Rows)
                                if (r.Name == hover.CombatantName) { stillListed = true; break; }
                            if (!stillListed)
                            {
                                window.HideHover();
                            }
                            else if (breakdowns != null)
                            {
                                foreach (var b in breakdowns)
                                    if (b.Grouping == BreakdownGrouping.ByCounterpart && b.CombatantName == hover.CombatantName && b.Source == hover.Source)
                                    {
                                        window.RenderHover(BreakdownEngine.Build(b.Entries, metric, duration, _inference.ColorForName));
                                        break;
                                    }
                            }
                        }
                        continue;
                    }

                    // Deaths: drill into a specific death → its recap (SPEC §Death Recap). The death's row
                    // in the rebuilt list is the auto-exit signal (gone → new encounter / cleared) and the
                    // header total (its time-of-death).
                    if (target.Source == MetricBreakdownSource.Deaths)
                    {
                        MeterRow deathRow = null;
                        foreach (var row in listFrame.Rows)
                            if (row.DrillKey == target.DeathKey) { deathRow = row; break; }
                        if (deathRow == null)
                        {
                            window.ExitDrill();
                            window.Render(listFrame);
                            continue;
                        }
                        RecapReading recap = null;
                        if (recaps != null)
                            foreach (var r in recaps)
                                if (r.DrillKey == target.DeathKey) { recap = r; break; }
                        var recapRows = recap != null ? DeathRecapEngine.Build(recap, _inference.ColorForName(deathRow.Name)) : new List<MeterRow>();   // victim's color as the two-tone ground
                        window.RenderDrill(recapRows, deathRow.FormattedValue);   // total cell = time-of-death
                        continue;
                    }

                    // The drilled combatant's OWN row in the scope-filtered list is its total AND the
                    // auto-exit signal: gone from the list -> it left the scoped population (plan-watch #3).
                    MeterRow ownRow = null;
                    foreach (var row in listFrame.Rows)
                        if (row.Name == target.CombatantName) { ownRow = row; break; }
                    if (ownRow == null)
                    {
                        window.ExitDrill();
                        window.Render(listFrame);
                        continue;
                    }

                    BreakdownReading breakdown = null;
                    if (breakdowns != null)
                        foreach (var b in breakdowns)
                            if (b.Grouping == BreakdownGrouping.ByAbility && b.CombatantName == target.CombatantName && b.Source == target.Source) { breakdown = b; break; }

                    // Header total is the combatant's own list value (ready immediately); the body fills
                    // when the breakdown arrives (one poll later than the click).
                    var rows = breakdown != null
                        ? BreakdownEngine.Build(breakdown.Entries, metric, duration, _ => _inference.ColorForName(target.CombatantName))   // drill: every ability row the drilled combatant's color
                        : new List<MeterRow>();
                    window.RenderDrill(rows, ownRow.FormattedValue);
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
