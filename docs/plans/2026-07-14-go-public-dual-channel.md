# Go Public + Dual Release Channels Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make eq2auras installable and self-updating with no GitHub token by taking the repo public, and split releases into a public **stable** channel and a bleeding-edge **beta** (`dev-latest`) channel selected by one checkbox.

**Architecture:** The self-updater stops authenticating (public assets download via `browser_download_url`) and targets a per-channel tag (`stable` | `dev-latest`), installing by **identity equality on the version string — never version ordering** (so beta→stable going numerically backwards installs). Stable is an in-the-moment **promotion** of an already-built dev artifact (same bytes, no recompile) via a manual `promote.yml`. The vendored ACT exe is untracked and CI fetches ACT's own latest public release at build time.

**Tech Stack:** .NET (Core: netstandard2.0 logic, tested on Mac via xUnit `net10.0` test project; Plugin: `net472` + WPF + WinForms, Windows/CI-only), GitHub Actions (`windows-latest` build, `ubuntu-latest` promote), `DataContractJsonSerializer` for all JSON.

## Global Constraints

Copied from SPEC — every task's requirements implicitly include these:

- **Single-assembly packaging.** Core sources are `<Compile Include>`d into the plugin; never reference a second DLL, never `Assembly.LoadFrom`.
- **No `System.Web.Extensions`** anywhere — it breaks the WPF XAML markup compiler. JSON = `DataContractJsonSerializer` only.
- **Keep the self-update path synchronous** (`Task.Run` + a synchronous body, as today). No `async` in the plugin project — hoisted state-machine fields are a scan-time hazard; follow the existing `SelfUpdater` pattern exactly.
- **DCJS skips field initializers on deserialize** → a knob's default must be its **0-value** (enum 0, `bool false`). Missing fields come back as 0.
- **Two-machine reality:** the Plugin/solution **cannot be built or tested on the Mac** (net472+WPF). Only `eq2auras.Core` is locally testable: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`. Plugin/CI tasks below are therefore verified by **branch CI (compile + artifact)** and the **live script (Task 10)** — they carry no local unit tests, by construction, not omission.
- **Branch/release flow:** work stays on branch `go-public-dual-channel`; branch pushes run verify-only CI; merging `main` is releasing and is the owner's explicit gate.
- **Owner-only steps** (repo visibility flip, first stable promotion, live verification) are runbook items in Task 10, never automated here.

---

## Rollout sequence (why task order matters)

The tasks land together on the branch, but the **go-live order after merge** is load-bearing and is scripted in Task 10. The dependency that drives it:

- The exe-untrack + CI-ACT-fetch (Task 1) must be green **before** the repo is public, or CI can't build without the vendored binary.
- The no-token updater (Task 5) only *works* once the repo is public (`browser_download_url` on a private asset 401s). So existing installs keep updating through their **old** token-based updater until the flip; the flip happens right after the new build lands on `main`; only then does the new updater's public path work.
- The default channel is **stable**, so a `stable` release must exist before public users install — the **first promotion** (Task 8 workflow, run per Task 10) happens right after the flip. Until then the updater reports "no stable release yet" (handled in Task 5).

---

## Task 1: CI fetches live ACT; untrack the vendored exe; stamp release name with the version

**Files:**
- Modify: `.github/workflows/build.yml` (add ACT-fetch steps before the msbuild step at `:29-30`; change publish `name:` at `:48`)
- Modify: `.gitignore:8` region (stop excepting the exe; it is `*.dll`-ignored already, so just ensure no tracked copy remains)
- Delete from git index (keep nothing): `ThirdParty/Advanced Combat Tracker.exe`

**Interfaces:**
- Produces: a `dev-latest` release whose **`name` is the version string** (`0.1.<run_number>`), consumed as the identity token by Task 5; a `ThirdParty/Advanced Combat Tracker.exe` present at build time from ACT's latest public release.

- [ ] **Step 1: Untrack the vendored ACT exe (keep the working-tree file for reference; git stops tracking it)**

```bash
git rm --cached "ThirdParty/Advanced Combat Tracker.exe"
```

- [ ] **Step 2: Ignore the exe so it is never re-committed**

Add to `.gitignore` (below the existing `*.dll` block, with a comment):

```gitignore
# ACT is fetched by CI from its own public release, never vendored (SPEC §Building against live ACT)
/ThirdParty/Advanced Combat Tracker.exe
```

- [ ] **Step 3: Add ACT-fetch steps to `build.yml` immediately before the `Build the plugin` step**

Insert after the `actions/setup-dotnet@v4` step (`build.yml:22-24`) and before `Run Core unit tests`:

```yaml
      - name: Resolve ACT latest release tag
        id: act
        shell: bash
        env:
          GH_TOKEN: ${{ github.token }}
        run: echo "tag=$(gh release view --repo EQAditu/AdvancedCombatTracker --json tagName -q .tagName)" >> "$GITHUB_OUTPUT"

      - name: Cache ACT zip (keyed on the resolved tag)
        id: actcache
        uses: actions/cache@v4
        with:
          path: act.zip
          key: act-${{ steps.act.outputs.tag }}

      - name: Download ACT if not cached
        if: steps.actcache.outputs.cache-hit != 'true'
        shell: bash
        env:
          GH_TOKEN: ${{ github.token }}
        run: gh release download "${{ steps.act.outputs.tag }}" --repo EQAditu/AdvancedCombatTracker --pattern ACTv3.zip --output act.zip --clobber

      - name: Extract ACT exe to the reference HintPath
        shell: pwsh
        run: |
          Write-Host "Building against ACT ${{ steps.act.outputs.tag }}"
          New-Item -ItemType Directory -Force -Path ThirdParty | Out-Null
          Expand-Archive -Path act.zip -DestinationPath act-extracted -Force
          Copy-Item "act-extracted/Advanced Combat Tracker.exe" "ThirdParty/Advanced Combat Tracker.exe" -Force
          if (-not (Test-Path "ThirdParty/Advanced Combat Tracker.exe")) { throw "ACT exe not found in ACTv3.zip" }
