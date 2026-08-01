using System.IO;
using Advanced_Combat_Tracker;
using Eq2Auras.Core.Meter;

namespace Eq2Auras.Plugin.SelfUpdate
{
    /// Persistence for the learned name→class cache (SPEC Part III §Class colors, §Settings): its own
    /// DCJS file beside settings.json, eager-loaded at init, flushed with confident diffs at encounter
    /// end. Same shape as SettingsStore; a missing/corrupt file loads to an empty cache (self-heals).
    public static class ClassCacheStore
    {
        private static readonly object Gate = new object();

        private static string PathOnDisk => Path.Combine(
            ActGlobals.oFormActMain.AppDataFolder.FullName, "eq2auras", "learned-classes.json");

        public static ClassCache Load()
        {
            try { return File.Exists(PathOnDisk) ? ClassCache.Parse(File.ReadAllText(PathOnDisk)) : new ClassCache(); }
            catch { return new ClassCache(); }
        }

        public static void Save(ClassCache cache)
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PathOnDisk));
                File.WriteAllText(PathOnDisk, cache.ToJson());
            }
        }
    }
}
