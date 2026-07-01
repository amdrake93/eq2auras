# Spike Findings — ACT API reconnaissance + live behaviour

Running record of what we've confirmed about ACT's timer system. Sources: `[decompiled]` = read from `ThirdParty/Advanced Combat Tracker.exe` via `ilspycmd` on the Mac (Task 1.5); `[live]` = observed in a running ACT (Task 6 / Task 8) — pending.

## API shapes (Task 1.5, `[decompiled]` from ACT 2.5MB assembly)

- **`SpellTimer.TimeLeft` is `int`** (whole seconds), computed as
  `TimerFinalDuration - Convert.ToInt32((LastEstimatedTime - startTime).TotalSeconds)` — **no clamp, so it goes negative after expiry.** This resolves a spec `[U]`: overdue count-up can read `−TimeLeft` directly. (Drove `TimerSnapshotRecord.TimeLeft` → `int?`, null = no live timer.)
- **`TimerData.RemoveValue` defaults to `-15`** — ACT's removal threshold is ~15s past zero. Resolves part of the `RemoveValue` `[U]` (still confirm the exact drop moment in `GetTimerFrames()` live).
- **`FormSpellTimers.GetTimerFrames()` → `List<TimerFrame>`** (and a `GetTimerFrames(int PanelNum)` overload; the no-arg calls panel 0).
- **Events** `OnSpellTimerNotify/Warning/Expire/Removed` are `event SpellTimerEventDelegate`, invoked as `handler(TimerFrame)` — handler signature is **`void (TimerFrame)`**.
- **`TimerFrame`**: `TimerData TimerData {get;set;}`, `string Combatant {get;set;}`, `List<SpellTimer> SpellTimers {get;set;}`, `string Name => TimerData.Name`, `int WarningValue => TimerData.WarningValue`, `int[] TimerVals`, `bool RadialDisplay`. (`Name` dereferences `TimerData`, which the constructor always sets — non-null in practice.)
- **`TimerData`**: `int WarningValue` (default 10), `int TimerValue` (default 30), `Color FillColor` (default `Blue`), `string Category`, `int RemoveValue` (default -15), `bool RadialDisplay`.
- **`SpellTimer`**: `DateTime StartTime`, `int TimerFinalDuration` (post-mod), `int TimeLeft`.

All member names/types the plan's `TimerProbe` assumes are confirmed (only `TimeLeft`'s type differed — was `double` in the draft, corrected to `int`).

## Bitness (Task 1.5, informational only)

`file` reports `PE32 executable (GUI) Intel 80386 Mono/.Net assembly`. For a managed AnyCPU exe the PE32 header is not conclusive, but a **separate `ACTx86.exe` ships alongside** the main exe, which indicates the main `Advanced Combat Tracker.exe` is the AnyCPU/64-bit build. **This gates no code** — overlay click-through uses the int `GetWindowLong`/`SetWindowLong` exports, correct on both bitnesses.

## Build / CI (Task 3, confirmed in the cloud)

- **SDK-style WPF on `net472` compiles in GitHub Actions** (run `28546103013`) — the XAML `MarkupCompilePass` runs and succeeds. **The legacy-csproj fallback (Task 3 Step 4) is NOT needed.** The #1 project-format risk is retired.
- **`System.Web.Extensions` breaks the WPF XAML markup compiler** (run `28545971312`): it drags in `System.Web`, which the markup compiler's metadata assembly-resolver can't resolve → `Could not find assembly 'System.Web…'`. **Consequence for Task 9:** the self-updater must **not** use `System.Web.Extensions`/`JavaScriptSerializer` for JSON. Parse the GitHub API response another way (e.g. `DataContractJsonSerializer` from `System.Runtime.Serialization`, or minimal hand-parsing) — anything that avoids pulling `System.Web` into a project that also does WPF markup compilation.
- `dotnet test` of the net10.0 Core tests runs green on `windows-latest` too (via `actions/setup-dotnet@v4` `10.0.x`).

## Live behaviour (Task 6 / Task 8) — PENDING

- [ ] Does `TimeLeft` reported via `GetTimerFrames()` actually go negative live (confirming the decompiled formula)?
- [ ] Exact `TimeLeft` value when a frame drops from `GetTimerFrames()` (the `RemoveValue` moment).
- [ ] What a reset (ability fired) looks like — `TimeLeft` jumping back to full.
- [ ] Distribution of `WarningValue` across the team's real timer set.
- [ ] Does re-enabling the plugin run NEW bytes (reload verdict, Task 8)?
- [ ] Does an un-merged `Core.dll` block reload (ILRepack premise, Task 8)?
- [ ] WPF transparency/click-through/animation over the game; dedicated-STA thread model OK (Task 7).