```

(Rationale recorded in SPEC §Building against live ACT: latest, not pinned, so an ACT API break is a loud build failure; cache is a network fallback; the "Building against ACT <tag>" line records the version compiled against.)

- [ ] **Step 4: Stamp the release name with the version**

In `build.yml`, the `Publish rolling prerelease` step, change the `name:` line (`build.yml:48`) from `name: dev-latest` to:

```yaml
          name: ${{ steps.ver.outputs.version }}
```

Leave `tag_name: dev-latest` and `prerelease: true` unchanged. (The tag stays rolling; the display name becomes the version — the identity token Task 5 compares.)

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "CI: fetch live ACT + untrack vendored exe; stamp dev-latest release name with version"
```

- [ ] **Step 6: Verify on the branch**

Push the branch; watch CI: `gh run watch <id> --exit-status`. Expected: green — the "Extract ACT exe" step prints `Building against ACT <tag>`, msbuild compiles against the fetched exe, and the artifact uploads. (No `dev-latest` publish on a branch — that is main-only.)

---

## Task 2: Core — ReleaseManifest gains `name` and `browser_download_url`

**Files:**
- Modify: `src/eq2auras.Core/SelfUpdate/ReleaseManifest.cs` (add `Name` to `ReleaseManifest`; add `BrowserDownloadUrl` to `ReleaseAsset`)
- Test: `tests/eq2auras.Core.Tests/ReleaseManifestTests.cs`

**Interfaces:**
- Produces: `ReleaseManifest.Name` (string, from JSON `name`), `ReleaseAsset.BrowserDownloadUrl` (string, from JSON `browser_download_url`) — consumed by Task 5.

- [ ] **Step 1: Write the failing test**

Add to `tests/eq2auras.Core.Tests/ReleaseManifestTests.cs`:

