# Update-notify recurring poll + manual-check feedback — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the plugin communicate update status — a manual "Check for updates" click reports its outcome under the button, and a recurring 5-minute background poll surfaces a mid-session release without a restart.

**Architecture:** Both halves are thin wiring in `Eq2AurasPlugin` on top of the existing `SelfUpdater` (whose outcome strings already exist) and the existing thread-safe label setters. Part 1 routes the manual button's status sink to a new `SetUpdateMessage` helper that writes **both** the tab notice (`_updateNotice`, under the button) and ACT's status label (`_statusLabel`), and sets an instant `checking for updates…` on click. Part 2 extracts the startup notify block into a reusable `CheckForUpdateNotify()` and drives it from a dedicated 5-minute WinForms `Timer`, disposed on unload.

**Tech Stack:** C# / .NET Framework 4.7.2 Plugin project (WinForms tab UI + WPF overlay), `System.Windows.Forms.Timer`, existing `SelfUpdater` (sync-over-async on a background thread).

## Global Constraints

- **Plugin project is transcribe-only.** It cannot be built or unit-tested on the Mac (net472 + WPF/WinForms). Verification is: branch-push verify CI (WPF/plugin compiles) + Alex's on-box field script. There is **no Core logic** in this change, so no Core tests are added; Core's existing suite remains the Core regression guard.
- **Scan-safety (SPEC §Packaging / hard rules):** no `async` added to the Plugin project; no non-GAC types in fields. `System.Windows.Forms.Timer` is a GAC type — safe as a field. `SelfUpdater` already does its own `Task.Run` internally; this plan does not add async.
- **Single-assembly packaging:** no new project references; all edits live in the existing `src/eq2auras.Plugin/Eq2AurasPlugin.cs`.
- **Reuse existing outcome strings.** The only new user-facing string is the transient `checking for updates…`. The five outcome strings (`already up to date (v‹X›)`, `downloading …`, `update v‹X› installed — reloading…`, `update failed: …`, `no ‹tag› release yet`) live in `src/eq2auras.Plugin/SelfUpdate/SelfUpdater.cs` and are not modified.
- **Behavior per SPEC §Release channels** — *Notify on startup, and on a recurring poll* + *The manual "check for updates" click reports its outcome on the tab* (branch `update-notify-poll-and-feedback`, `docs/SPEC.md`).

## File Structure

- **Modify only:** `src/eq2auras.Plugin/Eq2AurasPlugin.cs`
  - add a `_updatePollTimer` field;
  - add `SetUpdateMessage(string)` and `CheckForUpdateNotify()` helpers;
  - change the update-button `Click` handler (Part 1);
  - extract the startup notify block to `CheckForUpdateNotify()` and add the timer in `InitPlugin` (Part 2);
  - dispose the timer in `DeInitPlugin` (Part 2).
- **Docs:** `docs/backlog.md` — refresh the two stale cross-references and record this feature (Task 3).

No new files. No Core changes.

---

### Task 1: Manual "check for updates" click reports its outcome on the tab

**Files:**
- Modify: `src/eq2auras.Plugin/Eq2AurasPlugin.cs` — the update-button `Click` handler (currently `:89-92`); add helper `SetUpdateMessage` near the other thread-safe setters (`:348-357`).
- Test: none (transcribe-only Plugin; verified by CI compile + field script).

**Interfaces:**
- Consumes: `SetStatusThreadSafe(string)` (`:348`), `SetTabNoticeThreadSafe(string)` (`:353`), `ReloadSelf` (`:361`), `SelfUpdater(Action<string> status, Action applyReload)` + `.RunInBackground(pluginsDir, betaChannel, version)` (`src/eq2auras.Plugin/SelfUpdate/SelfUpdater.cs:25,33`).
- Produces: `private void SetUpdateMessage(string message)` — writes a message to both the ACT status label and the tab notice; used by the manual-click path in this task and available to the poll path in Task 2.

- [ ] **Step 1: Add the `SetUpdateMessage` helper.**

Insert directly above `SetStatusThreadSafe` (`:348`):

```csharp
        /// Writes an update message to BOTH the tab notice under the button (where the user
        /// is looking after clicking) and ACT's plugin status label. Used for the manual
        /// "check for updates" click lifecycle so a check that finds nothing is still visible.
        private void SetUpdateMessage(string message)
        {
            SetStatusThreadSafe(message);
            SetTabNoticeThreadSafe(message);
        }
```

- [ ] **Step 2: Route the button click through it, with an instant "checking…" state.**

Replace the current handler (`:89-92`):

