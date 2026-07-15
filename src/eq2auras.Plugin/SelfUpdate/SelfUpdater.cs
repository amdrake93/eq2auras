using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using Eq2Auras.Core.SelfUpdate;

namespace Eq2Auras.Plugin.SelfUpdate
{
    /// Downloads the selected channel's release DLL and live-reloads the plugin.
    /// Public repo: no token, no Authorization header — assets come from the public
    /// browser_download_url. The install decision is identity equality on the release
    /// name (the version), never version ordering (SPEC §Release channels).
    ///
    /// ⚠ SCAN-SAFETY RULE — everything here is deliberately SYNCHRONOUS (sync-over-async
    /// on a background thread). No async: hoisted state-machine fields are a scan-time
    /// hazard. Core types appear only as locals, never fields.
    public sealed class SelfUpdater
    {
        private const string Owner = "amdrake93";
        private const string Repo = "eq2auras";

        private readonly Action<string> _status;   // caller marshals to the UI thread
        private readonly Action _applyReload;       // caller toggles cbEnabled on the UI thread

        public SelfUpdater(Action<string> status, Action applyReload)
        {
            _status = status;
            _applyReload = applyReload;
        }

        /// Manual "check for updates": install the selected channel's build if its
        /// identity differs from what is running; otherwise report already-up-to-date.
        public void RunInBackground(string pluginsDir, bool betaChannel, string installedVersion)
        {
            Task_Run(() =>
            {
                try { Run(pluginsDir, betaChannel, installedVersion); }
                catch (Exception ex) { _status("update failed: " + ex.Message); }
            });
        }

        /// Startup notify: no download, no reload. Calls onUpdateAvailable(version) only
        /// when the channel has a build whose identity differs from installedVersion.
        public void CheckInBackground(bool betaChannel, string installedVersion, Action<string> onUpdateAvailable)
        {
            Task_Run(() =>
            {
                try
                {
                    var release = FetchRelease(betaChannel);
                    if (release != null && UpdateDecision.UpdateAvailable(installedVersion, release.Name))
                    {
                        onUpdateAvailable(release.Name);
                    }
                }
                catch { /* notify is best-effort; never surface a startup error */ }
            });
        }

        private void Run(string pluginsDir, bool betaChannel, string installedVersion)
        {
            var tag = UpdateDecision.TagForChannel(betaChannel);
            var release = FetchRelease(betaChannel);
            if (release == null)
            {
                _status("no " + tag + " release yet");
                return;
            }
            if (!UpdateDecision.UpdateAvailable(installedVersion, release.Name))
            {
                _status("already up to date (v" + installedVersion + ")");
                return;
            }

            using (var http = NewClient())
            {
                var downloaded = new List<KeyValuePair<string, byte[]>>();
                foreach (var asset in release.Assets)
                {
                    _status("downloading " + asset.Name + "…");
                    downloaded.Add(new KeyValuePair<string, byte[]>(
                        asset.Name, http.GetByteArrayAsync(asset.BrowserDownloadUrl).GetAwaiter().GetResult()));
                }

                // All-or-nothing: only touch disk once every download succeeded.
                foreach (var file in downloaded)
                {
                    File.WriteAllBytes(Path.Combine(pluginsDir, file.Key), file.Value);
                }
            }

            _status("update v" + release.Name + " installed — reloading…");
            _applyReload();
        }

        /// Returns the channel release, or null if the tag has no release yet
        /// (e.g. stable before the first promotion).
        private ReleaseManifest FetchRelease(bool betaChannel)
        {
            var tag = UpdateDecision.TagForChannel(betaChannel);
            using (var http = NewClient())
            {
                var url = "https://api.github.com/repos/" + Owner + "/" + Repo + "/releases/tags/" + tag;
                using (var response = http.GetAsync(url).GetAwaiter().GetResult())
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
                    response.EnsureSuccessStatusCode();
                    var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    return ReleaseManifest.Parse(json);
                }
            }
        }

        private static HttpClient NewClient()
        {
            var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("eq2auras-updater");   // GitHub requires a UA; no auth on a public repo
            return http;
        }

        // Local wrapper so the file has no `using System.Threading.Tasks;` at type scope;
        // keeps Task types out of field-adjacent positions (scan-safety belt-and-suspenders).
        private static void Task_Run(Action body) => System.Threading.Tasks.Task.Run(body);
    }
}
