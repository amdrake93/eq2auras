using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Advanced_Combat_Tracker;
using Eq2Auras.Core.Config;
using Eq2Auras.Core.Timers;
using Eq2Auras.Plugin.Act;
using Eq2Auras.Plugin.Diagnostics;
using Eq2Auras.Plugin.Overlay;
using Eq2Auras.Plugin.SelfUpdate;

namespace Eq2Auras.Plugin
{
    public class Eq2AurasPlugin : IActPluginV1
    {
        private Label _statusLabel;
        private FlowLayoutPanel _paletteRow;
        private JsonlLogWriter _log;
        private TimerProbe _probe;
        private OverlayHost _overlay;
        private OverlayEngine _engine;
        private Settings _settings;

        public void InitPlugin(TabPage pluginScreenSpace, Label pluginStatusText)
        {
            _statusLabel = pluginStatusText;
            var version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? "unknown";

            _log = new JsonlLogWriter();
            _settings = SettingsStore.Load();
            _overlay = new OverlayHost(_settings);
            _overlay.Start();
            _engine = new OverlayEngine(_settings);   // trackers hold the same PanelSettings instances the tab mutates
            _probe = new TimerProbe(_log,
                readings => _overlay.UpdateFrames(
                    _engine.Tick(readings)));

            pluginScreenSpace.Text = "eq2auras";
            BuildConfigTab(pluginScreenSpace);
            // The CI-stamped version is the build tracer: it bumps every push, and with
            // single-assembly packaging Core cannot diverge from it.
            _statusLabel.Text = "eq2auras v" + version + " | logging to " + _log.FilePath;
        }

        public void DeInitPlugin()
        {
            _probe?.Dispose();
            _probe = null;
            _engine = null;
            _overlay?.Dispose();
            _overlay = null;
            _log?.Dispose();
            _log = null;
            if (_statusLabel != null) _statusLabel.Text = "eq2auras unloaded";
        }

        private void BuildConfigTab(TabPage tab)
        {
            tab.AutoScroll = true;   // the layout outgrows a short ACT window

            var pluginsDir = Path.Combine(ActGlobals.oFormActMain.AppDataFolder.FullName, "Plugins");

            var tokenBox = new TextBox { Left = 10, Top = 12, Width = 300, UseSystemPasswordChar = true };
            var saveTokenButton = new Button { Left = 320, Top = 10, Width = 110, Text = "Save token" };
            saveTokenButton.Click += (s, e) =>
            {
                if (string.IsNullOrEmpty(tokenBox.Text)) return;
                TokenStore.Save(tokenBox.Text);
                tokenBox.Clear();
                _statusLabel.Text = "update token saved (DPAPI)";
            };

            var updateButton = new Button { Left = 10, Top = 44, Width = 150, Text = "Check for updates" };
            updateButton.Click += (s, e) =>
                new SelfUpdater(SetStatusThreadSafe, ReloadSelf).RunInBackground(pluginsDir);

            var panelABox = BuildPanelGroupBox("Panel A", _settings.Panels[0], 78);
            var panelBBox = BuildPanelGroupBox("Panel B", _settings.Panels[1], 336);

            var paletteLabel = new Label { Text = "Palette:", Left = 10, Top = 602, Width = 70 };
            // Wrap room for the max case: 16 swatches + 3 buttons flow onto two rows.
            _paletteRow = new FlowLayoutPanel { Left = 82, Top = 596, Width = 400, Height = 68 };
            RebuildPaletteRow();

            var moveBox = new CheckBox { Text = "Move overlay windows", Left = 10, Top = 674, Width = 200 };
            moveBox.CheckedChanged += (s, e) => _overlay.SetMoveMode(moveBox.Checked);

            tab.Controls.Add(tokenBox);
            tab.Controls.Add(saveTokenButton);
            tab.Controls.Add(updateButton);
            tab.Controls.Add(panelABox);
            tab.Controls.Add(panelBBox);
            tab.Controls.Add(paletteLabel);
            tab.Controls.Add(_paletteRow);
            tab.Controls.Add(moveBox);
        }

