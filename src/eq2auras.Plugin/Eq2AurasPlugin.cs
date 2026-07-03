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
            var panelBBox = BuildPanelGroupBox("Panel B", _settings.Panels[1], 176);

            var moveBox = new CheckBox { Text = "Move overlay windows", Left = 10, Top = 276, Width = 200 };
            moveBox.CheckedChanged += (s, e) => _overlay.SetMoveMode(moveBox.Checked);

            tab.Controls.Add(tokenBox);
            tab.Controls.Add(saveTokenButton);
            tab.Controls.Add(updateButton);
            tab.Controls.Add(panelABox);
            tab.Controls.Add(panelBBox);
            tab.Controls.Add(moveBox);
        }

        /// One labeled control set per group (SPEC §Configuration — no group selector).
        /// Dropdown changes apply live within a poll tick: the engine's trackers hold
        /// the same PanelSettings instance this mutates, and knob handlers + poll share
        /// ACT's UI thread. Persistence goes through the SettingsStore gate.
        private GroupBox BuildPanelGroupBox(string title, PanelSettings panel, int top)
        {
            var box = new GroupBox { Text = title, Left = 10, Top = top, Width = 250, Height = 90 };

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

            box.Controls.Add(colorLabel);
            box.Controls.Add(colorBox);
            box.Controls.Add(styleLabel);
            box.Controls.Add(styleBox);
            return box;
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
