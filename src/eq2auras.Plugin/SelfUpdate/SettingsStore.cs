using System;
using System.IO;
using Advanced_Combat_Tracker;
using Eq2Auras.Core.Config;

namespace Eq2Auras.Plugin.SelfUpdate
{
    public static class SettingsStore
    {
        private static readonly object Gate = new object();

        private static string PathOnDisk => Path.Combine(
            ActGlobals.oFormActMain.AppDataFolder.FullName, "eq2auras", "settings.json");

        public static Settings Load()
        {
            if (!File.Exists(PathOnDisk)) return new Settings();
            return Settings.Parse(File.ReadAllText(PathOnDisk));
        }

        /// ALL settings mutation goes through here: the mutate action and the file
        /// write share one gate, so writers on ACT's UI thread (tab knobs) and the
        /// overlay STA thread (drag-end positions) can't interleave or tear the file.
        public static void Update(Settings settings, Action mutate)
        {
            lock (Gate)
            {
                mutate();
                Directory.CreateDirectory(Path.GetDirectoryName(PathOnDisk));
                File.WriteAllText(PathOnDisk, settings.ToJson());
            }
        }
    }
}
