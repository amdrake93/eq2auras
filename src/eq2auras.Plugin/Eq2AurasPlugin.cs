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
        private EscalationTracker _tracker;
        private Settings _settings;

        public void InitPlugin(TabPage pluginScreenSpace, Label pluginStatusText)
        {
            _statusLabel = pluginStatusText;
            var version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? "unknown";

            _log = new JsonlLogWriter();
            _overlay = new OverlayHost();
            _overlay.Start();
            _settings = SettingsStore.Load();
            _tracker = new EscalationTracker(_settings);   // touched only on ACT's UI thread (the poll)
            _probe = new TimerProbe(_log,
                readings => _overlay.UpdateFrame(
                    _tracker.Tick(readings)));

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
            _tracker = null;
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

            // Knob controls (SPEC §Configuration) — dropdown changes apply live within a
            // poll tick (the tracker reads the same Settings instance; controls and poll
            // share ACT's UI thread) and persist immediately.
            var colorLabel = new Label { Text = "Colors:", Left = 10, Top = 82, Width = 70 };
            var colorBox = new ComboBox
            {
                Left = 85, Top = 78, Width = 150,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            colorBox.Items.AddRange(new object[] { "Palette", "Greyscale", "ACT colors" });
            colorBox.SelectedIndex = (int)_settings.ColorSource;
            colorBox.SelectedIndexChanged += (s, e) =>
            {
                _settings.ColorSource = (ColorSource)colorBox.SelectedIndex;
                SettingsStore.Save(_settings);
            };

            var styleLabel = new Label { Text = "Escalation:", Left = 10, Top = 112, Width = 70 };
            var styleBox = new ComboBox
            {
                Left = 85, Top = 108, Width = 150,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            styleBox.Items.AddRange(new object[] { "Center radial", "Highlight in place" });
            styleBox.SelectedIndex = (int)_settings.EscalationStyle;
            styleBox.SelectedIndexChanged += (s, e) =>
            {
                _settings.EscalationStyle = (EscalationStyle)styleBox.SelectedIndex;
                SettingsStore.Save(_settings);
            };

            tab.Controls.Add(tokenBox);
            tab.Controls.Add(saveTokenButton);
            tab.Controls.Add(updateButton);
            tab.Controls.Add(colorLabel);
            tab.Controls.Add(colorBox);
            tab.Controls.Add(styleLabel);
            tab.Controls.Add(styleBox);
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