```csharp
[Fact]
public void Parse_reads_release_name_and_asset_browser_download_url()
{
    var json = @"{
        ""tag_name"": ""dev-latest"",
        ""name"": ""0.1.150"",
        ""published_at"": ""2026-07-14T00:00:00Z"",
        ""assets"": [{
            ""name"": ""eq2auras.dll"",
            ""url"": ""https://api.github.com/repos/x/y/releases/assets/1"",
            ""browser_download_url"": ""https://github.com/x/y/releases/download/dev-latest/eq2auras.dll""
        }]
    }";

    var manifest = ReleaseManifest.Parse(json);

    Assert.Equal("0.1.150", manifest.Name);
    Assert.Equal(
        "https://github.com/x/y/releases/download/dev-latest/eq2auras.dll",
        manifest.Assets[0].BrowserDownloadUrl);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter "FullyQualifiedName~ReleaseManifestTests.Parse_reads_release_name"`
Expected: FAIL (no `Name` / `BrowserDownloadUrl` members).

- [ ] **Step 3: Add the members**

In `ReleaseManifest` (after `TagName`):

```csharp
        [DataMember(Name = "name")]
        public string Name { get; set; }
```

In `ReleaseAsset` (after `ApiUrl`):

```csharp
        [DataMember(Name = "browser_download_url")]
        public string BrowserDownloadUrl { get; set; }
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter "FullyQualifiedName~ReleaseManifestTests"`
Expected: PASS (all ReleaseManifest tests).

- [ ] **Step 5: Commit**

```bash
git add src/eq2auras.Core/SelfUpdate/ReleaseManifest.cs tests/eq2auras.Core.Tests/ReleaseManifestTests.cs
git commit -m "Core: ReleaseManifest reads release name + asset browser_download_url"
```

---

## Task 3: Core — UpdateDecision (channel→tag + identity-equality install rule)

**Files:**
- Create: `src/eq2auras.Core/SelfUpdate/UpdateDecision.cs`
- Test: `tests/eq2auras.Core.Tests/UpdateDecisionTests.cs`

**Interfaces:**
- Produces:
  - `static string UpdateDecision.TagForChannel(bool betaChannel)` → `"dev-latest"` when true, `"stable"` when false.
  - `static bool UpdateDecision.UpdateAvailable(string installedVersion, string releaseVersion)` → true iff `releaseVersion` is non-empty and not equal to `installedVersion` (ordinal string equality — never numeric comparison).
- Consumed by Task 5 (SelfUpdater) and Task 7 (notify).

- [ ] **Step 1: Write the failing tests**

Create `tests/eq2auras.Core.Tests/UpdateDecisionTests.cs`:

```csharp
using Eq2Auras.Core.SelfUpdate;
using Xunit;

public class UpdateDecisionTests
{
    [Theory]
    [InlineData(true, "dev-latest")]
    [InlineData(false, "stable")]
    public void TagForChannel_maps_beta_flag_to_tag(bool beta, string expectedTag)
    {
        Assert.Equal(expectedTag, UpdateDecision.TagForChannel(beta));
    }

    [Theory]
    [InlineData("0.1.96", "0.1.97", true)]    // dev advanced
    [InlineData("0.1.200", "0.1.150", true)]  // beta -> stable, numerically BACKWARD, must install
    [InlineData("0.1.150", "0.1.150", false)] // same identity -> already up to date
    public void UpdateAvailable_is_identity_inequality_not_ordering(
        string installed, string release, bool expected)
    {
        Assert.Equal(expected, UpdateDecision.UpdateAvailable(installed, release));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void UpdateAvailable_is_false_when_release_version_is_missing(string release)
    {
        Assert.False(UpdateDecision.UpdateAvailable("0.1.96", release));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter "FullyQualifiedName~UpdateDecisionTests"`
Expected: FAIL (type `UpdateDecision` does not exist).

- [ ] **Step 3: Write the implementation**

Create `src/eq2auras.Core/SelfUpdate/UpdateDecision.cs`:

