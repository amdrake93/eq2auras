# Newest-Master Governing Rule Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task (project convention: executed inline, not via subagents, so Alex can watch). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace soonest-instance-governs with newest-master-governs (SPEC §Timer identity, approved 2026-07-08) so a recast instantly resets its timer to the list and DoT-tick instances never display, plus the approved diagnostics ride-alongs.

**Architecture:** The probe (`TimerProbe`) stays a dumb per-instance snapshotter and gains two copied fields (`MasterTimer`, `StartTime`); Core's `EscalationTracker` owns the new governing selection (masters only → newest `StartTime` → tie-break larger `TimeLeft`); everything downstream is untouched. Diagnostics log every instance with a `master` flag; frame-event records switch to largest-master + instance count; the log writer deletes never-written files.

**Tech Stack:** C# — `eq2auras.Core` (netstandard2.0, xunit tests, buildable on the Mac) and `eq2auras.Plugin` (net472+WPF, **never build locally** — compile-verified by branch CI).

## Global Constraints

- **Never build the Plugin project or solution on the Mac.** Core tests only: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`. Plugin changes are verified by branch-push CI (verify-only: Core tests + WPF compile + artifact, no publish).
- **Single-assembly packaging** — Core sources are `<Compile Include>`d into the plugin; no new assemblies, no `async` in the plugin project, no non-GAC types in plugin fields.
- **No `System.Web.Extensions`**, no `Assembly.LoadFrom`.
- Working branch: `newest-master-governs` (spec already on it: `d63d827` + `f4ef907`). Commits are plain descriptive messages, no ticket prefix.
- Merge to `main` only on Alex's explicit approval (merge = release to `dev-latest`).
- Reviewer implementation-watch items (backlog, 2026-07-08) are restated inline where each lands; the code review verifies all six.

---

### Task 1: Core governing rule — `TimerReading` fields + `EscalationTracker` selection

**Files:**
- Modify: `src/eq2auras.Core/Timers/TimerReading.cs`
- Modify: `src/eq2auras.Core/Timers/EscalationTracker.cs:26-37` (selection + stale comment) and `:96-111` (`WithResolvedColor` copy)
- Test: `tests/eq2auras.Core.Tests/EscalationTrackerTests.cs`
- Test (helper only): `tests/eq2auras.Core.Tests/OverlayEngineTests.cs:9-21`

**Interfaces:**
- Produces: `TimerReading.IsMaster` (`bool`) and `TimerReading.StartTime` (`System.DateTime`) — Task 3's probe sets both; `EscalationTracker.Tick` signature unchanged.
- Watch items covered: #1 (selection + comment), #2's Core half (`TimerReading` fields), #6 (filter lives HERE, in the display pipeline — the adapter keeps snapshotting every instance).

- [ ] **Step 1: Update the test helper and rewrite the two soonest-era tests as failing newest-master tests**

In `EscalationTrackerTests.cs`, replace the `Reading` helper with:

```csharp
    private static readonly System.DateTime BaseStart = new System.DateTime(2026, 7, 8, 20, 0, 0, System.DateTimeKind.Utc);

    private static TimerReading Reading(string name, int timeLeft,
        int warning = 10, int total = 30, string combatant = "none", int removeValue = -15,
        bool master = true, int startOffset = 0)
        => new TimerReading
        {
            Name = name, Combatant = combatant, TimeLeft = timeLeft,
            WarningValue = warning, TotalSeconds = total,
            RemoveValueSeconds = removeValue,
            RawPreciseTimeLeft = timeLeft, FillArgb = -16776961,
            IsMaster = master, StartTime = BaseStart.AddSeconds(startOffset)
        };
