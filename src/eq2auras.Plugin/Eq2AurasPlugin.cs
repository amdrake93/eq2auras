using System.Reflection;
using System.Windows.Forms;
using Advanced_Combat_Tracker;
using Eq2Auras.Plugin.Act;
using Eq2Auras.Plugin.Diagnostics;
using Eq2Auras.Plugin.Overlay;

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
            _statusLabel.Text = "eq2auras v" + version + " loaded — logging to " + _log.FilePath;
        }

        public void DeInitPlugin()
        {
            _overlay?.Dispose();
            _overlay = null;
            _probe?.Dispose();
            _probe = null;
            _log?.Dispose();
            _log = null;
            if (_statusLabel != null) _statusLabel.Text = "eq2auras unloaded";
        }
    }
}