```csharp
namespace Eq2Auras.Core.SelfUpdate
{
    /// Pure update-targeting rules (SPEC §Release channels & public distribution).
    /// Kept in Core so the identity-not-ordering contract is unit-tested on the Mac.
    public static class UpdateDecision
    {
        public const string BetaTag = "dev-latest";
        public const string StableTag = "stable";

        public static string TagForChannel(bool betaChannel)
            => betaChannel ? BetaTag : StableTag;

        /// Install iff the channel release's identity differs from what is installed.
        /// Equality only — NEVER numeric ordering: opting out of beta routinely moves
        /// numerically backward (0.1.200 -> 0.1.150) and must still install.
        public static bool UpdateAvailable(string installedVersion, string releaseVersion)
            => !string.IsNullOrEmpty(releaseVersion) && releaseVersion != installedVersion;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter "FullyQualifiedName~UpdateDecisionTests"`
Expected: PASS (all 8 cases).

- [ ] **Step 5: Commit**

```bash
git add src/eq2auras.Core/SelfUpdate/UpdateDecision.cs tests/eq2auras.Core.Tests/UpdateDecisionTests.cs
git commit -m "Core: UpdateDecision — channel->tag map + identity-equality install rule"
```

---

## Task 4: Core — Settings gains the `BetaChannel` knob

**Files:**
- Modify: `src/eq2auras.Core/Config/Settings.cs` (add member after `DebugLogging` at `:31-32`)
- Test: `tests/eq2auras.Core.Tests/SettingsTests.cs`

**Interfaces:**
- Produces: `Settings.BetaChannel` (bool, default `false`), consumed by Task 6 (checkbox) and Task 7 (notify).

- [ ] **Step 1: Write the failing tests**

Add to `tests/eq2auras.Core.Tests/SettingsTests.cs`:

```csharp
[Fact]
public void BetaChannel_defaults_to_false_on_a_file_that_omits_it()
{
    // DCJS skips initializers; a missing bool must come back as the 0-value (false = stable).
    var settings = Settings.Parse("{}");
    Assert.False(settings.BetaChannel);
}

[Fact]
public void BetaChannel_survives_a_round_trip()
{
    var settings = Settings.Parse("{}");
    settings.BetaChannel = true;

    var reparsed = Settings.Parse(settings.ToJson());

    Assert.True(reparsed.BetaChannel);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter "FullyQualifiedName~SettingsTests.BetaChannel"`
Expected: FAIL (no `BetaChannel` member).

- [ ] **Step 3: Add the knob**

In `Settings.cs`, immediately after the `DebugLogging` property (`:31-32`):

```csharp
        [DataMember(Name = "betaChannel")]
        public bool BetaChannel { get; set; }   // global knob: false (0-value) = stable channel (SPEC §Two channels)
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter "FullyQualifiedName~SettingsTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/eq2auras.Core/Config/Settings.cs tests/eq2auras.Core.Tests/SettingsTests.cs
git commit -m "Core: Settings.BetaChannel knob (default false = stable)"
```

---

## Task 5: Plugin — SelfUpdater goes tokenless, channel-aware, identity-gated; adds a no-install check

**Files:**
- Modify: `src/eq2auras.Plugin/SelfUpdate/SelfUpdater.cs` (full rework)

**Interfaces:**
- Consumes: `UpdateDecision.TagForChannel`, `UpdateDecision.UpdateAvailable` (Task 3); `ReleaseManifest.Name`, `ReleaseAsset.BrowserDownloadUrl` (Task 2).
- Produces:
  - `void RunInBackground(string pluginsDir, bool betaChannel, string installedVersion)` — check + download + reload (identity-gated).
  - `void CheckInBackground(bool betaChannel, string installedVersion, Action<string> onUpdateAvailable)` — no-install check for notify (Task 7); invokes the callback with the available version string only when an update exists.

**Verification:** no local unit test is possible (WPF/net472 + live HTTP). Verified by branch CI compile (this task) and the Task 10 live script. Keep the whole file **synchronous** per Global Constraints.

- [ ] **Step 1: Replace the class body**

Rewrite `src/eq2auras.Plugin/SelfUpdate/SelfUpdater.cs` to:

```csharp
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
```

