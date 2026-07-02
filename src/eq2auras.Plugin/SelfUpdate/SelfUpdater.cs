using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Eq2Auras.Core.SelfUpdate;

namespace Eq2Auras.Plugin.SelfUpdate
{
    /// Downloads the dev-latest release's DLLs and live-reloads the plugin.
    /// Flow (all empirically proven): fetch release JSON -> download every asset to
    /// memory -> write all files into the Plugins folder (no locks: ACT byte-loads the
    /// plugin, our resolver byte-loads Core) -> toggle our own Enabled checkbox, which
    /// re-reads the files from disk and runs the new bytes. No ACT restart.
    ///
    /// ⚠ SCAN-SAFETY RULE — everything here is deliberately SYNCHRONOUS (sync-over-async
    /// on the background thread). ACT's plugin scan (Assembly.GetTypes, BEFORE InitPlugin
    /// registers our AssemblyResolve handler) resolves the types of all FIELDS in the
    /// assembly — and `async` methods hoist awaited locals into fields of hidden
    /// state-machine structs. An async method with a Core-typed local (e.g.
    /// ReleaseManifest) makes the scan demand eq2auras.Core.dll and fail. No async, no
    /// hoisted fields, no scan-time Core dependency. The same rule forbids Core/non-GAC
    /// types in ordinary fields anywhere in this assembly.
    public sealed class SelfUpdater
    {
        private const string Owner = "amdrake93";
        private const string Repo = "eq2auras";
        private const string Tag = "dev-latest";

        private readonly Action<string> _status;   // caller marshals to the UI thread
        private readonly Action _applyReload;      // caller toggles cbEnabled on the UI thread

        public SelfUpdater(Action<string> status, Action applyReload)
        {
            _status = status;
            _applyReload = applyReload;
        }

        /// Manual "check for updates": always downloads and reloads (spike behaviour;
        /// a published_at gate for auto-checks is a later refinement).
        public void RunInBackground(string pluginsDir)
        {
            Task.Run(() =>
            {
                try { Run(pluginsDir); }
                catch (Exception ex) { _status("update failed: " + ex.Message); }
            });
        }

        private void Run(string pluginsDir)
        {
            var token = TokenStore.Load();
            if (token == null)
            {
                _status("no update token saved — paste it on the eq2auras tab first");
                return;
            }

            using (var http = new HttpClient())
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("eq2auras-updater");
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                _status("checking " + Tag + "…");
                var releaseJson = http.GetStringAsync(
                    "https://api.github.com/repos/" + Owner + "/" + Repo + "/releases/tags/" + Tag)
                    .GetAwaiter().GetResult();
                var release = ReleaseManifest.Parse(releaseJson);

                var downloaded = new List<KeyValuePair<string, byte[]>>();
                foreach (var asset in release.Assets)
                {
                    _status("downloading " + asset.Name + "…");
                    downloaded.Add(new KeyValuePair<string, byte[]>(
                        asset.Name, DownloadAsset(http, asset.ApiUrl)));
                }

                // All-or-nothing: only touch disk once every download succeeded.
                foreach (var file in downloaded)
                {
                    File.WriteAllBytes(Path.Combine(pluginsDir, file.Key), file.Value);
                }

                _status("update " + release.PublishedAt + " installed — reloading…");
                _applyReload();
            }
        }

        private static byte[] DownloadAsset(HttpClient http, string apiUrl)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, apiUrl))
            {
                request.Headers.Accept.Clear();
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
                using (var response = http.SendAsync(request).GetAwaiter().GetResult())
                {
                    response.EnsureSuccessStatusCode();
                    return response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                }
            }
        }
    }
}