```

DELETE `Refire_does_not_extend_the_governing_countdown` (lines 118-132) and `Governing_instance_at_zero_goes_LATE_even_if_a_newer_instance_lingers` (lines 134-143) — both encode the dead rule, comments included. ADD these six in their place:

```csharp
    [Fact]
    public void Non_master_tick_never_governs_or_displays()
    {
        // A DoT tick re-trigger arrives as a non-master instance (ACT pre-applies the
        // OnlyMasterTicks config). It is diagnostics-only: the master keeps governing.
        var frame = new EscalationTracker().Tick(
            R(Reading("boss", 20, startOffset: 0), Reading("boss", 45, master: false, startOffset: 6)));

        var row = Assert.Single(frame.ListRows);
        Assert.Equal(20, row.TimeLeft);                      // NOT the tick's 45
    }

    [Fact]
    public void Master_recast_governs_over_the_older_master()
    {
        // Cooldown truth: the ability just fired, so the older prediction is falsified.
        var frame = new EscalationTracker().Tick(
            R(Reading("boss", 5, startOffset: 0), Reading("boss", 30, startOffset: 9)));

        Assert.Empty(frame.CenterElements);                  // 5s no longer governs -> no pie
        Assert.Equal(30, Assert.Single(frame.ListRows).TimeLeft);
    }

    [Fact]
    public void Master_recast_while_LATE_clears_the_LATE_instantly()
    {
        // THE raid-night bug (observed 51x, 2026-07-05): the overdue corpse must not
        // outrank a live recast while it awaits ACT's purge.
        var frame = new EscalationTracker().Tick(
            R(Reading("boss", -9, startOffset: 0), Reading("boss", 35, total: 35, startOffset: 44)));

        Assert.Empty(frame.CenterElements);                  // no LATE card survives the recast
        Assert.Equal(35, Assert.Single(frame.ListRows).TimeLeft);
    }

    [Fact]
    public void Newest_master_wins_even_with_less_time_than_an_older_modded_master()
    {
        // Timer mods can leave an older master with MORE remaining time; newest still wins
        // (deliberately not ACT's largest-master display — SPEC §Timer identity).
        var frame = new EscalationTracker().Tick(
            R(Reading("boss", 40, total: 60, startOffset: 0), Reading("boss", 30, total: 30, startOffset: 20)));

        Assert.Equal(30, Assert.Single(frame.ListRows).TimeLeft);
    }

    [Fact]
    public void No_masters_displays_nothing_for_the_key()
    {
        // A poll landing mid-purge can see a master-less frame; ACT kills it the same
        // engine pass. Ticks alone never earn display.
        var frame = new EscalationTracker().Tick(
            R(Reading("boss", 18, master: false), Reading("boss", 24, master: false, startOffset: 6)));

        Assert.Empty(frame.ListRows);
        Assert.Empty(frame.CenterElements);
    }

    [Fact]
    public void Overdue_master_stays_LATE_when_only_ticks_are_newer()
    {
        // A recast landing inside a still-running tick stream stays non-master: LATE
        // correctly holds until the overdue master purges (SPEC §The Overdue visual).
        var frame = new EscalationTracker().Tick(
            R(Reading("boss", -4, startOffset: 0), Reading("boss", 45, master: false, startOffset: 30)));

        Assert.Empty(frame.ListRows);
        var late = Assert.Single(frame.CenterElements);
        Assert.Equal(CenterElementKind.Late, late.Kind);
        Assert.Equal(4, late.LateSeconds);
    }

    [Fact]
    public void Equal_StartTime_masters_tie_break_to_the_larger_TimeLeft()
    {
        // Unreachable via ACT's 2s trigger dedup; defined for a total ordering.
        var frame = new EscalationTracker().Tick(
            R(Reading("boss", 5, startOffset: 0), Reading("boss", 30, startOffset: 0)));

        Assert.Equal(30, Assert.Single(frame.ListRows).TimeLeft);
    }
```

In `OverlayEngineTests.cs`, add `IsMaster = true` to its private `Reading` helper's initializer (line ~10-21) — without it every engine-routing test frame goes empty under the new filter.

- [ ] **Step 2: Run the Core tests to verify the new tests fail**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
Expected: compile FAILURE — `TimerReading` has no `IsMaster`/`StartTime` (that is the failing state for a data-shape change; note it and continue).

- [ ] **Step 3: Add the `TimerReading` fields**

In `TimerReading.cs`, add `using System;` at the top and two properties after `ShowInPanelB`:

```csharp
        public bool IsMaster { get; set; }       // SpellTimer.MasterTimer — display candidacy; non-masters are diagnostics-only (SPEC §Timer identity)
        public DateTime StartTime { get; set; }  // SpellTimer.StartTime — governing order: newest master wins