Notes for the implementer:
- The token gate (`TokenStore.Load()` / `Authorization` header / `asset.ApiUrl` + octet-stream `Accept`) is **removed entirely** — public assets need none of it. `browser_download_url` returns a redirect to the binary; `HttpClient` follows it by default.
- `TokenStore` is now unreferenced from here; it is deleted in Task 6 (after its other caller — the tab's save button — is removed).

- [ ] **Step 2: Verify compile on the branch**

This cannot compile locally. Commit and push with Task 6 & 7 (they share the tab wiring), then confirm the branch CI `Build the plugin` step is green. Expected: the plugin compiles; no reference to `TokenStore` remains after Task 6.

- [ ] **Step 3: Commit**

```bash
git add src/eq2auras.Plugin/SelfUpdate/SelfUpdater.cs
git commit -m "Plugin: SelfUpdater tokenless + channel-aware + identity-gated; add no-install check"
```

---

## Task 6: Plugin — tab surface: drop token UI, add Beta checkbox, delete TokenStore

**Files:**
- Modify: `src/eq2auras.Plugin/Eq2AurasPlugin.cs` (`BuildConfigTab` at `:63-112` — remove token controls `:69-77`; change the update button handler `:79-81`; add a Beta checkbox)
- Delete: `src/eq2auras.Plugin/SelfUpdate/TokenStore.cs`

**Interfaces:**
- Consumes: `Settings.BetaChannel` (Task 4); `SelfUpdater.RunInBackground(pluginsDir, betaChannel, installedVersion)` (Task 5); `SettingsStore.Update` (existing).
- Produces: a persisted, live-toggle Beta checkbox on the tab.

**Verification:** CI compile + Task 10 live script.

- [ ] **Step 1: Remove the token box and save-token button**

Delete `Eq2AurasPlugin.cs:69-77` (the `tokenBox`, `saveTokenButton`, its `Click` handler, and the `_statusLabel.Text = "update token saved (DPAPI)"` line) and remove the two controls from wherever they are added to the tab.

- [ ] **Step 2: Update the "Check for updates" handler to pass channel + version**

The plugin's version is already computed in `InitPlugin` (`:28-30`). Hold it in a field so the tab can pass it. Add a field near `_statusLabel`:

```csharp
        private string _version = "unknown";
```

In `InitPlugin`, change the local `var version = ...` (`:28-30`) to assign the field:

```csharp
            _version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? "unknown";
```

and update the status line at `:48` to use `_version`:

```csharp
            _statusLabel.Text = "eq2auras v" + _version + " | logging to " + _log.FilePath;
```

Change the update button handler (`:79-81`) to:

```csharp
            var updateButton = new Button { Left = 10, Top = 44, Width = 150, Text = "Check for updates" };
            updateButton.Click += (s, e) =>
                new SelfUpdater(SetStatusThreadSafe, ReloadSelf)
                    .RunInBackground(pluginsDir, _settings.BetaChannel, _version);
```

- [ ] **Step 3: Add the Beta channel checkbox (persist + re-check on toggle)**

Add near the update button (choose a free position in the update control group, e.g. `Top = 44, Left = 170`):

```csharp
            var betaCheck = new CheckBox
            {
                Left = 170, Top = 46, Width = 220,
                Text = "Beta channel (bleeding edge)",
                Checked = _settings.BetaChannel
            };
            betaCheck.CheckedChanged += (s, e) =>
            {
                SettingsStore.Update(_settings, () => _settings.BetaChannel = betaCheck.Checked);
                // Toggling triggers a check against the now-selected channel (SPEC §Updates target by channel identity).
                new SelfUpdater(SetStatusThreadSafe, ReloadSelf)
                    .RunInBackground(pluginsDir, _settings.BetaChannel, _version);
            };
```

Add `betaCheck` to the tab's controls alongside `updateButton`.

- [ ] **Step 4: Delete TokenStore (its last caller is now gone)**

```bash
git rm src/eq2auras.Plugin/SelfUpdate/TokenStore.cs
```

Confirm no references remain:

```bash
grep -rn "TokenStore" src/ && echo "STILL REFERENCED — do not proceed" || echo "clean"
```

Expected: `clean`.

- [ ] **Step 5: Commit**

```bash
git add src/eq2auras.Plugin/Eq2AurasPlugin.cs
git commit -m "Plugin: drop token UI, add Beta channel checkbox, delete TokenStore"
```

---

## Task 7: Plugin — notify-on-startup via the status string

**Files:**
- Modify: `src/eq2auras.Plugin/Eq2AurasPlugin.cs` (`InitPlugin`, after `BuildConfigTab` and the status-line assignment)

**Interfaces:**
- Consumes: `SelfUpdater.CheckInBackground(betaChannel, installedVersion, onUpdateAvailable)` (Task 5); `_settings.BetaChannel`, `_version`, `SetStatusThreadSafe` (existing).

**Verification:** CI compile + Task 10 live script.

- [ ] **Step 1: Kick a best-effort check at the end of `InitPlugin`**

After the `_statusLabel.Text = "eq2auras v" + _version + ...` line (`:48`), add:

```csharp
            // Notify-only startup check on the selected channel (SPEC §Notify on startup).
            // Best-effort, background; never blocks InitPlugin, never auto-installs.
            new SelfUpdater(SetStatusThreadSafe, ReloadSelf).CheckInBackground(
                _settings.BetaChannel, _version,
                available => SetStatusThreadSafe(
                    "update available: v" + available + " — click \"Check for updates\""));
```

(`SetStatusThreadSafe` already marshals to ACT's UI thread via `oFormActMain.Invoke`. The check reuses the synchronous `Task.Run` path; nothing here is `async`.)

- [ ] **Step 2: Verify compile on the branch**

Push (with Tasks 5–6); confirm CI `Build the plugin` is green.

- [ ] **Step 3: Commit**

```bash
git add src/eq2auras.Plugin/Eq2AurasPlugin.cs
git commit -m "Plugin: notify-on-startup update-available string (notify-only)"
```

---

## Task 8: CI — `promote.yml` (manual stable promotion of a dev build's exact bytes)

**Files:**
- Create: `.github/workflows/promote.yml`

**Interfaces:**
- Consumes: the `dev-latest` release whose `name` is the version and whose asset is `eq2auras.dll` (Task 1).
- Produces: a `stable` release (tag `stable`, `name` = the promoted version, `prerelease: false`) carrying the same `eq2auras.dll` bytes and recording the source commit SHA — the stable channel Task 5 fetches.

- [ ] **Step 1: Create the workflow**

Create `.github/workflows/promote.yml`:

```yaml
name: promote
on:
  workflow_dispatch:
    inputs:
      version:
        description: "Version to promote (blank = current dev-latest)"
        required: false
        default: ""

permissions:
  contents: write

jobs:
  promote:
    runs-on: ubuntu-latest
    steps:
      - name: Resolve the dev-latest build being promoted
        id: dev
        shell: bash
        env:
          GH_TOKEN: ${{ github.token }}
        run: |
          name="$(gh release view dev-latest --repo ${{ github.repository }} --json name -q .name)"
          sha="$(gh api repos/${{ github.repository }}/git/ref/tags/dev-latest -q .object.sha)"
          want="${{ github.event.inputs.version }}"
          if [ -n "$want" ] && [ "$want" != "$name" ]; then
            echo "::error::Requested $want but dev-latest currently holds $name. Accept the default to promote the current build, or restore that version's run artifact first (escape hatch)."
            exit 1
          fi
          echo "version=$name" >> "$GITHUB_OUTPUT"
          echo "sha=$sha" >> "$GITHUB_OUTPUT"

      - name: Download the dev-latest DLL (exact bytes — no recompile)
        shell: bash
        env:
          GH_TOKEN: ${{ github.token }}
        run: |
          mkdir dist
          gh release download dev-latest --repo ${{ github.repository }} --pattern eq2auras.dll --dir dist --clobber

      - name: Publish stable
        uses: softprops/action-gh-release@v2
        with:
          tag_name: stable
          name: ${{ steps.dev.outputs.version }}
          prerelease: false
          body: "Promoted from dev build ${{ steps.dev.outputs.version }} (source commit ${{ steps.dev.outputs.sha }})."
          files: dist/eq2auras.dll
```

Escape hatch (documented, not the default path): to promote a version older than current `dev-latest`, first restore that version's `dist/eq2auras.dll` from its build run's uploaded artifact (`actions/download-artifact` names them `eq2auras-<version>`, retained 90 days) instead of downloading from the `dev-latest` release, then run the Publish stable step. Kept out of the default flow to preserve "promote the build you just playtested."

- [ ] **Step 2: Commit**

```bash
git add .github/workflows/promote.yml
git commit -m "CI: promote.yml — manual stable promotion of a dev build's exact bytes"
```

- [ ] **Step 3: Verify (deferred to Task 10)**

`promote.yml` cannot run until the branch is on `main` and a `dev-latest` release exists. Its first real run is the first-stable step in the Task 10 runbook; that run is its verification.

---

## Task 9: CI/docs — README stable pill (independent, deferrable)

**Files:**
- Modify: `README.md` (split the status block into `devpill` / `stablepill` sub-markers)
- Modify: `.github/workflows/build.yml` (the README stamp rewrites only the `devpill` sub-block)
- Modify: `.github/workflows/promote.yml` (add a step that rewrites only the `stablepill` sub-block)

**Interfaces:** none consumed by code; purely the README status block. Lowest-priority task — the design calls it benign; it can ship after the channels work.

- [ ] **Step 1: Introduce sub-markers in `README.md`**

Inside the existing `<!-- status:begin --> … <!-- status:end -->` block, wrap the pills so each workflow owns one region:

```markdown
<!-- status:begin -->
<!-- devpill:begin -->[![build](https://github.com/amdrake93/eq2auras/actions/workflows/build.yml/badge.svg?branch=main)](https://github.com/amdrake93/eq2auras/actions/workflows/build.yml) ![dev](https://img.shields.io/badge/dev-0.1.0-56B4E9)<!-- devpill:end -->
<!-- stablepill:begin --> ![stable](https://img.shields.io/badge/stable-none-lightgrey)<!-- stablepill:end -->

`dev-latest` · [releases](https://github.com/amdrake93/eq2auras/releases) · [CI runs](https://github.com/amdrake93/eq2auras/actions) · [SPEC](docs/SPEC.md) · [backlog](docs/backlog.md)
<!-- status:end -->
```

- [ ] **Step 2: Point the build.yml stamp at the devpill sub-block only**

In `build.yml`'s `Stamp README status block` step, replace the whole-block `perl` substitution with one that targets `devpill`:

```bash
          export DEVPILL="<!-- devpill:begin -->[![build](https://github.com/amdrake93/eq2auras/actions/workflows/build.yml/badge.svg?branch=main)](https://github.com/amdrake93/eq2auras/actions/workflows/build.yml) ![dev](https://img.shields.io/badge/dev-${VERSION}-56B4E9)<!-- devpill:end -->"
          perl -0777 -pi -e 's/<!-- devpill:begin -->.*?<!-- devpill:end -->/$ENV{DEVPILL}/s' README.md
```

(The commit/push tail of that step is unchanged.)

- [ ] **Step 3: Add a stablepill stamp step to promote.yml**

After the `Publish stable` step, add:

```yaml
      - uses: actions/checkout@v4
      - name: Stamp README stable pill
        shell: bash
        env:
          VERSION: ${{ steps.dev.outputs.version }}
        run: |
          today="$(date -u +%Y-%m-%d)"
          export STABLEPILL="<!-- stablepill:begin --> ![stable](https://img.shields.io/badge/stable-${VERSION}-009E73) ![released](https://img.shields.io/badge/released-${today//-/--}-E69F00)<!-- stablepill:end -->"
          perl -0777 -pi -e 's/<!-- stablepill:begin -->.*?<!-- stablepill:end -->/$ENV{STABLEPILL}/s' README.md
          git config user.name "github-actions[bot]"
          git config user.email "41898282+github-actions[bot]@users.noreply.github.com"
          git add README.md
          if git commit -m "README: stamp stable v${VERSION} [skip ci]"; then
            git pull --rebase origin main || true
            git push origin HEAD:main || echo "::warning::stable pill push failed — corrected next promotion"
          fi
```

- [ ] **Step 4: Commit**

```bash
git add README.md .github/workflows/build.yml .github/workflows/promote.yml
git commit -m "CI/docs: split README status into dev/stable pills, each workflow owns one"
```

---

## Task 10: Runbook — go-live sequence + live verification (owner-run)

**Not code.** This is the ordered cutover and the "do X, expect Y" live script for the owner. It runs after the branch is reviewer-approved and merged.

**Ordered go-live sequence:**

- [ ] **1. Merge `go-public-dual-channel` to `main`.** CI publishes the new build to `dev-latest` (name = version). Existing installs (old token-based updater) still update fine — a valid token against a soon-to-be-public repo keeps working through the flip.
- [ ] **2. Flip the repo to public** (owner, GitHub settings). CI already fetches ACT (Task 1), so the missing vendored exe does not break the build. The self-hosted-runner hatch is now foreclosed (SPEC) — do not enable runners.
- [ ] **3. Cut the first stable:** run the `promote` workflow (accept the default version = current `dev-latest`). Confirm a `stable` release appears, `prerelease: false`, name = the version, body naming the source commit.
- [ ] **4. Verify the update round-trips** (live, on the Windows box):

**Live script (attach to the branch at the owner's gate):**

- [ ] **Stable install, no token:** on a machine that has *never* held the token, install `eq2auras.dll` from the `stable` release and enable it. Expect: loads and runs; no token prompt anywhere.
- [ ] **Notify string:** with the plugin on stable and a newer build available on the selected channel, restart ACT (or reload). Expect: the status label shows `update available: v<X> — click "Check for updates"`. With nothing newer, expect the normal `eq2auras v<X> | logging to …` line.
- [ ] **Manual update, forward:** on beta with `dev-latest` ahead, click **Check for updates**. Expect: downloads, `update v<X> installed — reloading…`, live reload to the new version (status line shows the new `v<X>`).
- [ ] **Channel downgrade, backward (the identity case):** on a beta build numerically *ahead* of stable (e.g. beta `0.1.200`, stable `0.1.150`), untick **Beta channel**. Expect: a check fires immediately and it installs stable `0.1.150` — i.e. it moves *backward* and reloads, never "already up to date".
- [ ] **Already-up-to-date:** click **Check for updates** twice. Expect: second click reports `already up to date (v<X>)` with no reload.
- [ ] **Empty stable guard** (only if checked before the first promotion): on stable with no `stable` release yet, click **Check for updates**. Expect: `no stable release yet` — no crash.

Break to the owner if any live step fails; otherwise the branch is done.

---

## Self-review

**Spec coverage** (SPEC §Release channels & public distribution):
- Going public / no token → Tasks 5, 6 (updater + TokenStore removal). ✓
- Two channels, one boolean → Task 4 (knob) + Task 6 (checkbox). ✓
- Stable = promotion of exact bytes, defaults to current dev-latest, records commit SHA, name = version, not prerelease → Task 8. ✓
- Identity, never version order → Task 3 (rule + backward-case test) + Task 5 (gate). ✓
- Notify on startup (status string, notify-only) → Task 7. ✓
- Building against live ACT (untracked exe, fetch latest, record version, cache fallback) → Task 1. ✓
- README stable pill joins the dev one → Task 9. ✓
- Rollout/cutover + first-stable-before-public + empty-stable handling → Task 10 + Task 5 null path. ✓

**Placeholder scan:** no TBD/TODO; every code step carries real code; live steps carry concrete expected output. ✓

**Type consistency:** `TagForChannel`/`UpdateAvailable` signatures match between Task 3 (definition) and Tasks 5, 7 (use); `ReleaseManifest.Name` / `ReleaseAsset.BrowserDownloadUrl` match between Task 2 and Task 5; `RunInBackground(pluginsDir, betaChannel, installedVersion)` and `CheckInBackground(betaChannel, installedVersion, onUpdateAvailable)` match between Task 5 (definition) and Tasks 6, 7 (call sites); `Settings.BetaChannel` matches between Task 4 and Tasks 6, 7. ✓
