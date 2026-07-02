using System;
using System.IO;
using System.Reflection;
using Advanced_Combat_Tracker;

namespace Eq2Auras.Plugin
{
    /// ACT loads the plugin assembly in a way that does not add the Plugins folder to the
    /// CLR's probe path (it loads from bytes — no file location), so our dependency
    /// eq2auras.Core.dll — sitting right next to eq2auras.dll — is "not found" the moment a
    /// Core type is first used. This resolves any of our own dependencies from the Plugins
    /// folder explicitly. Register it before any Core type is touched (first line of InitPlugin).
    internal static class PluginAssemblyResolver
    {
        private static bool _registered;
        private static string _pluginDir;

        public static void EnsureRegistered()
        {
            if (_registered) return;
            _registered = true;
            _pluginDir = Path.Combine(ActGlobals.oFormActMain.AppDataFolder.FullName, "Plugins");
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
        }

        private static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            var simpleName = new AssemblyName(args.Name).Name;
            var candidate = Path.Combine(_pluginDir, simpleName + ".dll");
            return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
        }
    }
}
