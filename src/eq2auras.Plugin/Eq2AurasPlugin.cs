using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using Advanced_Combat_Tracker;
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

        public void InitPlugin(TabPage pluginScreenSpace, Label pluginStatusText)
        {
            // MUST be first: lets the CLR find eq2auras.Core.dll in the Plugins folder
            // (ACT does not probe there for plugin dependencies).
            PluginAssemblyResolver.EnsureRegistered();

            _statusLabel = pluginStatusText;
            var version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? "unknown";

            _log = new JsonlLogWriter();
            _probe = new TimerProbe(_log);
            _overlay = new OverlayHost();
            _overlay.Start();

            pluginScreenSpace.Text = "eq2auras";
            BuildConfigTab(pluginScreenSpace);
            _statusLabel.Text = "eq2auras v" + version + " | core=" + ReadCoreMarkerSafe()
                + " | logging to " + _log.FilePath;
        }

        public void DeInitPlugin()
        {
            _overlay?.Dispose();
            _overlay = null;
            _probe?.Dispose();
            _probe = null;
            _log?.Dispose();
            _log = null;
            PluginAssemblyResolver.Unregister(); // last: teardown above may still touch Core types
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

            tab.Controls.Add(tokenBox);
            tab.Controls.Add(saveTokenButton);
            tab.Controls.Add(updateButton);
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

        // Reload-probe tracer: if a live reload serves a stale cached Core, the new
        // CoreBuildInfo member won't exist there and the JIT throws when this method
        // is compiled — NoInlining keeps that failure catchable at the call site.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string ReadCoreMarker() => Eq2Auras.Core.CoreBuildInfo.Marker;

        private static string ReadCoreMarkerSafe()
        {
            try { return ReadCoreMarker(); }
            catch (Exception) { return "STALE"; }
        }
    }
}
