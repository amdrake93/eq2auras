# Debug Mode + Log Retention Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task (project convention: executed inline so Alex can watch). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make diagnostic logging match SPEC §Diagnostic logging: events-only by default, full per-tick dump behind a persisted Debug mode checkbox, and a rolling 14-day/200 MB retention sweep at plugin load (suppressed while debug is on).

**Architecture:** The retention *decision* is a pure Core function (`LogRetentionPolicy` — TDD-able on the Mac); the plugin does only IO around it. `Settings` gains one global bool knob (`DebugLogging`); `TimerProbe` gates its per-poll `LogReading` calls on a `Func<bool>` so the tab checkbox live-applies with no reload; `InitPlugin` reorders to load-settings → sweep → open-writer.

**Tech Stack:** C# — `eq2auras.Core` (netstandard2.0, xunit, local `dotnet test`) and `eq2auras.Plugin` (net472+WPF, **never build locally** — branch-push CI is the compile check).

## Global Constraints

- **Never build the Plugin project or solution on the Mac.** Core tests only: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`.
- **DCJS knob rules** (SPEC §Configuration / `Settings.cs:11-13`): defaults must be the 0-value — `bool DebugLogging` default `false` = off satisfies this by construction. No `System.Web.Extensions`.
- **Single-assembly packaging**: no new assemblies; no `async` in the plugin project; plugin fields limited to GAC + compiled-in types (`Func<bool>` is fine — `Action<...>` fields already exist).
- Retention constants come from the spec verbatim: **14 days / 200 MB**, baked (SPEC §Baked-in constants).
- Working branch: `qol-debug-mode-log-retention` (spec already on it). Merge to `main` only on Alex's explicit approval.
- The three reviewer plan-watch items (backlog, 2026-07-09) are restated inline where each lands (Tasks 2 and 3); the code review verifies them.

---

### Task 1: `Settings.DebugLogging` knob

**Files:**
- Modify: `src/eq2auras.Core/Config/Settings.cs` (property after `EscalationStyle`, ~line 30)
- Test: `tests/eq2auras.Core.Tests/SettingsTests.cs`

**Interfaces:**
- Produces: `Settings.DebugLogging` (`bool`, `[DataMember(Name = "debugLogging")]`, default `false`) — Task 3 reads it in `InitPlugin` and through the probe's `Func<bool>`; Task 4's checkbox mutates it via `SettingsStore.Update`.

- [ ] **Step 1: Write the failing test**

Add to `tests/eq2auras.Core.Tests/SettingsTests.cs`:

```csharp
    [Fact]
    public void DebugLogging_defaults_off_and_round_trips()
    {
        // DCJS 0-value rule: absent from an old settings.json -> false = off.
        Assert.False(Settings.Parse("{}").DebugLogging);

        var settings = new Settings { DebugLogging = true };
        Assert.True(Settings.Parse(settings.ToJson()).DebugLogging);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter DebugLogging_defaults_off_and_round_trips`
Expected: compile FAILURE — `'Settings' does not contain a definition for 'DebugLogging'`.

- [ ] **Step 3: Add the property**

In `src/eq2auras.Core/Config/Settings.cs`, after the `EscalationStyle` property:

```csharp
        [DataMember(Name = "debugLogging")]
        public bool DebugLogging { get; set; }   // global knob (SPEC §Diagnostic logging): off = lifecycle events only
```

- [ ] **Step 4: Run the full Core suite**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
Expected: PASS, zero failures.

- [ ] **Step 5: Commit**

```bash
git add src/eq2auras.Core/Config/Settings.cs tests/eq2auras.Core.Tests/SettingsTests.cs
git commit -m "Core: DebugLogging global knob (default off, DCJS 0-value)"
```

---

### Task 2: `LogRetentionPolicy` — pure retention decision (Core, TDD)

**Files:**
- Create: `src/eq2auras.Core/Diagnostics/LogRetentionPolicy.cs`
- Test: `tests/eq2auras.Core.Tests/LogRetentionPolicyTests.cs` (new file)

**Interfaces:**
- Produces: `LogFileInfo` (`string Path`, `DateTime LastWriteUtc`, `long SizeBytes`) and
  `LogRetentionPolicy.FilesToDelete(IEnumerable<LogFileInfo> files, DateTime nowUtc) -> List<string>`;
  constants `LogRetentionPolicy.RetentionDays = 14`, `LogRetentionPolicy.MaxTotalBytes = 200L * 1024 * 1024`.
  Task 3's plugin sweep is a thin IO wrapper around this.
- **Plan-watch item (sweep edge semantics), pinned here:** age source = **file last-write time (UTC)** — robust against filename-format drift and cheap to read; deletion = over-age first, then **oldest-first while total size exceeds the cap and more than one file survives** — so a lone newest file over 200 MB survives (newest always wins on size; age still applies to it).

- [ ] **Step 1: Write the failing tests**

Create `tests/eq2auras.Core.Tests/LogRetentionPolicyTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Eq2Auras.Core.Diagnostics;
using Xunit;

public class LogRetentionPolicyTests
{
    private static readonly DateTime Now = new DateTime(2026, 7, 9, 12, 0, 0, DateTimeKind.Utc);

    private static LogFileInfo File(string path, int ageDays, long sizeBytes)
        => new LogFileInfo { Path = path, LastWriteUtc = Now.AddDays(-ageDays), SizeBytes = sizeBytes };

    private static List<string> Doomed(params LogFileInfo[] files)
        => LogRetentionPolicy.FilesToDelete(files, Now);

    [Fact]
    public void Fresh_files_under_budget_are_all_kept()
    {
        Assert.Empty(Doomed(File("a", 1, 1000), File("b", 5, 1000), File("c", 13, 1000)));
    }

    [Fact]
    public void Files_older_than_the_window_are_deleted()
    {
        Assert.Equal(new[] { "old" }, Doomed(File("old", 15, 10), File("fresh", 2, 10)));
    }

    [Fact]
    public void Size_overflow_deletes_oldest_first_until_under_budget()
    {
        long mb80 = 80L * 1024 * 1024;
        var doomed = Doomed(File("newest", 1, mb80), File("middle", 2, mb80), File("oldest", 3, mb80));

        Assert.Equal(new[] { "oldest" }, doomed);   // 240 MB -> drop oldest -> 160 MB fits
    }

    [Fact]
    public void A_lone_newest_file_over_the_cap_survives()
    {
        Assert.Empty(Doomed(File("giant", 1, 300L * 1024 * 1024)));
    }

    [Fact]
    public void Giant_newest_still_evicts_everything_older()
    {
        var doomed = Doomed(File("giant", 1, 300L * 1024 * 1024), File("older", 2, 10));

        Assert.Equal(new[] { "older" }, doomed);    // newest wins; older can't fit any budget
    }

    [Fact]
    public void Age_and_size_rules_compose()
    {
        long mb150 = 150L * 1024 * 1024;
        var doomed = Doomed(
            File("ancient", 20, 10),                 // age
            File("big-old", 10, mb150),              // size (oldest survivor)
            File("big-new", 1, mb150));

        Assert.Equal(new[] { "ancient", "big-old" }, doomed);
    }

    [Fact]
    public void Input_order_does_not_matter()
    {
        long mb80 = 80L * 1024 * 1024;
        var doomed = Doomed(File("oldest", 3, mb80), File("newest", 1, mb80), File("middle", 2, mb80));

        Assert.Equal(new[] { "oldest" }, doomed);
    }

    [Fact]
    public void Empty_input_deletes_nothing()
    {
        Assert.Empty(LogRetentionPolicy.FilesToDelete(new List<LogFileInfo>(), Now));
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter LogRetentionPolicyTests`
Expected: compile FAILURE — `LogFileInfo`/`LogRetentionPolicy` do not exist.

- [ ] **Step 3: Implement the policy**

Create `src/eq2auras.Core/Diagnostics/LogRetentionPolicy.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace Eq2Auras.Core.Diagnostics
{
    /// One log file's facts, gathered by the plugin's IO layer.
    public sealed class LogFileInfo
    {
        public string Path { get; set; }
        public DateTime LastWriteUtc { get; set; }
        public long SizeBytes { get; set; }
    }

    /// The rolling-window retention decision (SPEC §Diagnostic logging): pure logic,
    /// no IO — the plugin enumerates files and deletes what this returns. Age uses
    /// last-write time; size deletes oldest-first; the newest file always survives
    /// the size rule (a problem report must always have the latest log).
    public static class LogRetentionPolicy
    {
        public const int RetentionDays = 14;
        public const long MaxTotalBytes = 200L * 1024 * 1024;

        public static List<string> FilesToDelete(IEnumerable<LogFileInfo> files, DateTime nowUtc)
        {
            var cutoff = nowUtc.AddDays(-RetentionDays);
            var all = files.ToList();

            var doomed = all
                .Where(f => f.LastWriteUtc < cutoff)
                .Select(f => f.Path)
                .ToList();

            var survivors = all
                .Where(f => f.LastWriteUtc >= cutoff)
                .OrderBy(f => f.LastWriteUtc)
                .ToList();

            long total = survivors.Sum(f => f.SizeBytes);
            int oldest = 0;
            while (total > MaxTotalBytes && survivors.Count - oldest > 1)
            {
                total -= survivors[oldest].SizeBytes;
                doomed.Add(survivors[oldest].Path);
                oldest++;
            }

            return doomed;
        }
    }
}
```

- [ ] **Step 4: Run the full Core suite**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
Expected: PASS — the eight new tests plus everything pre-existing. (Note `Age_and_size_rules_compose` expects age-doomed paths listed before size-doomed ones — the implementation appends in that order.)

- [ ] **Step 5: Commit**

```bash
git add src/eq2auras.Core/Diagnostics/LogRetentionPolicy.cs tests/eq2auras.Core.Tests/LogRetentionPolicyTests.cs
git commit -m "Core: LogRetentionPolicy — 14d/200MB rolling window, newest survives the size rule"
```

---

### Task 3: Plugin wiring — sweep IO, init-order flip, probe gating (CI-verified)

**Files:**
- Modify: `src/eq2auras.Plugin/Diagnostics/JsonlLogWriter.cs` (extract dir helper, add sweep)
- Modify: `src/eq2auras.Plugin/Act/TimerProbe.cs:20-33` (ctor), `:70-72` (poll gating)
- Modify: `src/eq2auras.Plugin/Eq2AurasPlugin.cs:32-39` (init order + probe construction)

**Interfaces:**
- Consumes: `Settings.DebugLogging` (Task 1), `LogRetentionPolicy.FilesToDelete` + `LogFileInfo` (Task 2).
- Produces: `JsonlLogWriter.SweepOldLogs()` (static, void); `TimerProbe` ctor becomes
  `TimerProbe(JsonlLogWriter log, Func<bool> debugLogging, Action<List<TimerReading>> onReadings)` — Task 4's checkbox needs no probe access (it mutates `_settings`, which the `Func` closes over).
- **Plan-watch items, pinned here:** (a) **init order flips** to load-settings → sweep → open-writer — today `Eq2AurasPlugin.cs:32-33` constructs the writer first; (b) **`TimerProbe` gets its flag via a `Func<bool>` ctor param** — checkbox handler and poll share ACT's UI thread, so no synchronization beyond that; (c) the sweep runs **before** the session's writer exists, so it can never touch the file about to open, and it is **skipped entirely when `DebugLogging` is on** (SPEC: verbose = keep everything).

- [ ] **Step 1: Extract the logs-directory helper and add the sweep to `JsonlLogWriter`**

In `src/eq2auras.Plugin/Diagnostics/JsonlLogWriter.cs`, add `using System.Linq;` (the only missing import — `Eq2Auras.Core.Diagnostics` is already imported). Replace the constructor's dir computation and add the static members:

```csharp
        public JsonlLogWriter()
        {
            var dir = LogsDirectory();
            Directory.CreateDirectory(dir);
            FilePath = Path.Combine(dir, "spike-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".jsonl");
            _writer = new StreamWriter(
                new FileStream(FilePath, FileMode.Create, FileAccess.Write, FileShare.Read),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) // no BOM — keeps JSONL clean
            {
                AutoFlush = true
            };
        }

        private static string LogsDirectory()
            => Path.Combine(ActGlobals.oFormActMain.AppDataFolder.FullName, "eq2auras", "logs");

        /// Rolling-window retention (SPEC §Diagnostic logging): decision is Core's
        /// LogRetentionPolicy; this is only the IO. Called BEFORE the session writer
        /// opens, and never when debug mode is on. Best-effort throughout — a locked
        /// or vanished file must not break plugin init.
        public static void SweepOldLogs()
        {
            try
            {
                var dir = LogsDirectory();
                if (!Directory.Exists(dir)) return;

                var files = new DirectoryInfo(dir)
                    .GetFiles("spike-*.jsonl")
                    .Select(f => new LogFileInfo
                    {
                        Path = f.FullName,
                        LastWriteUtc = f.LastWriteTimeUtc,
                        SizeBytes = f.Length
                    })
                    .ToList();

                foreach (var path in LogRetentionPolicy.FilesToDelete(files, DateTime.UtcNow))
                {
                    try { File.Delete(path); }
                    catch { }
                }
            }
            catch { }
        }
```

- [ ] **Step 2: Gate per-poll logging in `TimerProbe`**

Change the fields/ctor (currently `TimerProbe(JsonlLogWriter log, Action<List<TimerReading>> onReadings)`):

```csharp
        private readonly JsonlLogWriter _log;
        private readonly Func<bool> _debugLogging;
        private readonly Action<List<TimerReading>> _onReadings;
        private readonly Timer _pollTimer;

        public TimerProbe(JsonlLogWriter log, Func<bool> debugLogging, Action<List<TimerReading>> onReadings)
        {
            _log = log;
            _debugLogging = debugLogging;
            _onReadings = onReadings;
```

(rest of the ctor unchanged). In `OnPoll`, gate the dump — events elsewhere in the file stay unconditional:

```csharp
            if (_debugLogging())
            {
                foreach (var reading in readings) LogReading("poll", reading);
            }
            _onReadings(readings);
```

- [ ] **Step 3: Flip the init order and wire the probe in `Eq2AurasPlugin.InitPlugin`**

Replace lines 32-39 (`_log = ...` through the probe construction):

```csharp
            _settings = SettingsStore.Load();
            // Sweep BEFORE the session's log file opens; debug mode = keep everything.
            if (!_settings.DebugLogging) JsonlLogWriter.SweepOldLogs();
            _log = new JsonlLogWriter();
            _overlay = new OverlayHost(_settings);
            _overlay.Start();
            _engine = new OverlayEngine(_settings);   // trackers hold the same PanelSettings instances the tab mutates
            _probe = new TimerProbe(_log,
                () => _settings.DebugLogging,
                readings => _overlay.UpdateFrames(
                    _engine.Tick(readings)));
```

- [ ] **Step 4: Verify — Core suite still green locally (plugin compile is Task 5's CI push)**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
Expected: PASS. Do NOT attempt a plugin build on the Mac.

- [ ] **Step 5: Commit**

```bash
git add src/eq2auras.Plugin/Diagnostics/JsonlLogWriter.cs src/eq2auras.Plugin/Act/TimerProbe.cs src/eq2auras.Plugin/Eq2AurasPlugin.cs
git commit -m "Plugin: retention sweep before session open (skipped in debug mode), init-order flip, per-poll logging gated on DebugLogging"
```

---

### Task 4: Tab checkbox

**Files:**
- Modify: `src/eq2auras.Plugin/Eq2AurasPlugin.cs:88-98` (`BuildConfigTab` — after the move checkbox)

**Interfaces:**
- Consumes: `Settings.DebugLogging` (Task 1), `SettingsStore.Update` (existing persistence gate). Live-apply is free: the probe's `Func<bool>` re-reads `_settings.DebugLogging` every poll, and checkbox + poll share ACT's UI thread.

- [ ] **Step 1: Add the checkbox**

In `BuildConfigTab`, after the `moveBox` declaration block (`moveBox` sits at `Top = 674`):

```csharp
            var debugBox = new CheckBox
            {
                Text = "Debug logging (full per-tick dump)",
                Left = 10, Top = 702, Width = 280,
                Checked = _settings.DebugLogging
            };
            debugBox.CheckedChanged += (s, e) =>
                SettingsStore.Update(_settings, () => _settings.DebugLogging = debugBox.Checked);
```

and add it to the tab alongside the others:

```csharp
            tab.Controls.Add(moveBox);
            tab.Controls.Add(debugBox);
```

- [ ] **Step 2: Verify — Core suite unaffected**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
Expected: PASS (plugin-only edit; compile verified by Task 5's CI push).

- [ ] **Step 3: Commit**

```bash
git add src/eq2auras.Plugin/Eq2AurasPlugin.cs
git commit -m "Plugin: Debug logging checkbox on the tab (persisted, live-apply at next poll)"
```

---

### Task 5: Backlog status, CI push, merge-gate handoff

**Files:**
- Modify: `docs/backlog.md` (the in-flight Phase-1-odds entry)

- [ ] **Step 1: Update the backlog entry**

In `docs/backlog.md`, in the standing-items bullet that begins `- Phase-1 odds → IN FLIGHT (branch \`qol-debug-mode-log-retention\`, 2026-07-08):`, change `IN FLIGHT` to `IMPLEMENTED ON BRANCH, PENDING MERGE + LIVE VERIFY` and append to that bullet: ` Plan: docs/plans/2026-07-09-debug-mode-log-retention.md.`

- [ ] **Step 2: Commit and push the branch; watch verify-only CI**

```bash
git add docs/backlog.md
git commit -m "Backlog: debug mode + retention implemented on branch"
git push -u origin qol-debug-mode-log-retention
sleep 10   # give the workflow run a moment to register before listing
gh run watch $(gh run list --branch qol-debug-mode-log-retention --limit 1 --json databaseId --jq '.[0].databaseId') --exit-status
```

Expected: green — Core tests on `windows-latest` AND the net472+WPF plugin compile.

- [ ] **Step 3: Hand off at the merge gate — do NOT merge**

Report to Alex: branch ready (`git diff main..qol-debug-mode-log-retention`), CI green, live script:
1. **Default = events-only**: update, double-click a timer through a full lifecycle → the session JSONL contains `notify`/`warning`/`expire`/`removed` records and **zero `poll` records**.
2. **Live toggle**: check "Debug logging" (no reload), double-click again → `poll` records appear mid-file; uncheck → they stop.
3. **Retention**: with debug off, restart the plugin → files older than 14 days gone (the current folder is fresh post-wipe, so this is a code-review-verified case unless an old file is planted by hand — optional).
4. **Sunday prep**: check the Debug box before the raid (the backlog note).

---

## Self-Review

- **Spec coverage:** `DebugLogging` knob + DCJS default (T1); events-only vs full-dump gating + live-apply (T3+T4); rolling sweep 14d/200MB, newest-wins, suppressed-in-debug, before-writer-opens (T2+T3); tab checkbox in knob inventory (T4); empty-file deletion untouched (already shipped). Gap check against §Diagnostic logging: none found.
- **Plan-watch items:** init-order flip → T3 Step 3; probe `Func<bool>` wiring → T3 Steps 2-3; sweep edge semantics pinned (last-write-UTC age source, oldest-first, lone-newest survives size) → T2 with tests for each.
- **Placeholder scan:** clean — every code step shows complete code; every run step has command + expected outcome.
- **Type consistency:** `LogFileInfo`/`FilesToDelete(IEnumerable<LogFileInfo>, DateTime)` identical in T2 (definition) and T3 (consumption); `TimerProbe(JsonlLogWriter, Func<bool>, Action<List<TimerReading>>)` matches between T3 Steps 2 and 3; `debugLogging` JSON name matches T1's DataMember.