        /// One labeled control set per group (SPEC §Configuration — no group selector).
        /// Dropdown changes apply live within a poll tick: the engine's trackers hold
        /// the same PanelSettings instance this mutates, and knob handlers + poll share
        /// ACT's UI thread. Persistence goes through the SettingsStore gate.
        private GroupBox BuildPanelGroupBox(string title, PanelSettings panel, int top)
        {
            var box = new GroupBox { Text = title, Left = 10, Top = top, Width = 250, Height = 250 };

            var colorLabel = new Label { Text = "Colors:", Left = 8, Top = 26, Width = 70 };
            var colorBox = new ComboBox
            {
                Left = 82, Top = 22, Width = 150,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            colorBox.Items.AddRange(new object[] { "Palette", "Greyscale", "ACT colors" });
            colorBox.SelectedIndex = (int)panel.ColorSource;
            colorBox.SelectedIndexChanged += (s, e) =>
                SettingsStore.Update(_settings, () => panel.ColorSource = (ColorSource)colorBox.SelectedIndex);

            var styleLabel = new Label { Text = "Escalation:", Left = 8, Top = 58, Width = 70 };
            var styleBox = new ComboBox
            {
                Left = 82, Top = 54, Width = 150,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            styleBox.Items.AddRange(new object[] { "Center radial", "Highlight in place" });
            styleBox.SelectedIndex = (int)panel.EscalationStyle;
            styleBox.SelectedIndexChanged += (s, e) =>
                SettingsStore.Update(_settings, () => panel.EscalationStyle = (EscalationStyle)styleBox.SelectedIndex);

            var fontButton = new Button { Text = "Font…", Left = 8, Top = 86, Width = 70 };
            var fontLabel = new Label { Left = 82, Top = 90, Width = 160, Text = FontLabelText(panel) };
            fontButton.Click += (s, e) =>
            {
                using (var dialog = new FontDialog())
                {
                    var currentDip = panel.FontBaseSize ?? 13.0;
                    var currentFamily = panel.FontFamily ?? System.Drawing.SystemFonts.MessageBoxFont.Name;
                    dialog.Font = new System.Drawing.Font(currentFamily, (float)(currentDip * 72.0 / 96.0));
                    if (dialog.ShowDialog() != DialogResult.OK) return;

                    SettingsStore.Update(_settings, () =>
                    {
                        panel.FontFamily = dialog.Font.Name;
                        panel.FontBaseSize = dialog.Font.SizeInPoints * 96.0 / 72.0;   // points -> DIPs
                    });
                    fontLabel.Text = FontLabelText(panel);
                    _overlay.RefreshStyles();
                }
            };

            var rowLabel = new Label { Text = "Row:", Left = 8, Top = 122, Width = 40 };
            var rowWidthBox = DimensionBox(52, 118, panel.RowWidth ?? VisualStyle.DefaultRowWidth,
                Settings.MinRowWidth, Settings.MaxRowWidth,
                v => panel.RowWidth = v);
            var xLabel = new Label { Text = "×", Left = 116, Top = 122, Width = 14 };
            var rowHeightBox = DimensionBox(132, 118, panel.RowHeight ?? VisualStyle.DefaultRowHeight,
                Settings.MinRowHeight, Settings.MaxRowHeight,
                v => panel.RowHeight = v);

            var radialLabel = new Label { Text = "Radial:", Left = 8, Top = 154, Width = 44 };
            var radialBox = DimensionBox(52, 150, panel.RadialSize ?? VisualStyle.DefaultRadialSize,
                Settings.MinRadialSize, Settings.MaxRadialSize,
                v => panel.RadialSize = v);

            var spacingLabel = new Label { Text = "Spacing:", Left = 8, Top = 186, Width = 44 };
            var spacingBox = DimensionBox(52, 182, panel.RowSpacing ?? 4.0,
                Settings.MinRowSpacing, Settings.MaxRowSpacing,
                v => panel.RowSpacing = v);

            var growLabel = new Label { Text = "Grow:", Left = 8, Top = 218, Width = 40 };
            var listGrowBox = GrowBox(52, 214, panel.ListGrowDirection,
                d => panel.ListGrowDirection = d);
            var centerGrowLabel = new Label { Text = "ctr", Left = 136, Top = 218, Width = 24 };
            var centerGrowBox = GrowBox(162, 214, panel.CenterGrowDirection,
                d => panel.CenterGrowDirection = d);

            box.Controls.Add(colorLabel);
            box.Controls.Add(colorBox);
            box.Controls.Add(styleLabel);
            box.Controls.Add(styleBox);
            box.Controls.Add(fontButton);
            box.Controls.Add(fontLabel);
            box.Controls.Add(rowLabel);
            box.Controls.Add(rowWidthBox);
            box.Controls.Add(xLabel);
            box.Controls.Add(rowHeightBox);
            box.Controls.Add(radialLabel);
            box.Controls.Add(radialBox);
            box.Controls.Add(spacingLabel);
            box.Controls.Add(spacingBox);
            box.Controls.Add(growLabel);
            box.Controls.Add(listGrowBox);
            box.Controls.Add(centerGrowLabel);
            box.Controls.Add(centerGrowBox);
            return box;
        }

        /// One grow-direction dropdown (per window — SPEC §Window growth). The flip
        /// machinery (convert-and-persist) runs in the windows via ApplyGrowDirections.
        private ComboBox GrowBox(int left, int top, GrowDirection value, Action<GrowDirection> assign)
        {
            var box = new ComboBox
            {
                Left = left, Top = top, Width = 80,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            box.Items.AddRange(new object[] { "Down", "Up" });
            box.SelectedIndex = (int)value;
            box.SelectedIndexChanged += (s, e) =>
            {
                SettingsStore.Update(_settings, () => assign((GrowDirection)box.SelectedIndex));
                _overlay.ApplyGrowDirections();
            };
            return box;
        }

        /// One numeric dimension knob: bounds are the shared Settings constants
        /// (enforced here AND in Normalize), live-applied via the rebuild-once path.
        private NumericUpDown DimensionBox(int left, int top, double value,
            double min, double max, Action<double> assign)
        {
            var box = new NumericUpDown
            {
                Left = left, Top = top, Width = 60,
                Minimum = (decimal)min, Maximum = (decimal)max,
                Value = (decimal)value
            };
            // Wired AFTER Value is set — the initial assignment must not fire a save.
            box.ValueChanged += (s, e) =>
            {
                SettingsStore.Update(_settings, () => assign((double)box.Value));
                _overlay.RefreshStyles();
            };
            return box;
        }

        private static string FontLabelText(PanelSettings panel)
            => (panel.FontFamily ?? "default") + " "
               + Math.Round((panel.FontBaseSize ?? 13.0) * 72.0 / 96.0) + " pt";

        private void RebuildPaletteRow()
        {
            _paletteRow.Controls.Clear();

            for (int i = 0; i < _settings.PaletteArgb.Count; i++)
            {
                int index = i;
                var swatch = new Button
                {
                    Width = 30, Height = 26, FlatStyle = FlatStyle.Flat,
                    BackColor = System.Drawing.Color.FromArgb(_settings.PaletteArgb[index])
                };
                swatch.Click += (s, e) =>
                {
                    using (var dialog = new ColorDialog { Color = swatch.BackColor, FullOpen = true })
                    {
                        if (dialog.ShowDialog() != DialogResult.OK) return;
                        SettingsStore.Update(_settings, () => _settings.PaletteArgb[index] = dialog.Color.ToArgb());
                        swatch.BackColor = dialog.Color;
                    }
                };
                _paletteRow.Controls.Add(swatch);
            }

            var add = new Button { Text = "+", Width = 26, Height = 26, Enabled = _settings.PaletteArgb.Count < Settings.MaxPaletteSize };
            add.Click += (s, e) =>
            {
                SettingsStore.Update(_settings, () => _settings.PaletteArgb.Add(unchecked((int)0xFF808080)));
                RebuildPaletteRow();
            };

            var remove = new Button { Text = "−", Width = 26, Height = 26, Enabled = _settings.PaletteArgb.Count > 1 };
            remove.Click += (s, e) =>
            {
                SettingsStore.Update(_settings, () => _settings.PaletteArgb.RemoveAt(_settings.PaletteArgb.Count - 1));
                RebuildPaletteRow();
            };

            var reset = new Button { Text = "Reset", Width = 52, Height = 26 };
            reset.Click += (s, e) =>
            {
                SettingsStore.Update(_settings, () =>
                {
                    _settings.PaletteArgb.Clear();
                    _settings.PaletteArgb.AddRange(ColorPolicy.DefaultPaletteArgb);
                });
                RebuildPaletteRow();
            };

            _paletteRow.Controls.Add(add);
            _paletteRow.Controls.Add(remove);
            _paletteRow.Controls.Add(reset);
        }

        private void SetStatusThreadSafe(string message)
        {
            ActGlobals.oFormActMain.Invoke((MethodInvoker)(() => _statusLabel.Text = message));
        }

        /// Toggling our own Enabled checkbox drives DeInitPlugin -> InitPlugin, and the
        /// enable path re-reads the DLLs from disk — live reload (empirically proven).
        private void ReloadSelf()
        {
            var self = ActGlobals.oFormActMain.PluginGetSelfData(this);
            ActGlobals.oFormActMain.Invoke((MethodInvoker)(() =>
            {
                self.cbEnabled.Checked = false;
                self.cbEnabled.Checked = true;
            }));
        }

    }
}
