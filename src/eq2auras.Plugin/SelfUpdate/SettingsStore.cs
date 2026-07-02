using System.IO;
using Advanced_Combat_Tracker;
using Eq2Auras.Core.Config;

namespace Eq2Auras.Plugin.SelfUpdate
{
    public static class SettingsStore
    {
        private static string PathOnDisk => Path.Combine(
            ActGlobals.oFormActMain.AppDataFolder.FullName, "eq2auras", "settings.json");

        public static Settings Load()
        {
            if (!File.Exists(PathOnDisk)) return new Settings();
            return Settings.Parse(File.ReadAllText(PathOnDisk));
        }

        public static void Save(Settings settings)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathOnDisk));
            File.WriteAllText(PathOnDisk, settings.ToJson());
        }
    }
}