```

- [ ] **Step 4: Run the tests again — new behavior tests now fail red, not the compile**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
Expected: FAIL on exactly four —
- `Master_recast_governs_over_the_older_master` and `Equal_StartTime_masters_tie_break_to_the_larger_TimeLeft`: the old rule governs by the 5s reading, which escalates to a pie — `Assert.Empty(frame.CenterElements)` throws (not a wrong row value).
- `Master_recast_while_LATE_clears_the_LATE_instantly`: a LATE card is present — `Assert.Empty(frame.CenterElements)` throws.
- `No_masters_displays_nothing_for_the_key`: the old selection ignores `IsMaster`, so a row shows — `Assert.Empty(frame.ListRows)` throws.

Three PASS already and that is expected — their red phase was Step 2's compile failure, and each guards against a different wrong rule, not against soonest:
- `Non_master_tick_never_governs_or_displays` guards against naive newest-wins (which would return the tick's 45).
- `Newest_master_wins_even_with_less_time_than_an_older_modded_master` guards against largest-master/ACT-native display (which would return 40).
- `Overdue_master_stays_LATE_when_only_ticks_are_newer` coincides with soonest here (the overdue master is soonest).

Do not debug the three green tests; Step 6 confirms all seven hold under the new rule.

- [ ] **Step 5: Implement the new governing selection**

In `EscalationTracker.cs`, replace lines 28-37 (the stale soonest-governs comment AND the selection — watch item #1) with:

```csharp
            // Cooldown truth (SPEC §Timer identity; engine mechanics: docs/act-timer-engine.md):
            // the LATEST-FIRED MASTER instance governs — every trigger means the ability just
            // fired, so every older prediction is falsified. Non-master (DoT-tick) instances
            // are diagnostics-only and never display, exactly as ACT's native window treats
            // them. Timer mods can leave an older master with MORE remaining time; newest
            // still wins. Tie-break (unreachable via ACT's 2s dedup): larger TimeLeft.
            var governing = readings
                .Where(r => r.IsMaster)
                .GroupBy(KeyOf)
                .Select(g => g
                    .OrderByDescending(r => r.StartTime)
                    .ThenByDescending(r => r.TimeLeft)
                    .First())
                .Select(r => WithResolvedColor(r, paletteArgb))
                .ToList();
```

In `WithResolvedColor` (line ~96), the copy must stay lossless — add to the initializer alongside the existing fields:

```csharp
                IsMaster = reading.IsMaster,
                StartTime = reading.StartTime,
```

- [ ] **Step 6: Run the full Core test suite to verify everything passes**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
Expected: PASS, zero failures — the seven new/kept governing tests plus every pre-existing test (single-instance tests ride on the helper's `master: true` default).

- [ ] **Step 7: Commit**

```bash
git add src/eq2auras.Core/Timers/TimerReading.cs src/eq2auras.Core/Timers/EscalationTracker.cs tests/eq2auras.Core.Tests/EscalationTrackerTests.cs tests/eq2auras.Core.Tests/OverlayEngineTests.cs
git commit -m "Core: newest-master governing rule — masters only, newest StartTime, TimeLeft tie-break (SPEC §Timer identity)"
```

---

### Task 2: Diagnostics schema — `master` + `instances` on `TimerSnapshotRecord`

**Files:**
- Modify: `src/eq2auras.Core/Diagnostics/TimerSnapshotRecord.cs`
- Test: `tests/eq2auras.Core.Tests/TimerSnapshotRecordTests.cs`

**Interfaces:**
- Produces: `TimerSnapshotRecord.Master` (`bool?`) and `TimerSnapshotRecord.Instances` (`int?`) — Task 3 sets `Master` on per-instance records and `Instances` on frame-event records; JSONL emits both keys on every record (null where not applicable) so field captures self-explain.
- Watch item covered: #3.

- [ ] **Step 1: Update the two existing serialization tests and add the null/combination test (failing)**

In `TimerSnapshotRecordTests.cs`, extend the record initializer in `ToJsonl_serializes_all_fields` with `Master = true, Instances = null,` and change its expected string to:

```csharp
        Assert.Equal(
            "{\"kind\":\"poll\",\"ts\":1750000000000,\"name\":\"Tank Buster\"," +
            "\"combatant\":\"Big Bad\",\"timeLeft\":12,\"warningValue\":10,\"totalValue\":30," +
            "\"panelA\":true,\"panelB\":false,\"master\":true,\"instances\":null}",
            json);
```

In the escaping test, append `,\"master\":null,\"instances\":null` inside its expected string the same way (it sets neither new field). Add one new test:

