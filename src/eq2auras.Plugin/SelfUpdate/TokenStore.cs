using System.IO;
using System.Security.Cryptography;
using System.Text;
using Advanced_Combat_Tracker;

namespace Eq2Auras.Plugin.SelfUpdate
{
    /// Stores the fine-grained GitHub PAT encrypted at rest with DPAPI (per-user).
    /// Scope discipline: the token must be contents:read on the eq2auras repo only.
    public static class TokenStore
    {
        private static string PathOnDisk => Path.Combine(
            ActGlobals.oFormActMain.AppDataFolder.FullName, "eq2auras", "token.bin");

        public static void Save(string token)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathOnDisk));
            byte[] encrypted = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(PathOnDisk, encrypted);
        }

        public static string Load()
        {
            if (!File.Exists(PathOnDisk)) return null;
            byte[] decrypted = ProtectedData.Unprotect(
                File.ReadAllBytes(PathOnDisk), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
    }
}