```csharp
            var updateButton = new Button { Left = 10, Top = 12, Width = 150, Text = "Check for updates" };
            updateButton.Click += (s, e) =>
                new SelfUpdater(SetStatusThreadSafe, ReloadSelf)
                    .RunInBackground(pluginsDir, _settings.BetaChannel, _version);
```

with:

```csharp
            var updateButton = new Button { Left = 10, Top = 12, Width = 150, Text = "Check for updates" };
            updateButton.Click += (s, e) =>
            {
                SetUpdateMessage("checking for updates…");   // instant feedback under the button; overwritten by the outcome
                new SelfUpdater(SetUpdateMessage, ReloadSelf)
                    .RunInBackground(pluginsDir, _settings.BetaChannel, _version);
            };
```

Note: the `Click` handler runs on ACT's UI thread, so the synchronous `SetUpdateMessage("checking for updates…")` (which `Invoke`s to the UI thread) paints before `RunInBackground` spawns its background task; the outcome message replaces it when the check returns. Passing `SetUpdateMessage` as the updater's status sink routes every outcome to both labels.

- [ ] **Step 3: Verify (compile + field).** No local build. Confirm on branch-push CI that the plugin compiles. On-box (Task-1 slice of the field script): click **Check for updates** when up-to-date → the tab notice under the button shows `checking for updates…` then `already up to date (v‹X›)`.

- [ ] **Step 4: Commit.**

```bash
git add src/eq2auras.Plugin/Eq2AurasPlugin.cs
git commit -m "Manual check-for-updates reports outcome on the tab notice + instant checking state"
```

---

### Task 2: Recurring 5-minute notify poll

**Files:**
- Modify: `src/eq2auras.Plugin/Eq2AurasPlugin.cs` — add `_updatePollTimer` field (fields block `:17-26`); extract the startup notify block (`:57-67`, comment + call) into `CheckForUpdateNotify()`; start the timer in `InitPlugin`; dispose it in `DeInitPlugin` (`:70-81`).
- Test: none (transcribe-only; CI compile + field script).

**Interfaces:**
- Consumes: `SetUpdateMessage(string)` (from Task 1), `SetStatusThreadSafe` (`:348`), `ReloadSelf` (`:361`), `SelfUpdater(...).CheckInBackground(betaChannel, version, Action<string> onUpdateAvailable)` (`src/eq2auras.Plugin/SelfUpdate/SelfUpdater.cs:44`), `System.Windows.Forms.Timer` (the `using System.Windows.Forms;` at the top of the file makes `Timer` resolve to the WinForms timer, as in `TimerProbe.cs`).
- Produces: `private void CheckForUpdateNotify()` — the notify-only check (surfaces `update available: v‹X›` only when an update exists); called at startup and per timer tick.

> Depends on Task 1 (uses `SetUpdateMessage`); implement Task 1 first.

- [ ] **Step 1: Add the timer field.**

In the field block (`:17-26`), after `private Settings _settings;`, add:

```csharp
        private Timer _updatePollTimer;
```

(`Timer` = `System.Windows.Forms.Timer`; the `using System.Windows.Forms;` is already present.)

- [ ] **Step 2: Extract the startup notify block into a reusable method.**

Replace the current inline startup check in `InitPlugin` (`:57-67`):

```csharp
            // Notify-only startup check on the selected channel (SPEC §Notify on startup).
            // Best-effort, background; never blocks InitPlugin, never auto-installs.
            // Surfaces the notice on BOTH the tab label and the ACT status label ("both … and").
            new SelfUpdater(SetStatusThreadSafe, ReloadSelf).CheckInBackground(
                _settings.BetaChannel, _version,
                available =>
                {
                    var notice = "update available: v" + available + " — click \"Check for updates\"";
                    SetTabNoticeThreadSafe(notice);
                    SetStatusThreadSafe(notice);
                });
```

with a call plus the timer setup:

```csharp
            // Notify-only update check (SPEC §Notify on startup, and on a recurring poll):
            // best-effort, background, never auto-installs. Runs now and every 5 minutes so a
            // build published while the plugin is loaded surfaces without a restart.
            CheckForUpdateNotify();
            _updatePollTimer = new Timer { Interval = 5 * 60 * 1000 };
            _updatePollTimer.Tick += (s, e) => CheckForUpdateNotify();
            _updatePollTimer.Start();
```

Then add the extracted method next to the other update helpers (directly above `SetUpdateMessage` from Task 1):

```csharp
        /// Notify-only channel check (SPEC §Notify on startup, and on a recurring poll):
        /// no download, no reload. Surfaces "update available: v‹X›" on both the tab notice
        /// and ACT's status label (via SetUpdateMessage), and only when an update exists.
        private void CheckForUpdateNotify()
        {
            new SelfUpdater(SetStatusThreadSafe, ReloadSelf).CheckInBackground(
                _settings.BetaChannel, _version,
                available => SetUpdateMessage("update available: v" + available + " — click \"Check for updates\""));
        }
```