```csharp
    [Fact]
    public void ToJsonl_frame_event_carries_null_master_and_an_instance_count()
    {
        // `removed` semantics (SPEC §Diagnostic logging): value null by construction,
        // positive instance count = evidence of killed non-masters.
        var record = new TimerSnapshotRecord
        {
            Kind = "removed", TimestampUnixMs = 2, Name = "x", Combatant = "y",
            TimeLeft = null, WarningValue = 0, TotalValue = 0,
            PanelA = false, PanelB = false, Master = null, Instances = 10
        };

        Assert.Contains("\"timeLeft\":null", record.ToJsonl());
        Assert.Contains("\"master\":null", record.ToJsonl());
        Assert.Contains("\"instances\":10", record.ToJsonl());
    }
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter TimerSnapshotRecordTests`
Expected: compile FAILURE — no `Master`/`Instances` members.

- [ ] **Step 3: Implement the schema fields**

In `TimerSnapshotRecord.cs`, add after `PanelB`:

```csharp
        public bool? Master { get; set; }   // per-instance records: SpellTimer.MasterTimer; null on frame events
        public int? Instances { get; set; } // frame events: live instance count at event time; null on per-instance records
```

And in `ToJsonl()`, before the closing brace append:

```csharp
            sb.Append(",\"master\":");
            if (Master.HasValue) sb.Append(Master.Value ? "true" : "false");
            else sb.Append("null");
            sb.Append(",\"instances\":");
            if (Instances.HasValue) sb.Append(Instances.Value);
            else sb.Append("null");
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
Expected: PASS (full suite — guards against other tests parsing the JSONL shape).

- [ ] **Step 5: Commit**

```bash
git add src/eq2auras.Core/Diagnostics/TimerSnapshotRecord.cs tests/eq2auras.Core.Tests/TimerSnapshotRecordTests.cs
git commit -m "Core: spike schema gains master flag + instance count (null where not applicable)"
```

---

### Task 3: Probe capture + truthful frame events (plugin — CI-verified, no local build)

**Files:**
- Modify: `src/eq2auras.Plugin/Act/TimerProbe.cs:51-72` (`OnPoll`), `:74-88` (`LogReading`), `:90-106` (`LogFrameEvent`)

**Interfaces:**
- Consumes: `TimerReading.IsMaster`/`.StartTime` (Task 1), `TimerSnapshotRecord.Master`/`.Instances` (Task 2), ACT's `SpellTimer.MasterTimer`/`.StartTime`/`.TimeLeft` (see `docs/act-timer-engine.md` §Data model).
- Watch items covered: #2 (probe half), #4, and #6's guard — `OnPoll` keeps snapshotting **every** instance; no master filtering here.

- [ ] **Step 1: Capture the two instance fields in `OnPoll`**

In the `readings.Add(new TimerReading { ... })` initializer, add:

```csharp
                        IsMaster = instance.MasterTimer,
                        StartTime = instance.StartTime,
```

- [ ] **Step 2: Flag per-instance records in `LogReading`**

In the `_log.Write(new TimerSnapshotRecord { ... })` initializer, add:

```csharp
                Master = reading.IsMaster,
```

(`Instances` stays null — per-instance records don't carry a count.)

- [ ] **Step 3: Rewrite `LogFrameEvent` to largest-master + instance count**

Replace the method body (currently reads `timers[0].TimeLeft` — the misattribution flaw) with:

```csharp
        private void LogFrameEvent(string kind, TimerFrame frame)
        {
            var timers = frame.SpellTimers;
            int? largestMaster = null;
            int instances = 0;
            if (timers != null)
            {
                instances = timers.Count;
                foreach (var timer in timers)
                {
                    if (timer == null || !timer.MasterTimer) continue;
                    if (!largestMaster.HasValue || timer.TimeLeft > largestMaster.Value)
                    {
                        largestMaster = timer.TimeLeft;
                    }
                }
            }

            _log.Write(new TimerSnapshotRecord
            {
                Kind = kind,
                TimestampUnixMs = NowMs(),
                Name = frame.Name ?? "",
                Combatant = frame.Combatant ?? "",
                TimeLeft = largestMaster,
                WarningValue = frame.TimerData != null ? frame.TimerData.WarningValue : 0,
                TotalValue = frame.TimerData != null ? frame.TimerData.TimerValue : 0,
                PanelA = frame.TimerData != null && frame.TimerData.Panel1Display,
                PanelB = frame.TimerData != null && frame.TimerData.Panel2Display,
                Instances = instances
            });
        }
