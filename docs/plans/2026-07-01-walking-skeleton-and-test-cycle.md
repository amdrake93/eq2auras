# Walking Skeleton + Test/Deploy Cycle — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up the eq2auras repo skeleton and prove the entire edit-on-Mac → CI-build → deploy-to-Windows → run-in-ACT → self-reload → logs-back-to-Mac loop works end to end, with **no feature code**.

**Architecture:** Two projects — `eq2auras.Core` (.NET Standard 2.0, pure, unit-tested on the Mac) and `eq2auras.Plugin` (.NET Framework 4.7.2 + WPF, Windows-only, built in CI). A minimal `IActPluginV1` loads in ACT, writes diagnostic JSONL to an app-data path, and opens a transparent click-through WPF test window (a pulsing rectangle whose **colour encodes the build**). CI (`windows-latest` + `msbuild`) publishes the DLLs to a rolling GitHub prerelease; the plugin self-updates from that release. Ships as **two DLLs (no ILRepack yet)** so this plan also empirically answers whether a dependent `Core.dll` blocks live reload.

**Tech Stack:** C#, .NET Standard 2.0 (Core), .NET Framework 4.7.2 + WPF (Plugin), xUnit on net8.0 (tests), GitHub Actions + MSBuild (CI), ACT `Advanced_Combat_Tracker` API, DPAPI (`ProtectedData`), `JavaScriptSerializer` (net472 built-in JSON), `HttpClient` (GitHub API).

**What this plan deliberately does NOT build:** the timer list, escalation state machine, radial pies, LATE alerts, the center escalation zone, data binding, per-timer identity handling. Those are substantive plans that follow, informed by this plan's findings.

---

## Prerequisites (one-time, manual — do these before Task 1)

- [ ] **P1 — Mac .NET SDK.** Install the .NET SDK on the Mac and verify:
  ```bash
  brew install --cask dotnet-sdk
  dotnet --version   # expect 8.x or later
  ```
- [ ] **P2 — Private GitHub repo + remote.** Create a **private** repo named `eq2auras` under the personal GitHub account (web UI). Then, from the existing local repo:
  ```bash
  cd /Users/Alex/repos/eq2auras
  git remote add origin git@github.com:<your-user>/eq2auras.git
  git push -u origin main
  ```
- [ ] **P3 — ACT reference assembly.** On the Windows machine, locate `Advanced Combat Tracker.exe` (the running ACT executable). Copy it to the Mac (shared folder / scp / USB) and place it at `/Users/Alex/repos/eq2auras/ThirdParty/Advanced Combat Tracker.exe`. Commit it (private repo — referencing is the documented workflow, and it never runs in CI):
  ```bash
  cd /Users/Alex/repos/eq2auras
  mkdir -p ThirdParty
  # copy the exe into ThirdParty/, then:
  git add "ThirdParty/Advanced Combat Tracker.exe"
  git commit -m "Add ACT reference assembly for compilation"
  ```
  Note: `.gitignore` ignores `*.dll`/`*.pdb` and `bin/`/`obj/`, but **not** `*.exe`, and `ThirdParty/` is not ignored — a normal `git add` works (no `-f` needed).
- [ ] **P4 — Fine-grained PAT (used in Task 9).** In GitHub → Settings → Developer settings → **Fine-grained tokens**, create a token **scoped to only the `eq2auras` repo**, permission **Contents: Read-only**. Save the value somewhere safe; it is entered into the plugin on the Windows box in Task 9. Do **not** commit it.
- [ ] **P5 — `gh` authenticated.** Several steps use `gh run watch`; authenticate the GitHub CLI against the account:
  ```bash
  gh auth login
  ```

---

## File Structure

```
eq2auras/
├── eq2auras.sln
├── ThirdParty/
│   └── Advanced Combat Tracker.exe        # P3, committed, reference-only
├── src/
│   ├── eq2auras.Core/                      # .NET Standard 2.0, dependency-free
│   │   ├── eq2auras.Core.csproj
│   │   └── Diagnostics/
│   │       ├── TimerSnapshotRecord.cs      # the raw per-reading log record + JSONL serializer
│   │       └── Json.cs                      # tiny dependency-free JSON string escaper
│   └── eq2auras.Plugin/                     # .NET Framework 4.7.2 + WPF, Windows-only
│       ├── eq2auras.Plugin.csproj
│       ├── Eq2AurasPlugin.cs                # IActPluginV1 entry point + lifecycle
│       ├── Diagnostics/
│       │   └── JsonlLogWriter.cs            # append-only JSONL writer to %APPDATA% path
│       ├── Act/
│       │   └── TimerProbe.cs                # subscribes OnSpellTimer*, polls GetTimerFrames()
│       ├── Overlay/
│       │   ├── OverlayHost.cs               # STA thread + Dispatcher lifecycle
│       │   └── TestWindow.xaml (+ .cs)      # transparent click-through pulsing rectangle
│       └── SelfUpdate/
│           ├── TokenStore.cs                # DPAPI-protected token at rest
│           └── SelfUpdater.cs               # GitHub release fetch → swap → reload
├── tests/
│   └── eq2auras.Core.Tests/                 # net8.0, xUnit — runs on the Mac
│       ├── eq2auras.Core.Tests.csproj
│       └── TimerSnapshotRecordTests.cs
├── .github/workflows/build.yml              # windows-latest, msbuild, publish prerelease
└── docs/plans/2026-07-01-walking-skeleton-and-test-cycle.md   # this file
```

**Where each step runs** (called out per step): **[MAC]** local with `dotnet`; **[CI]** GitHub Actions on push; **[WIN]** manual on the Windows/ACT box.

---

## Task 1: Core project + diagnostic log record (TDD on the Mac)