- [ ] **Step 3: Dispose the timer on unload.**

At the top of `DeInitPlugin` (`:70`, before `_probe?.Dispose();`), add:

```csharp
            _updatePollTimer?.Stop();
            _updatePollTimer?.Dispose();
            _updatePollTimer = null;
```

- [ ] **Step 4: Verify (compile + field).** No local build. Confirm branch-push CI compiles. On-box (Task-2 slice of the field script): leave the plugin open, publish a new `dev-latest` build, and within ~5 minutes the tab notice shows `update available: v‹X›` with no restart/re-enable; unload the plugin → no error (timer stops/disposes cleanly).

- [ ] **Step 5: Commit.**

```bash
git add src/eq2auras.Plugin/Eq2AurasPlugin.cs
git commit -m "Add recurring 5-minute notify poll so mid-session releases surface without restart"
```

---

### Task 3: Backlog housekeeping (spec-review nit + record the feature)

**Files:**
- Modify: `docs/backlog.md`

**Interfaces:** none (docs only).

- [ ] **Step 1: Reframe the "grey out Check-for-updates" idea entry.** It was the origin of this work but the design did a 180 (the problem was communication, not the button state). Update that entry to record: superseded by this communication approach (click-outcome feedback + recurring poll), branch `update-notify-poll-and-feedback`.

- [ ] **Step 2: Mark the "Update-notify caching observation" addressed.** Note that a mid-session build now surfaces within one 5-minute poll interval without a disable/re-enable — the recurring poll resolves the observed behavior (it was the missing re-poll, not an HTTP cache).

- [ ] **Step 3: Refresh the one stale section-name cross-reference.** Only the grey-out IDEA entry cites the heading — `docs/backlog.md`, the "grey out 'Check for updates' when no update is detected" entry: "…notify string, SPEC §Release channels — *Notify on startup*". That heading is now "Notify on startup, and on a recurring poll" (`docs/SPEC.md:485`); update that single pointer. (The caching-observation entry has **no** §-heading pointer — Step 2 covers its content — and the historical go-public entry's descriptive "Notify-on-startup" prose is not a heading citation, so leave it as-is.)

- [ ] **Step 4: Commit.**

```bash
git add docs/backlog.md
git commit -m "Backlog: record update-notify feature; refresh stale §Notify cross-refs [skip ci]"
```

---

## Testing strategy

Plugin code is transcribe-only (no Mac build; no WPF/WinForms unit harness). Gates:

1. **Branch-push verify CI** — the plugin/WPF must compile.
2. **On-box merge-gate field script** (Alex):
   1. **Up-to-date click feedback.** On a current build, click **Check for updates** → tab notice under the button shows `checking for updates…` then `already up to date (v‹X›)`. (Previously showed nothing on the tab.)
   2. **Update-available click.** With an update on the selected channel, click → `checking…` → `downloading …` → `update v‹X› installed — reloading…` → the plugin reloads.
   3. **Recurring poll surfaces a mid-session release.** Leave the plugin loaded; publish a new `dev-latest` build; within ~5 min the tab shows `update available: v‹X›` **with no restart or re-enable**. (Confirms the re-poll, ruling out the earlier caching observation.)
   4. **Clean unload.** Disable the plugin → no error; re-enable → startup notify + poll resume.
   5. **Channel interaction (Item A regression guard).** Toggling Beta still does not auto-install (Item A); the poll/notice reflect the selected channel after the next poll or a manual click.

## Self-review notes

- **Spec coverage:** §*Notify on startup, and on a recurring poll* → Task 2 (startup call + 5-min timer, notify-only, disposed on unload). §*The manual "check for updates" click reports its outcome on the tab* → Task 1 (`SetUpdateMessage` dual write + instant `checking…`). §Deploy & reload clarification (install on button only) → unchanged code; the poll uses `CheckInBackground` (notify-only), preserving the split.
- **No placeholders:** every code step shows the full replacement text.
- **Type consistency:** `SetUpdateMessage(string)`, `CheckForUpdateNotify()`, `_updatePollTimer` (`System.Windows.Forms.Timer`) are used consistently across tasks; `CheckInBackground`/`RunInBackground` signatures match `SelfUpdater.cs`.
- **Cadence:** `Interval = 5 * 60 * 1000` ms = 5 min (≈12 checks/hr/user; GitHub unauthenticated limit is 60/hr per IP — trivial, per SPEC).