```

(`TimeLeft` = largest-master is null by construction on `removed` — no masters remain — matching SPEC §Diagnostic logging exactly. `Master` stays null: frame events are not per-instance.)

- [ ] **Step 4: Verify — Core suite still green locally; plugin compile deferred to Task 5's CI push**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
Expected: PASS (probe isn't in the Core test compile; this catches accidental Core edits only). Do NOT attempt a plugin build on the Mac.

- [ ] **Step 5: Commit**

```bash
git add src/eq2auras.Plugin/Act/TimerProbe.cs
git commit -m "Plugin: probe captures MasterTimer + StartTime; frame events log largest-master + instance count"
```

---

### Task 4: `JsonlLogWriter` deletes never-written files (plugin — CI-verified)

**Files:**
- Modify: `src/eq2auras.Plugin/Diagnostics/JsonlLogWriter.cs`

**Interfaces:**
- Consumes: nothing new. Produces: no API change — `Dispose()` behavior only.
- Watch item covered: #5.

- [ ] **Step 1: Track writes and delete-on-dispose when empty**

Add a field beside `_writer`:

```csharp
        private bool _wroteAnything;
```

Set it in `Write` (inside the lock, after the null guard):

```csharp
                _wroteAnything = true;
```

Replace `Dispose` with:

```csharp
        public void Dispose()
        {
            lock (_gate)
            {
                if (_writer == null) return;
                _writer.Dispose();
                _writer = null;

                if (_wroteAnything) return;
                try { File.Delete(FilePath); }      // empty session artifact — best-effort cleanup
                catch { }                            // teardown must never throw over a leftover file
            }
        }
```

- [ ] **Step 2: Verify — Core suite unaffected**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
Expected: PASS (unchanged — plugin-only edit; compile verified by Task 5's CI push).

- [ ] **Step 3: Commit**

```bash
git add src/eq2auras.Plugin/Diagnostics/JsonlLogWriter.cs
git commit -m "Plugin: delete never-written spike log files on dispose"
```

---

### Task 5: Backlog status, CI sanity push, merge-gate handoff

**Files:**
- Modify: `docs/backlog.md` (the IN FLIGHT entry)

**Interfaces:** none — bookkeeping + verification.

- [ ] **Step 1: Update the backlog IN FLIGHT entry**

In `docs/backlog.md`, change the heading `### IN FLIGHT — newest-master governing rule (branch \`newest-master-governs\`)` to `### IMPLEMENTED, PENDING MERGE + LIVE VERIFY — newest-master governing rule (branch \`newest-master-governs\`)` and change `Fix (spec amendment in review):` to `Fix (spec + code on branch, plan `docs/plans/2026-07-08-newest-master-governs.md`):`. Leave the watch items and live script in place — the live script runs post-merge.

- [ ] **Step 2: Commit**

```bash
git add docs/backlog.md
git commit -m "Backlog: newest-master governing rule implemented on branch, pending merge + live verify"
```

- [ ] **Step 3: Push the branch and watch verify-only CI**

```bash
git push -u origin newest-master-governs
gh run watch $(gh run list --branch newest-master-governs --limit 1 --json databaseId --jq '.[0].databaseId') --exit-status
```

Expected: green — Core tests pass on `windows-latest` AND the net472+WPF plugin compiles (the only compile check the plugin changes get before merge).

- [ ] **Step 4: Hand off at the merge gate — do NOT merge**

Report to Alex: branch ready (`git diff main..newest-master-governs`), CI green, and the three-case live script from the backlog entry queued for post-merge verification: (1) recast >12s after a LATE trigger → LATE clears instantly to a full Calm bar; (2) re-fire within 12s → no reset, JSONL shows `master: false`; (3) no-activity session → no zero-byte log left. Merge and push happen only on Alex's explicit approval.

---

## Self-Review

- **Spec coverage:** §Timer identity rule → Task 1; adapter-snapshots-everything + masters-filter-in-display → Task 1/Task 3 split honors it; diagnostics `master` flag → Tasks 2+3; largest-master events + `removed` null-with-count → Tasks 2+3; empty-file deletion → Task 4; SPEC data-table `MasterTimer`/`StartTime` rows → Tasks 1+3. All six reviewer watch items mapped (1→T1, 2→T1+T3, 3→T2, 4→T3, 5→T4, 6→T1+T3). No gaps found.
- **Placeholder scan:** none — every code step shows the code, every run step has a command + expected outcome.
- **Type consistency:** `IsMaster`/`StartTime` names identical across Task 1 (definition), Task 1 Step 5 (selection), Task 3 (probe); `Master`/`Instances` identical across Tasks 2 and 3; tie-break `ThenByDescending(TimeLeft)` matches the Task 1 test.
