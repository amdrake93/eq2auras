# eq2auras

ACT (Advanced Combat Tracker) overlay-suite plugin for EverQuest 2 — north star: "WeakAuras for EQ2". Single shipped DLL, self-updating, WPF overlay over the game.

## Source of truth — read these first
- `docs/SPEC.md` — how the whole system works NOW (architecture, escalation model, knob/config model, packaging, dev cycle). Present-tense, no changelog framing.
- `docs/backlog.md` — triaged next work (top item = what's queued) + guild feedback.
- `docs/act-timer-engine.md` — ACT engine ground truth (decompiled + field-verified: data model, trigger pipeline, master flags, purge rules). Trust over API docs; wins wherever an older doc conflicts.
- `docs/act-parse-engine.md` — ACT combat-data ground truth, sibling to the timer doc (decompiled: encounter/combatant model, ExportVariables, mini parse, plugin hook points, threading). The Parse Meter module's data-side reference; `docs/details-addon-reference.md` is its display-side design input (Details! architecture patterns).
- `docs/plans/2026-07-01-spike-findings.md` — the Phase-0 spike record. Historical; partially superseded (see the engine doc's §Supersessions).
- `docs/plans/` — dated implementation plans (`YYYY-MM-DD-<name>.md`), historical once executed.
- `docs/review-workflow.md` — the two-session writer/reviewer process contract (review types, feedback-block format, decision routing, bootstrap for new contributors/agents).
- `docs/sessions/` — session chronicles: the story between the docs (sagas, reversals, why-chains), written when a long context gets saved off. Read the latest one to catch up on how we work, not just what exists.

## The two-machine reality
- **All development happens here on the Mac** (company Claude license). Alex's personal Windows box runs ACT+EQ2 and is a **passive test target only** — no Claude, no toolchain there. Never ask him to develop on it.
- Only `eq2auras.Core` (netstandard2.0) builds/tests locally: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`. **Never** try to build the Plugin/solution on the Mac (net472+WPF).
- Ship loop: commit → push to `main` → GitHub Actions (`windows-latest`, msbuild) publishes single `eq2auras.dll` to the rolling `dev-latest` prerelease → Alex clicks **"Check for updates"** in ACT → plugin downloads + live-reloads itself (no ACT restart). Watch CI: `gh run watch <id> --exit-status`.
- Live verification is Alex's (often streaming to guildmates who act as testers/jury). Give him short concrete "do X, expect Y" scripts.

## Hard-won engine rules — violate these and the plugin breaks in the field
- **Single-assembly packaging**: Core sources are `<Compile Include>`d into the plugin. Never reference a second DLL; ACT's pre-`InitPlugin` type scan resolves all field types (including async state-machine hoisted locals!) before any resolver could run. Consequence: **no `async` in the plugin project** carelessly, and no non-GAC types in fields unless compiled in.
- **Never reference `System.Web.Extensions`** (breaks the WPF XAML markup compiler in CI). JSON = `DataContractJsonSerializer`. DCJS skips field initializers on deserialize → **enum knob defaults must be the 0-value**.
- **Never `Assembly.LoadFrom`** (locks files for the process lifetime).
- **The wall clock owns the visuals** — ACT's `TimeLeft`/clock is log-line-driven and lurches when the log is quiet. Smooth animations from `RawPreciseTimeLeft`; ACT's ints for state decisions only.
- **Never outlive the data**: display exactly what ACT reports. Instance governance (master flags, newest-master rule, LATE windows) lives in SPEC §Timer identity + `docs/act-timer-engine.md` — read those before touching governing logic; don't restate the mechanics here.
- **WPF: retain elements, animate properties** — never rebuild visuals per tick (kills animations); one linear drain-to-zero animation, re-targeted only past ~0.75s drift.
- Timer **color** identity is keyed by normalized timer NAME only (ability identity — see SPEC §Timer colors).

## Working style (learned, keep honoring)
- **Alex owns the phase transitions** (brainstorm → spec → plan → implement). Finish the current phase's artifact and stop; auto-accept mode ≠ drive the workflow. When he pauses for a manual step, wait for an explicit "go" — don't resume off an ambiguous reply. **Discussion is not approval**: affirming comments mid-discussion ≠ green light; no file edits until an explicit "write it up", and one artifact per turn — he reads along live.
- **A separate reviewer agent reviews every written artifact** (code rounds on request) — the full contract is `docs/review-workflow.md`. The automated review loop (contract §Automated orchestration) runs only on Alex's explicit per-artifact invocation; the loop then counts as one authorized turn. In short: process pasted feedback with `superpowers:receiving-code-review`; record plan-watch items in the backlog and "no change" decisions in a doc (the reviewer can't see this conversation and re-raises anything unrecorded).
- **Fix flow** (field-driven reversals/small reworks): spec + code on one branch, no plan doc — legitimate, but it **still pauses for the reviewer's spec verification before Alex's merge gate**. Present fixes as ready-for-review, never ready-to-merge. Every fast fix so far left stragglers the review caught.
- Spec amendments **before** technical plans. Plans in `docs/plans/`, executed inline (not subagent) so he can watch; strict TDD in Core.
- **Development happens on branches** (descriptive names, e.g. `slice5-<name>`; no ticket prefixes). Alex reviews via `git diff main..<branch>` — merge to `main` and push **only on his explicit approval**. Branch pushes run verify-only CI (Core tests + the WPF plugin compile + build artifact, no publish); pushing `main` is releasing — the `dev-latest` prerelease the self-updater consumes publishes from `main` only. Doc-only tweaks (backlog notes, spec typos) may still go straight to `main`.
- Diagnostic JSONL logs land on the Windows box (`%APPDATA%\Advanced Combat Tracker\eq2auras\logs`); ferry them back via the repo (`spike-data/`, gitignore-excepted) for analysis here.
- The GitHub update token: fine-grained PAT, contents:read, this repo only — it must never appear in chat/commits; it lives in his password manager + DPAPI on the Windows box.
