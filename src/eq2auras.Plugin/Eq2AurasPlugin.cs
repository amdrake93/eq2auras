using System.Reflection;
using System.Windows.Forms;
using Advanced_Combat_Tracker;

namespace Eq2Auras.Plugin
{
    public class Eq2AurasPlugin : IActPluginV1
    {
        private Label _statusLabel;

        public void InitPlugin(TabPage pluginScreenSpace, Label pluginStatusText)
        {
            _statusLabel = pluginStatusText;
            var version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? "unknown";

            pluginScreenSpace.Text = "eq2auras";
            _statusLabel.Text = "eq2auras v" + version + " loaded";
        }

        public void DeInitPlugin()
        {
            if (_statusLabel != null)
            {
                _statusLabel.Text = "eq2auras unloaded";
            }
        }
    }
}
