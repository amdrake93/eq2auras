using System;
using System.Collections.Generic;
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

        /// Call from DeInitPlugin. Live reload loads a fresh assembly instance (own statics),
        /// so without this each reload would stack another orphaned handler on the AppDomain.
        public static void Unregister()
        {
            if (!_registered) return;
            _registered = false;
            AppDomain.CurrentDomain.AssemblyResolve -= Resolve;
        }

        private static readonly Dictionary<string, Assembly> _loaded = new Dictionary<string, Assembly>();

        private static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            var simpleName = new AssemblyName(args.Name).Name;
            lock (_loaded)
            {
                if (_loaded.TryGetValue(simpleName, out var cached)) return cached;

                var candidate = Path.Combine(_pluginDir, simpleName + ".dll");
                if (!File.Exists(candidate)) return null;

                // Byte-load, never LoadFrom: LoadFrom keeps the file LOCKED for the process
                // lifetime (blocking live updates of the dependency) and path-caches (serving
                // a stale assembly across reloads). Byte arrays also carry no mark-of-the-web.
                var asm = Assembly.Load(File.ReadAllBytes(candidate));
                _loaded[simpleName] = asm;
                return asm;
            }
        }
    }
}