The first real Core type is the raw diagnostic record the spike emits. It records **raw ACT readings** (no derived state — the spike observes ACT's behaviour, it does not impose our model yet). Dependency-free so the payload stays minimal.

**Files:**
- Create: `eq2auras.sln`
- Create: `src/eq2auras.Core/eq2auras.Core.csproj`
- Create: `src/eq2auras.Core/Diagnostics/Json.cs`
- Create: `src/eq2auras.Core/Diagnostics/TimerSnapshotRecord.cs`
- Create: `tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
- Test: `tests/eq2auras.Core.Tests/TimerSnapshotRecordTests.cs`

- [ ] **Step 1: Create the solution and projects** **[MAC]**

```bash
cd /Users/Alex/repos/eq2auras
dotnet new sln -n eq2auras
dotnet new classlib -n eq2auras.Core -o src/eq2auras.Core -f netstandard2.0
dotnet new xunit -n eq2auras.Core.Tests -o tests/eq2auras.Core.Tests -f net8.0
rm src/eq2auras.Core/Class1.cs tests/eq2auras.Core.Tests/UnitTest1.cs
dotnet sln add src/eq2auras.Core/eq2auras.Core.csproj
dotnet sln add tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj
dotnet add tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj reference src/eq2auras.Core/eq2auras.Core.csproj
```

- [ ] **Step 2: Write the failing test** **[MAC]**

Create `tests/eq2auras.Core.Tests/TimerSnapshotRecordTests.cs`:

```csharp
using Eq2Auras.Core.Diagnostics;
using Xunit;

public class TimerSnapshotRecordTests
{
    [Fact]
    public void ToJsonl_serializes_all_fields_with_invariant_numbers()
    {
        var record = new TimerSnapshotRecord
        {
            Kind = "poll",
            TimestampUnixMs = 1750000000000,
            Name = "Tank Buster",
            Combatant = "Big Bad",
            TimeLeft = 12.5,
            WarningValue = 10,
            TotalValue = 30
        };

        var json = record.ToJsonl();

        Assert.Equal(
            "{\"kind\":\"poll\",\"ts\":1750000000000,\"name\":\"Tank Buster\"," +
            "\"combatant\":\"Big Bad\",\"timeLeft\":12.5,\"warningValue\":10,\"totalValue\":30}",
            json);
    }

    [Fact]
    public void ToJsonl_escapes_quotes_backslashes_and_control_chars()
    {
        var record = new TimerSnapshotRecord
        {
            Kind = "notify",
            TimestampUnixMs = 1,
            Name = "He said \"hi\"\tand\\left",
            Combatant = "",
            TimeLeft = -3.0,
            WarningValue = 0,
            TotalValue = 0
        };

        var json = record.ToJsonl();

        Assert.Equal(
            "{\"kind\":\"notify\",\"ts\":1,\"name\":\"He said \\\"hi\\\"\\tand\\\\left\"," +
            "\"combatant\":\"\",\"timeLeft\":-3,\"warningValue\":0,\"totalValue\":0}",
            json);
    }

    [Fact]
    public void ToJsonl_emits_null_for_NaN_timeLeft()
    {
        var record = new TimerSnapshotRecord
        {
            Kind = "poll", TimestampUnixMs = 5, Name = "x", Combatant = "",
            TimeLeft = double.NaN, WarningValue = 0, TotalValue = 0
        };

        Assert.Contains("\"timeLeft\":null", record.ToJsonl());
    }
}
```

- [ ] **Step 3: Run the test to verify it fails** **[MAC]**

```bash
dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj
```
Expected: FAIL — `TimerSnapshotRecord` does not exist (compile error).

- [ ] **Step 4: Implement the JSON escaper** **[MAC]**

Create `src/eq2auras.Core/Diagnostics/Json.cs`:

```csharp
using System.Globalization;
using System.Text;

namespace Eq2Auras.Core.Diagnostics
{
    internal static class Json
    {
        public static string Escape(string s)
        {
            if (s == null) return "";
            var sb = new StringBuilder(s.Length + 2);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        public static string Number(double d) =>
            d.ToString("0.############", CultureInfo.InvariantCulture);
    }
}
```

- [ ] **Step 5: Implement the record** **[MAC]**

Create `src/eq2auras.Core/Diagnostics/TimerSnapshotRecord.cs`:

```csharp
using System.Text;

namespace Eq2Auras.Core.Diagnostics
{
    /// One raw reading of an ACT timer at a moment in time. Fields are captured
    /// verbatim from ACT — no derived state — because the spike observes ACT's
    /// behaviour rather than imposing the overlay's model.
    public sealed class TimerSnapshotRecord
    {
        public string Kind { get; set; }            // poll | notify | warning | expire | removed
        public long TimestampUnixMs { get; set; }
        public string Name { get; set; }
        public string Combatant { get; set; }
        public double TimeLeft { get; set; }
        public int WarningValue { get; set; }
        public int TotalValue { get; set; }

        public string ToJsonl()
        {
            var sb = new StringBuilder(128);
            sb.Append("{\"kind\":\"").Append(Json.Escape(Kind)).Append("\"");
            sb.Append(",\"ts\":").Append(TimestampUnixMs);
            sb.Append(",\"name\":\"").Append(Json.Escape(Name)).Append("\"");
            sb.Append(",\"combatant\":\"").Append(Json.Escape(Combatant)).Append("\"");
            sb.Append(",\"timeLeft\":");
            if (double.IsNaN(TimeLeft)) sb.Append("null");            // invalid JSON otherwise — keeps the spike log parseable
            else sb.Append(Json.Number(TimeLeft));
            sb.Append(",\"warningValue\":").Append(WarningValue);
            sb.Append(",\"totalValue\":").Append(TotalValue);
            sb.Append("}");
            return sb.ToString();
        }
    }
}
```

- [ ] **Step 6: Run the test to verify it passes** **[MAC]**

```bash
dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj
```
Expected: PASS — 2 passed.

- [ ] **Step 7: Commit** **[MAC]**

```bash
git add eq2auras.sln src/eq2auras.Core tests/eq2auras.Core.Tests
git commit -m "Core: TimerSnapshotRecord + JSONL serializer (Mac-testable)"
```

---

## Task 1.5: Confirm ACT API shapes on the Mac (reconnaissance)

`TimerProbe` (Task 5) hardcodes several ACT members the spec flags `[U]`. The committed `ThirdParty/Advanced Combat Tracker.exe` is right here on the Mac — decompile it locally to confirm the real names/types **before** Task 5 depends on them, instead of discovering mistakes through slow CI round-trips. This front-loads the spec's reconnaissance onto the fast machine.

- [ ] **Step 1: Install a decompiler** **[MAC]**

```bash
dotnet tool install -g ilspycmd
```

- [ ] **Step 2: Note ACT's bitness (informational — gates no code)** **[MAC]**

```bash
cd /Users/Alex/repos/eq2auras
file "ThirdParty/Advanced Combat Tracker.exe"
```
Caveat: `file` reads the **PE header**, which is *not* reliable for a managed (.NET) exe — an **AnyCPU** assembly emits a `PE32` header yet runs as a **64-bit** process on a 64-bit OS (the CLR JITs to host bitness). Treat `PE32`/`PE32+` as a hint only. For the definitive answer, read the CLR header's `32BITREQUIRED`/`32BITPREF` CorFlags — `corflags "Advanced Combat Tracker.exe"` on Windows, or the CorFlags shown in the `ilspycmd` dump on the Mac. Record it in the findings note. **This is informational only:** click-through uses the int `GetWindowLong`/`SetWindowLong` exports, which are correct on both 32- and 64-bit, so no code depends on the result.

- [ ] **Step 3: Decompile the ACT types we touch** **[MAC]**

```bash
ilspycmd "ThirdParty/Advanced Combat Tracker.exe" -t Advanced_Combat_Tracker.FormSpellTimers > /tmp/act-FormSpellTimers.cs
ilspycmd "ThirdParty/Advanced Combat Tracker.exe" -t Advanced_Combat_Tracker.TimerFrame > /tmp/act-TimerFrame.cs
ilspycmd "ThirdParty/Advanced Combat Tracker.exe" -t Advanced_Combat_Tracker.SpellTimer > /tmp/act-SpellTimer.cs
ilspycmd "ThirdParty/Advanced Combat Tracker.exe" -t Advanced_Combat_Tracker.TimerData > /tmp/act-TimerData.cs
```
If `-t` errors (the flag varies across `ilspycmd` versions), decompile the whole assembly once and rely on the Step-4 grep instead:
```bash
ilspycmd "ThirdParty/Advanced Combat Tracker.exe" > /tmp/act-all.cs
```

- [ ] **Step 4: Confirm the members TimerProbe/TestWindow/SelfUpdater will use** **[MAC]**

```bash
grep -nE "GetTimerFrames|OnSpellTimer|TimeLeft|WarningValue|TimerValue|Combatant|SpellTimers|TimerData|AppDataFolder|PluginGetSelfData|cbEnabled" /tmp/act-*.cs
```
Confirm and note the exact spelling/type of: `FormSpellTimers.GetTimerFrames()` return type (expect `List<TimerFrame>`); the `OnSpellTimer*` delegate signature (expect `void (TimerFrame)`); `TimerFrame.Name`/`.Combatant` (types); `TimerFrame.SpellTimers` element type; `TimerFrame.TimerData`; `SpellTimer.TimeLeft` (**`int` or `double`?**); `TimerData.WarningValue`/`.TimerValue` (types).

- [ ] **Step 5: Reconcile this plan's code with reality** **[MAC]**

If any member name or type differs from what Tasks 5/7/9 assume, edit those code blocks now (and, if `SpellTimer.TimeLeft` is `int`, change `TimerSnapshotRecord.TimeLeft` to `int` and its test expectations together). Record confirmed shapes + the bitness in `docs/plans/2026-07-01-spike-findings.md`.

```bash
git add docs/plans
git commit -m "Recon: confirmed ACT API shapes + bitness from the decompiled exe"
```

---

## Task 2: Minimal ACT plugin (loads, labels, teardown)

A do-nothing-but-load plugin. Cannot be built on the Mac — verified by CI (Task 3) then by loading in ACT (Task 4).

**Files:**
- Create: `src/eq2auras.Plugin/eq2auras.Plugin.csproj`
- Create: `src/eq2auras.Plugin/Eq2AurasPlugin.cs`

- [ ] **Step 1: Create the Plugin project file** **[MAC]** (authoring only; it won't build here)

Create `src/eq2auras.Plugin/eq2auras.Plugin.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net472</TargetFramework>
    <UseWPF>true</UseWPF>
    <UseWindowsForms>true</UseWindowsForms>
    <OutputType>Library</OutputType>
    <AssemblyName>eq2auras</AssemblyName>
    <RootNamespace>Eq2Auras.Plugin</RootNamespace>
    <LangVersion>latest</LangVersion>
    <!-- CI overrides Version via /p:Version=... ; default for local authoring -->
    <Version>0.0.0-dev</Version>
  </PropertyGroup>

  <ItemGroup>
    <Reference Include="Advanced Combat Tracker">
      <HintPath>..\..\ThirdParty\Advanced Combat Tracker.exe</HintPath>
      <SpecificVersion>False</SpecificVersion>
      <Private>False</Private>
    </Reference>
    <Reference Include="System.Web.Extensions" />
    <Reference Include="System.Net.Http" />
    <Reference Include="System.Security" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\eq2auras.Core\eq2auras.Core.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Implement the plugin entry point** **[MAC]** (authoring only)

Create `src/eq2auras.Plugin/Eq2AurasPlugin.cs`:

```csharp
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
```

- [ ] **Step 2b: Add a throwaway XAML to force WPF markup compilation early** **[MAC]** (authoring)

The plugin csproj is SDK-style with `UseWPF` on `net472` — a combination **not officially guaranteed** for XAML markup compilation; it can fail depending on SDK version. Prove it compiles in CI *now* (Task 3), not five tasks deep at Task 7. Create `src/eq2auras.Plugin/BuildProbe.xaml`:

```xml
<UserControl x:Class="Eq2Auras.Plugin.BuildProbe"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" />
```

and `src/eq2auras.Plugin/BuildProbe.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace Eq2Auras.Plugin
{
    public partial class BuildProbe : UserControl
    {
        public BuildProbe() { InitializeComponent(); }
    }
}
```

Nothing references it — its only job is to exercise the XAML compiler in the first CI build. Task 7 deletes it once the real window proves the same path.

- [ ] **Step 3: Add the Plugin project to the solution** **[MAC]**

```bash
cd /Users/Alex/repos/eq2auras
dotnet sln add src/eq2auras.Plugin/eq2auras.Plugin.csproj
```

- [ ] **Step 4: Confirm the Mac test loop still ignores the Windows project** **[MAC]**

```bash
dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj
```
Expected: PASS — building the test project pulls in only Core, not the net472 Plugin. (Do **not** run `dotnet build eq2auras.sln` on the Mac — the WPF/net472 project cannot build here; that is CI's job.)

- [ ] **Step 5: Commit** **[MAC]**

```bash
git add src/eq2auras.Plugin eq2auras.sln
git commit -m "Plugin: minimal IActPluginV1 skeleton (loads, labels, teardown)"
```

---

## Task 3: CI build + publish (proves the plugin compiles on Windows)

**Files:**
- Create: `.github/workflows/build.yml`

- [ ] **Step 1: Write the workflow** **[MAC]**

Create `.github/workflows/build.yml`:

```yaml
name: build
on:
  push:
    branches: [main]
  workflow_dispatch:

permissions:
  contents: write   # needed to create/update the rolling prerelease

jobs:
  build:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4

      - name: Compute version
        id: ver
        shell: bash
        run: echo "version=0.1.${{ github.run_number }}" >> "$GITHUB_OUTPUT"

      - uses: microsoft/setup-msbuild@v3

      - name: Run Core unit tests
        run: dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj -c Release

      - name: Build the plugin (MSBuild — required for WPF; plugin only — tests already ran via dotnet)
        run: msbuild src/eq2auras.Plugin/eq2auras.Plugin.csproj -restore -p:Configuration=Release -p:Version=${{ steps.ver.outputs.version }}

      - name: Stage plugin artifacts
        shell: bash
        run: |
          mkdir dist
          cp "src/eq2auras.Plugin/bin/Release/net472/eq2auras.dll" dist/
          cp "src/eq2auras.Plugin/bin/Release/net472/eq2auras.Core.dll" dist/

      - uses: actions/upload-artifact@v4
        with:
          name: eq2auras-${{ steps.ver.outputs.version }}
          path: dist/*

      - name: Publish rolling prerelease
        uses: softprops/action-gh-release@v2
        with:
          tag_name: dev-latest
          name: dev-latest
          prerelease: true
          files: dist/*
```

- [ ] **Step 2: Push and watch the run** **[MAC]**

```bash
git add .github/workflows/build.yml
git commit -m "CI: windows-latest msbuild build + rolling dev-latest prerelease"
git push
gh run watch --exit-status
```
Expected: the run **succeeds**, produces `dist/eq2auras.dll` + `dist/eq2auras.Core.dll`, and a `dev-latest` prerelease appears with both assets.

- [ ] **Step 3: Contingency — missing ACT reference** **[MAC]**

If the MSBuild step fails with an unresolved type in `Advanced_Combat_Tracker` naming another assembly, copy that DLL from the Windows ACT folder into `ThirdParty/`, add a matching `<Reference>` block (same `Private=False`) to `eq2auras.Plugin.csproj`, `git add -f` it, commit, and push again. Repeat until the build is green. (Expected: not needed — all API types live in the exe.)

- [ ] **Step 4: Contingency — WPF markup compilation fails (the Task-2b probe's whole point)** **[MAC]**

If MSBuild fails on XAML compilation itself (errors like missing `BuildProbe.g.cs`, `MarkupCompilePass1/2`, or `GenerateTemporaryTargetAssembly`), the SDK-style + `net472` + `UseWPF` combination is not working on the runner's SDK — this is the anticipated project-format risk, and it surfaces here at CI, cheaply, before any UI work. Fallback: **convert `eq2auras.Plugin` to a legacy (non-SDK) csproj** — `<Project ToolsVersion="...">` importing `Microsoft.CSharp.targets`, with explicit `<Page Include="...xaml">`/`<Compile>` items, the WPF assembly references (`PresentationCore`/`PresentationFramework`/`WindowsBase`/`System.Xaml`), and (if needed) `packages.config`. This is the proven path for net472 WPF ACT plugins (Triggernometry, Hojoring, ActStatter all use legacy csproj). The `Core` and test projects stay SDK-style; only the WPF plugin converts. Two stacking gotchas to expect immediately after converting:
  - **netstandard facade:** a net472 *legacy* project consuming the netstandard2.0 `Core` usually needs an explicit `<Reference Include="netstandard" />` and `<AutoGenerateBindingRedirects>true</AutoGenerateBindingRedirects>` (SDK-style adds these implicitly; legacy does not). Without them you get type-load/facade errors that look unrelated to the format switch.
  - **CI restore:** `msbuild …csproj -restore` restores `PackageReference`, **not** `packages.config`. All the plugin's non-Core references (`System.Web.Extensions`, `System.Net.Http`, `System.Security`, the WPF assemblies) are framework/GAC refs needing no package — so **avoid a `packages.config`** entirely and `-restore` keeps working; only add a separate `nuget restore` step if a real package sneaks in.

  Re-run CI until green before proceeding to Task 4.

---

## Task 4: First manual install + load in ACT (bootstrap + log path)

Establishes the one-time manual install, confirms the plugin loads, and confirms the app-data log directory is where we think it is. No self-update yet.

**Files:** none (adds the JSONL writer used from here on is Task 5; this task loads the Task-2 skeleton).

- [ ] **Step 1: Download the built plugin to the Windows box** **[WIN]**

On the Windows/ACT machine, download the two assets (`eq2auras.dll`, `eq2auras.Core.dll`) from the repo's `dev-latest` release into a scratch folder.

- [ ] **Step 2: Unblock the downloaded DLLs** **[WIN]**

Browser-downloaded files carry the mark-of-the-web; ACT will refuse to load them otherwise. In PowerShell:
```powershell
Get-ChildItem <scratch>\*.dll | Unblock-File
```

- [ ] **Step 3: Install into ACT** **[WIN]**

Copy both DLLs to `%APPDATA%\Advanced Combat Tracker\Plugins\`. In ACT → **Plugins** tab → **Browse**, select `eq2auras.dll`, click **Add/Enable**.
Expected: the plugin appears enabled; the status label reads `eq2auras v0.1.<n> loaded`; an `eq2auras` tab appears.

- [ ] **Step 4: Confirm clean teardown** **[WIN]**

Uncheck the plugin's **Enabled** box, then re-check it.
Expected: label toggles to `eq2auras unloaded` then back to `eq2auras v0.1.<n> loaded`, with no ACT error dialog. (This is *lifecycle firing* — NOT proof that new bytes load; that is Task 8.)

- [ ] **Step 5: Record the outcome** **[WIN]**

Note in the release/PR description or a scratch note: plugin loaded ✅/❌, any `.dll.config` prompt from ACT (contingency: if ACT asks for one, add an `eq2auras.dll.config` with an empty `<configuration/>` and re-add), and the exact `%APPDATA%\Advanced Combat Tracker\Plugins` path confirmed.

---

## Task 5: Diagnostic logging wired into the plugin (the spike instrument)

Now the plugin actually observes ACT: it subscribes to the four spell-timer events and polls `GetTimerFrames()` on a timer, writing `TimerSnapshotRecord` JSONL to the app-data path.

**Files:**
- Create: `src/eq2auras.Plugin/Diagnostics/JsonlLogWriter.cs`
- Create: `src/eq2auras.Plugin/Act/TimerProbe.cs`
- Modify: `src/eq2auras.Plugin/Eq2AurasPlugin.cs`

- [ ] **Step 1: Implement the log writer** **[MAC]** (authoring)

Create `src/eq2auras.Plugin/Diagnostics/JsonlLogWriter.cs`:

```csharp
using System;
using System.IO;
using System.Text;
using Advanced_Combat_Tracker;
using Eq2Auras.Core.Diagnostics;

namespace Eq2Auras.Plugin.Diagnostics
{
    /// Append-only JSONL writer under %APPDATA%\Advanced Combat Tracker\eq2auras\logs.
    /// One file per plugin session. Thread-safe via a lock; flushes each line.
    public sealed class JsonlLogWriter : IDisposable
    {
        private readonly object _gate = new object();
        private StreamWriter _writer;

        public string FilePath { get; }

        public JsonlLogWriter()
        {
            var dir = Path.Combine(
                ActGlobals.oFormActMain.AppDataFolder.FullName,
                "eq2auras", "logs");
            Directory.CreateDirectory(dir);
            FilePath = Path.Combine(dir, "spike-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".jsonl");
            _writer = new StreamWriter(new FileStream(FilePath, FileMode.Create, FileAccess.Write, FileShare.Read), Encoding.UTF8)
            {
                AutoFlush = true
            };
        }

        public void Write(TimerSnapshotRecord record)
        {
            lock (_gate)
            {
                if (_writer == null) return;
                _writer.WriteLine(record.ToJsonl());
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _writer?.Dispose();
                _writer = null;
            }
        }
    }
}
```

- [ ] **Step 2: Implement the timer probe** **[MAC]** (authoring)

Create `src/eq2auras.Plugin/Act/TimerProbe.cs`. It snapshots ACT's live data defensively (copying primitives out immediately — never holding ACT's live objects) per the spec's concurrency rule.

```csharp
using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Advanced_Combat_Tracker;
using Eq2Auras.Core.Diagnostics;
using Eq2Auras.Plugin.Diagnostics;

namespace Eq2Auras.Plugin.Act
{
    /// Subscribes to ACT's spell-timer lifecycle events and polls GetTimerFrames()
    /// on a WinForms timer, logging raw readings. Pure observation — no overlay logic.
    public sealed class TimerProbe : IDisposable
    {
        private readonly JsonlLogWriter _log;
        private readonly Timer _pollTimer;

        public TimerProbe(JsonlLogWriter log)
        {
            _log = log;

            ActGlobals.oFormSpellTimers.OnSpellTimerNotify += OnNotify;
            ActGlobals.oFormSpellTimers.OnSpellTimerWarning += OnWarning;
            ActGlobals.oFormSpellTimers.OnSpellTimerExpire += OnExpire;
            ActGlobals.oFormSpellTimers.OnSpellTimerRemoved += OnRemoved;

            _pollTimer = new Timer { Interval = 100 }; // 10 Hz is ample for the spike
            _pollTimer.Tick += OnPoll;
            _pollTimer.Start();
        }

        private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        private void LogFrame(string kind, TimerFrame frame)
        {
            // Copy primitives out of ACT's live objects immediately (snapshot).
            var timers = frame.SpellTimers;
            double timeLeft = timers != null && timers.Count > 0 ? timers[0].TimeLeft : double.NaN;
            _log.Write(new TimerSnapshotRecord
            {
                Kind = kind,
                TimestampUnixMs = NowMs(),
                Name = frame.Name ?? "",
                Combatant = frame.Combatant ?? "",
                TimeLeft = timeLeft,
                WarningValue = frame.TimerData != null ? frame.TimerData.WarningValue : 0,
                TotalValue = frame.TimerData != null ? frame.TimerData.TimerValue : 0
            });
        }

        private void OnNotify(TimerFrame f) => LogFrame("notify", f);
        private void OnWarning(TimerFrame f) => LogFrame("warning", f);
        private void OnExpire(TimerFrame f) => LogFrame("expire", f);
        private void OnRemoved(TimerFrame f) => LogFrame("removed", f);

        private void OnPoll(object sender, EventArgs e)
        {
            List<TimerFrame> frames;
            try
            {
                frames = ActGlobals.oFormSpellTimers.GetTimerFrames();
            }
            catch
            {
                return; // collection mutating on ACT's thread; skip this tick
            }
            if (frames == null) return;
            foreach (var f in frames)
            {
                LogFrame("poll", f);
            }
        }

        public void Dispose()
        {
            _pollTimer.Stop();
            _pollTimer.Tick -= OnPoll;
            _pollTimer.Dispose();

            ActGlobals.oFormSpellTimers.OnSpellTimerNotify -= OnNotify;
            ActGlobals.oFormSpellTimers.OnSpellTimerWarning -= OnWarning;
            ActGlobals.oFormSpellTimers.OnSpellTimerExpire -= OnExpire;
            ActGlobals.oFormSpellTimers.OnSpellTimerRemoved -= OnRemoved;
        }
    }
}
```

> Note: `TimeLeft`, `WarningValue`, `TimerValue`, `Combatant` are used per the spec's data table; if CI reveals a type mismatch (e.g. `TimeLeft` is `int`, `Combatant` is not `string`), adjust the copy in this file and the record field type in `TimerSnapshotRecord` together. This is expected reconnaissance, not a plan defect — the spec flags these as `[U]`.

- [ ] **Step 3: Wire probe + writer into the plugin lifecycle** **[MAC]** (authoring)

Replace `src/eq2auras.Plugin/Eq2AurasPlugin.cs` with:

```csharp
using System.Reflection;
using System.Windows.Forms;
using Advanced_Combat_Tracker;
using Eq2Auras.Plugin.Act;
using Eq2Auras.Plugin.Diagnostics;

namespace Eq2Auras.Plugin
{
    public class Eq2AurasPlugin : IActPluginV1
    {
        private Label _statusLabel;
        private JsonlLogWriter _log;
        private TimerProbe _probe;

        public void InitPlugin(TabPage pluginScreenSpace, Label pluginStatusText)
        {
            _statusLabel = pluginStatusText;
            var version = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? "unknown";

            _log = new JsonlLogWriter();
            _probe = new TimerProbe(_log);

            pluginScreenSpace.Text = "eq2auras";
            _statusLabel.Text = "eq2auras v" + version + " loaded — logging to " + _log.FilePath;
        }

        public void DeInitPlugin()
        {
            _probe?.Dispose();
            _probe = null;
            _log?.Dispose();
            _log = null;
            if (_statusLabel != null) _statusLabel.Text = "eq2auras unloaded";
        }
    }
}
```

- [ ] **Step 4: Commit, push, let CI build** **[MAC]**

```bash
git add src/eq2auras.Plugin
git commit -m "Plugin: diagnostic probe — subscribe OnSpellTimer*, poll GetTimerFrames(), write JSONL"
git push
gh run watch --exit-status
```
Expected: CI green; new `dev-latest` assets.

- [ ] **Step 5: Reinstall on Windows and confirm logging** **[WIN]**

Download the new DLLs, `Unblock-File`, copy into the Plugins folder (disable the plugin first if the file is locked — see Task 8 for the lock finding), re-enable. The status label now shows the log path. Confirm a `spike-*.jsonl` file exists under `%APPDATA%\Advanced Combat Tracker\eq2auras\logs`.
Expected: file created; while ACT parses combat (or synthetic timers, Task 6) it accumulates JSONL lines.

---

## Task 6: Spike observation run + log retrieval (answers the ACT-behaviour unknowns)

This is the payoff: use the logger to observe ACT's real timer behaviour and get the data back to the Mac.

- [ ] **Step 1: Set up a retrieval path** **[WIN]/[MAC]**

Pick the simplest available: a shared/synced folder that includes `%APPDATA%\Advanced Combat Tracker\eq2auras\logs`, or plan to copy the `.jsonl` manually. Confirm a file created on Windows can be opened on the Mac.

- [ ] **Step 2: Drive synthetic timers** **[WIN]**

Without needing a raid: in ACT create a **Custom Trigger** (Options → Custom Triggers) with a regex and an attached **Spell Timer** that has a short duration and a small `WarningValue`, then paste matching lines into ACT's log (Options → **"Simulate log line"** / the log-line test box) to fire it repeatedly. Fire one that you then *let expire* (never re-match) and one you *re-match before expiry* (a reset).
Expected: JSONL captures `notify`, `warning`, `poll` (with decreasing `timeLeft`), `expire`, and eventually `removed` lines.

- [ ] **Step 3: Capture a real fight if available** **[WIN]**

If a live encounter is convenient, run with the plugin enabled to capture real timers (more `WarningValue` variety).

- [ ] **Step 4: Retrieve and analyze on the Mac** **[MAC]**

Copy the `.jsonl` to the Mac. Answer, from the data, the spec's `[U]` questions:
```bash
# does TimeLeft go negative, or clamp at 0, after expiry?
grep -o '"timeLeft":[-0-9.]*' spike-*.jsonl | sort -u | head
# at what timeLeft does a 'removed' occur (RemoveValue behaviour)?
grep '"kind":"removed"' spike-*.jsonl
# distribution of WarningValue across observed timers
grep -o '"warningValue":[0-9]*' spike-*.jsonl | sort | uniq -c
# what does a reset look like (timeLeft jumps back up for the same name)?
grep '"name":"<your test timer>"' spike-*.jsonl
```

- [ ] **Step 5: Record findings** **[MAC]**

Append a short "Spike findings" note to `docs/plans/2026-07-01-walking-skeleton-and-test-cycle.md` (or a sibling `2026-07-01-spike-findings.md`): negative-vs-clamped `TimeLeft`, the `timeLeft` value at `removed`, `WarningValue` distribution, reset shape. Commit.
```bash
git add docs/plans
git commit -m "Spike findings: ACT timer behaviour (TimeLeft/RemoveValue/reset/WarningValue)"
```

---

## Task 7: WPF test window (transparent, click-through, colour = build)

Adds the rendering-stack probe: a transparent, always-on-top, click-through WPF window with a pulsing rectangle whose colour is a build-time constant. Verifies WPF hosting inside ACT and settles the thread model.

**Files:**
- Create: `src/eq2auras.Plugin/Overlay/TestWindow.xaml`
- Create: `src/eq2auras.Plugin/Overlay/TestWindow.xaml.cs`
- Create: `src/eq2auras.Plugin/Overlay/OverlayHost.cs`
- Modify: `src/eq2auras.Plugin/Eq2AurasPlugin.cs`

- [ ] **Step 1: The window markup** **[MAC]** (authoring)

First delete the Task-2b throwaway (`src/eq2auras.Plugin/BuildProbe.xaml` and `BuildProbe.xaml.cs`) — the real window now exercises the XAML path. Then create `src/eq2auras.Plugin/Overlay/TestWindow.xaml`:

```xml
<Window x:Class="Eq2Auras.Plugin.Overlay.TestWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        WindowStyle="None" AllowsTransparency="True" Background="Transparent"
        Topmost="True" ShowInTaskbar="False" ResizeMode="NoResize"
        Width="220" Height="220" Left="80" Top="80">
    <Grid>
        <Rectangle x:Name="Pulse" Width="140" Height="140" RadiusX="16" RadiusY="16"
                   HorizontalAlignment="Center" VerticalAlignment="Center">
            <Rectangle.Triggers>
                <EventTrigger RoutedEvent="Loaded">
                    <BeginStoryboard>
                        <Storyboard RepeatBehavior="Forever" AutoReverse="True">
                            <DoubleAnimation Storyboard.TargetProperty="Opacity"
                                             From="1.0" To="0.3" Duration="0:0:0.8"/>
                        </Storyboard>
                    </BeginStoryboard>
                </EventTrigger>
            </Rectangle.Triggers>
        </Rectangle>
    </Grid>
</Window>
```

- [ ] **Step 2: The window code-behind — colour constant + click-through** **[MAC]** (authoring)

Create `src/eq2auras.Plugin/Overlay/TestWindow.xaml.cs`. **The `BuildColor` here is the reload happy-path indicator** — change it and push to prove a live reload ran new code.

```csharp
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Eq2Auras.Plugin.Overlay
{
    public partial class TestWindow : Window
    {
        // ▼▼ RELOAD INDICATOR: change this colour + push to test live self-update ▼▼
        private static readonly Color BuildColor = Colors.DeepSkyBlue;
        // ▲▲

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_TRANSPARENT = 0x20;

        // Ex-style is a 32-bit value, so the int GetWindowLong/SetWindowLong are correct AND
        // portable on both 32- and 64-bit Windows. Do NOT use the ...Ptr variants here: they
        // are only needed for pointer-sized indices (GWLP_WNDPROC, etc.) and are not exported
        // on 32-bit hosts, so they throw EntryPointNotFoundException on a 32-bit ACT.
        // ACT's bitness is confirmed in Task 1.5.
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        public TestWindow()
        {
            InitializeComponent();
            ((Rectangle)Pulse).Fill = new SolidColorBrush(BuildColor);
            SourceInitialized += MakeClickThrough;
        }

        private void MakeClickThrough(object sender, EventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_LAYERED | WS_EX_TRANSPARENT);
        }
    }
}
```

- [ ] **Step 3: The STA host** **[MAC]** (authoring)

Create `src/eq2auras.Plugin/Overlay/OverlayHost.cs`. Runs the WPF window on a dedicated STA thread with its own `Dispatcher` — the thread model the spec asks the spike to settle.

```csharp
using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace Eq2Auras.Plugin.Overlay
{
    public sealed class OverlayHost : IDisposable
    {
        private Thread _thread;
        private Dispatcher _dispatcher;
        private TestWindow _window;

        public void Start()
        {
            var ready = new ManualResetEventSlim(false);
            _thread = new Thread(() =>
            {
                _dispatcher = Dispatcher.CurrentDispatcher;
                _window = new TestWindow();
                _window.Show();
                ready.Set();
                Dispatcher.Run();
            });
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.IsBackground = true;
            _thread.Start();
            ready.Wait(TimeSpan.FromSeconds(5));
        }

        // Thread-model note: this uses a dedicated STA thread + its own Dispatcher (the spec
        // left ACT-UI-thread vs. dedicated-STA open). If the window misbehaves over the game
        // (topmost/focus loss), the fallback is to create it on ACT's own WinForms UI thread
        // (already STA) via ActGlobals.oFormActMain.BeginInvoke(...). The spike settles which.

        public void Dispose()
        {
            if (_dispatcher == null) return;
            _dispatcher.Invoke(() =>
            {
                _window?.Close();
                _window = null;
            });
            _dispatcher.InvokeShutdown();
            _thread?.Join(TimeSpan.FromSeconds(2));
        }
    }
}
```

- [ ] **Step 4: Launch/close it from the plugin lifecycle** **[MAC]** (authoring)

In `src/eq2auras.Plugin/Eq2AurasPlugin.cs`, add an `OverlayHost` field and manage it alongside the probe:

```csharp
        private Eq2Auras.Plugin.Overlay.OverlayHost _overlay;
```
In `InitPlugin`, after creating `_probe`:
```csharp
            _overlay = new Eq2Auras.Plugin.Overlay.OverlayHost();
            _overlay.Start();
```
In `DeInitPlugin`, before nulling the log:
```csharp
            _overlay?.Dispose();
            _overlay = null;
```

- [ ] **Step 5: Commit, push, build** **[MAC]**

```bash
git add src/eq2auras.Plugin
git commit -m "Plugin: WPF test window (transparent, click-through, pulsing, colour=build)"
git push
gh run watch --exit-status
```
Expected: CI green. If MSBuild errors on WPF (`GenerateTemporaryTargetAssembly`, `pack://`), that is the spec-flagged WPF-in-CI risk surfacing here — fix in CI (ensure `msbuild` not `dotnet build`; `UseWPF` set) before proceeding.

- [ ] **Step 6: Install and observe over the game** **[WIN]**

Update the plugin on the Windows box (download the new DLLs, `Unblock-File`, then disable → copy both DLLs → re-enable, as in Task 4 Steps 1–3), launch EQ2 in **borderless-windowed** mode.
Expected: a pulsing sky-blue rounded rectangle floats over the game; **clicks pass through it** to the game; it stays on top. Record: transparency ✅, click-through ✅, animation smooth ✅, and that the dedicated-STA-thread model works (no cross-thread exceptions in ACT). If click-through or transparency fails, note it — this is the WPF thread-model / layered-window finding the spec wanted.

---

## Task 8: Manual reload probe (does re-enable run NEW bytes?)

Isolates the decisive question — separate from any self-update networking — by overwriting the DLLs by hand.

- [ ] **Step 1: Confirm the installed build colour** **[WIN]**

Plugin enabled, EQ2 running: the rectangle is sky-blue (current `BuildColor`).

- [ ] **Step 2: Produce a visibly different build** **[MAC]**

Edit `TestWindow.xaml.cs`: change `BuildColor` to `Colors.Red`. Commit, push, wait for CI, download the new DLLs to the Windows box, `Unblock-File`.
```bash
git commit -am "Reload probe: flip test window to red"
git push && gh run watch --exit-status
```

- [ ] **Step 3: Attempt a live overwrite WITHOUT disabling** **[WIN]**

With the plugin still enabled, try to copy the new `eq2auras.dll` over the installed one.
Record: **does the copy succeed or fail with "file in use"?** (Answers the DLL-lock `[U]`.)

- [ ] **Step 4: Toggle enable and check the colour** **[WIN]**

Whether or not Step 3 succeeded: if it failed, disable the plugin, copy both new DLLs, re-enable; if it succeeded, just toggle disable→enable.
**The decisive observation:** did the rectangle turn **red** (new bytes ran) or stay **blue** (stale in-memory assembly re-ran)?
- Red → live reload works; the self-updater path (Task 9) is viable.
- Blue → new bytes did not load on re-enable → the self-updater must **prompt an ACT restart** (or a loader-plugin, but that is WPF-hostile per the spec).

- [ ] **Step 5: Test the two-DLL question** **[WIN]**

Repeat Steps 2–4 but this time change **only Core** (add a field to `TimerSnapshotRecord` and reference it in the log) so a *new `eq2auras.Core.dll`* is required. Observe whether the dependent DLL swaps/reloads or blocks.
Record: does an un-merged `Core.dll` block reload? (Answers the ILRepack premise — if it blocks and Step 4 was otherwise red, that argues for ILRepack or restart-prompt.) **Dependency caveat:** this sub-test is only meaningful if Step 4 was *red* (live reload works at all). If Step 4 was blue, a "Core change did nothing" result is inconclusive — don't over-interpret it.

- [ ] **Step 6: Record the verdict** **[MAC]**

In the spike-findings note, record: file-lock (locked/not), new-bytes-on-toggle (yes/no), dependent-Core.dll (blocks/not). These select: live self-update **vs** restart-prompt, and two-DLL **vs** ILRepack. Reset `BuildColor` back to `DeepSkyBlue`, commit.
```bash
git commit -am "Reload probe verdict recorded; reset test colour"
git push
```

---

## Task 9: Self-updater (full release → auto-reload path)

Builds the in-plugin updater. If Task 8 found live reload works, this achieves push→CI→auto-reload; if Task 8 found it does not, this instead downloads and **prompts a restart** (the branch is chosen by the Task-8 verdict — implement the branch that matches).

**Files:**
- Create: `src/eq2auras.Plugin/SelfUpdate/TokenStore.cs`
- Create: `src/eq2auras.Plugin/SelfUpdate/SelfUpdater.cs`
- Modify: `src/eq2auras.Plugin/Eq2AurasPlugin.cs`

- [ ] **Step 1: DPAPI token store** **[MAC]** (authoring)

Create `src/eq2auras.Plugin/SelfUpdate/TokenStore.cs`:

```csharp
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Advanced_Combat_Tracker;

namespace Eq2Auras.Plugin.SelfUpdate
{
    /// Stores the fine-grained GitHub PAT encrypted at rest with DPAPI (per-user).
    public static class TokenStore
    {
        private static string PathOnDisk => System.IO.Path.Combine(
            ActGlobals.oFormActMain.AppDataFolder.FullName, "eq2auras", "token.bin");

        public static void Save(string token)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathOnDisk));
            byte[] enc = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(PathOnDisk, enc);
        }

        public static string Load()
        {
            if (!File.Exists(PathOnDisk)) return null;
            byte[] dec = ProtectedData.Unprotect(
                File.ReadAllBytes(PathOnDisk), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(dec);
        }
    }
}
```

- [ ] **Step 2: The updater** **[MAC]** (authoring)

Create `src/eq2auras.Plugin/SelfUpdate/SelfUpdater.cs`. Runs on a background thread; parses the GitHub release JSON with the framework's `JavaScriptSerializer` (no external dependency); downloads private-repo assets via the asset API URL with `Accept: application/octet-stream` (an `HttpClient` download carries no mark-of-the-web).

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Eq2Auras.Plugin.SelfUpdate
{
    public sealed class SelfUpdater
    {
        private const string Owner = "<your-user>";
        private const string Repo = "eq2auras";
        private const string Tag = "dev-latest";

        private readonly Action<string> _status;

        public SelfUpdater(Action<string> status) { _status = status; }

        public void CheckInBackground(string pluginsDir)
        {
            Task.Run(() =>
            {
                try { Check(pluginsDir).GetAwaiter().GetResult(); }
                catch (Exception ex) { _status("update check failed: " + ex.Message); }
            });
        }

        private async Task Check(string pluginsDir)
        {
            var token = TokenStore.Load();
            if (token == null) { _status("no update token set"); return; }

            using (var http = new HttpClient())
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("eq2auras-updater");
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var relJson = await http.GetStringAsync(
                    $"https://api.github.com/repos/{Owner}/{Repo}/releases/tags/{Tag}");
                var rel = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(relJson);

                // Spike: manual "check for updates" always downloads the dev-latest assets.
                // (Auto-check-on-startup with a published_at version gate is a later refinement.)
                var assets = (object[])rel["assets"];
                foreach (Dictionary<string, object> asset in assets)
                {
                    string name = (string)asset["name"];
                    string apiUrl = (string)asset["url"];
                    byte[] bytes = await DownloadAsset(http, apiUrl);
                    File.WriteAllBytes(Path.Combine(pluginsDir, name), bytes);
                }
                _status("update downloaded — " + ApplyHint());
            }
        }

        private static async Task<byte[]> DownloadAsset(HttpClient http, string apiUrl)
        {
            using (var req = new HttpRequestMessage(HttpMethod.Get, apiUrl))
            {
                req.Headers.Accept.Clear();
                req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
                using (var resp = await http.SendAsync(req))
                {
                    resp.EnsureSuccessStatusCode();
                    return await resp.Content.ReadAsByteArrayAsync();
                }
            }
        }

        // Task 8 verdict decides the apply strategy:
        //  - live reload works  -> after writing files, toggle ActPluginData.cbEnabled off/on
        //  - live reload fails   -> return a message telling the user to restart ACT
        private static string ApplyHint() => "restart ACT to load the update";
    }
}
```

> **Branch note (from Task 8):** implement the branch the probe selected — not both.
> - *New bytes run on toggle, file not locked:* after writing the files, toggle `cbEnabled` off→on (get `ActPluginData` via `ActGlobals.oFormActMain.PluginGetSelfData(this)`, flip `pluginData.cbEnabled.Checked`), and change `ApplyHint()` to "reloaded".
> - *Stale bytes, or file locked:* the in-place `File.WriteAllBytes` will fail or be ineffective while ACT holds the DLL — instead write the downloaded assets to a **staging** subfolder and keep `ApplyHint()` = "restart ACT to load the update" (ACT reads the Plugins folder fresh on startup; a tiny copy-from-staging step or manual copy completes it). This is the expected WPF outcome.

- [ ] **Step 3: Wire a token box + "check for updates" button into the plugin tab** **[MAC]** (authoring)

In `Eq2AurasPlugin.InitPlugin`, after setting `_statusLabel.Text`, add (a masked textbox to paste the token, a save button, and an update button — no external dependency):

```csharp
            var pluginsDir = System.IO.Path.Combine(
                ActGlobals.oFormActMain.AppDataFolder.FullName, "Plugins");

            var tokenBox = new TextBox { Left = 10, Top = 10, Width = 300, UseSystemPasswordChar = true };
            var saveTokenButton = new Button { Text = "Save token", Left = 320, Top = 8, Width = 100 };
            saveTokenButton.Click += (s, e) =>
            {
                if (!string.IsNullOrEmpty(tokenBox.Text))
                {
                    SelfUpdate.TokenStore.Save(tokenBox.Text);
                    tokenBox.Clear();
                    _statusLabel.Text = "update token saved";
                }
            };

            var updateButton = new Button { Text = "Check for updates", Left = 10, Top = 40, Width = 140 };
            updateButton.Click += (s, e) =>
                new SelfUpdate.SelfUpdater(msg => _statusLabel.Text = msg).CheckInBackground(pluginsDir);

            pluginScreenSpace.Controls.Add(tokenBox);
            pluginScreenSpace.Controls.Add(saveTokenButton);
            pluginScreenSpace.Controls.Add(updateButton);
```

(`TextBox`/`Button` are `System.Windows.Forms`, already available via `UseWindowsForms`. This is intentionally minimal — the eventual config surface, per the spec, is a later phase.)

- [ ] **Step 4: Set `Owner`** **[MAC]**

Replace `<your-user>` in `SelfUpdater.cs` with the actual GitHub account. Commit, push, build.
```bash
git commit -am "Plugin: DPAPI token store + GitHub-release self-updater (background)"
git push && gh run watch --exit-status
```

- [ ] **Step 5: Install this build, enter the token** **[WIN]**

Update to this build (manual copy). On the eq2auras tab, click the token button and paste the fine-grained PAT (P4). Confirm `token.bin` appears under `%APPDATA%\...\eq2auras\`.

---

## Task 10: Full auto-reload cycle test + resolve the spec's open items

- [ ] **Step 1: Establish baseline** **[WIN]** — plugin running, rectangle sky-blue.

- [ ] **Step 2: Push a colour change** **[MAC]**
```bash
# edit TestWindow.xaml.cs BuildColor -> Colors.Orange
git commit -am "Auto-reload cycle test: orange"
git push && gh run watch --exit-status
```

- [ ] **Step 3: Trigger the self-update** **[WIN]**

Click "check for updates" on the tab (or restart ACT if the Task-8 verdict was restart-prompt).
**Decisive observation:** does the rectangle become **orange** end-to-end, driven only by a push + the in-plugin updater?
- Yes → the full edit-on-Mac → CI → GitHub-release → self-update loop is proven.
- Yes-but-needs-restart → the loop works with a manual ACT restart per shell change (the expected WPF outcome).

- [ ] **Step 4: Resolve the spec's unverified items** **[MAC]**

Update `docs/SPEC.md` — move the now-answered items out of *Unverified items to confirm* and record the decisions in *Development & test cycle*: (a) reload = live-toggle **or** restart-prompt; (b) ILRepack = needed **or** two-DLL; (c) WPF thread model = dedicated-STA (confirmed) or ACT-thread; (d) `TimeLeft`/`RemoveValue`/`ExtraInfo` findings from Task 6.
```bash
git commit -am "Spec: resolve reload/ILRepack/thread-model/TimeLeft unknowns from the spike"
git push
```

- [ ] **Step 5: Close out** — the walking skeleton stands and the whole loop is characterized. The next plan (substantive) can now design the timer list, state model, and escalation visuals on proven ground.

---

## Notes for the executor

- **Two machines:** `[MAC]` steps use `dotnet` and never build the Plugin/sln; `[CI]` happens on push; `[WIN]` is manual on the ACT box. Never attempt to build `eq2auras.Plugin` on the Mac.
- **The reload verdict (Task 8) gates Task 9's shape** — do Task 8 before writing the Task-9 apply branch.
- **No feature code.** If you find yourself adding a timer list, escalation, pies, or the center zone, stop — that belongs to the next plan.
- **`<your-user>`** appears in `SelfUpdater.cs` and the P2 remote URL — replace with the real GitHub account.
- **Branching:** commits go straight to `main` throughout — a deliberate departure from the usual `<ticket>-<branch>` convention, since eq2auras is a solo personal repo with no ticket tracker. Switch to feature branches if that changes.
