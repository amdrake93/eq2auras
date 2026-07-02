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

## Live behaviour (Task 4 / 6 / 7 / 8)

Confirmed live in ACT:
- [x] **Plugin loads** (`eq2auras.dll` Browse-added) and `InitPlugin` runs fully — log dir + logger + probe + overlay all initialize. `Core.dll` (netstandard2.0) loads in ACT's .NET Framework host **without** a facade/binding-redirect problem — the netstandard worry is retired.
- [x] **WPF renders inside ACT**: transparent layered window on a dedicated STA thread + Dispatcher, with storyboard animation (pulsing box), live. Dedicated-STA thread model works — no ACT-UI-thread fallback needed.
- [x] Root cause of the earlier "core threw an error": `eq2auras.Core.dll` was Browse-added as a plugin (it's a dependency, not an `IActPluginV1`). Add only `eq2auras.dll`.

- [x] **Dependency resolution gotcha (fixed).** A timer firing threw `Could not load file or assembly 'eq2auras.Core' … cannot find the file`, spammed every poll. Cause: `Core.dll` is in the Plugins folder but ACT **does not probe the Plugins folder for a plugin's dependencies** — strong evidence ACT loads the plugin from **raw bytes** (no file location → CLR searches only ACT's app dir + GAC). `InitPlugin`/box worked because they touch no `Core` type; the failure only hit when `LogFrame` first used `TimerSnapshotRecord`. **Fix:** `PluginAssemblyResolver` registers `AppDomain.AssemblyResolve` (first line of `InitPlugin`) to `LoadFrom` our deps in the Plugins folder.
  - **Bonus for Task 8:** ACT loading plugins from bytes is the exact precondition under which the overwrite-file + toggle-`cbEnabled` reload can run *new* bytes. Encouraging sign for live self-update, still to be confirmed.

Still pending:
- [x] **Click-through confirmed** — clicks pass through the box to the game behind it (WS_EX_LAYERED|WS_EX_TRANSPARENT works).
- [ ] **Clean teardown** — disable the plugin: box vanishes, no error; re-enable: returns. (Implicitly exercised during testing; confirm explicitly.)
- [x] **Live timer data captured** (`spike-20260701-192959.jsonl`, timer `Holy Shield` warn=10 total=30):
  - **`timeLeft` goes negative** — `30→…→0→−1` confirms the decompiled `duration − elapsed` (no clamp).
  - **`warning` fires at `tL=10`** (= `warningValue`), **`expire` at `tL=0`**.
  - **Reset observed** — `timeLeft` jumped back to `30` on re-fire (ability fired again). Reset = jump toward `total`.
- [ ] **`RemoveValue=-15` decay NOT cleanly observed** — reached only `−1` before a re-trigger interrupted; `removed` events fired at `tL=2/1` (muddied by overlap). Needs a single timer left to fully expire untouched (~20s).
- [ ] **`WarningValue` distribution** — only one timer type observed; needs more variety.

### Design inputs for the NEXT (feature) plan — surfaced by two near-simultaneous triggers of the same timer
- **Identity key `(Name, Combatant)` is insufficient when `combatant="none"`** (timer not tied to a caster/target) — two concurrent instances become indistinguishable. The real overlay needs a per-instance key.
- **Concurrent instances share ONE `TimerFrame`** — `TimerFrame.SpellTimers` is a `List`; the probe logs only `[0]`, losing the others (seen as `8→7→8` flicker and odd `removed tL=2/1`). The overlay must iterate all `SpellTimers`, not just `[0]`.

### Minor bug fixed
- Log had a **UTF-8 BOM** (`StreamWriter(..., Encoding.UTF8)`) → JSONL parse needed `utf-8-sig`. Fixed to `new UTF8Encoding(false)` (BOM-less).
- [x] **Reload verdict (Task 8) — RESTART REQUIRED.** On trying to re-add an updated plugin, ACT reported *"duplicate plugin found, file already loaded into memory, restart ACT."* ACT keeps the plugin assembly resident by identity; neither re-adding nor (by the same reasoning) toggling Enabled swaps in new bytes. **A new build requires a full ACT restart to load.** ⇒ **Task 9 self-update = download-then-prompt-restart, NOT live hot-swap** (matches the predicted WPF outcome). The two-DLL vs ILRepack "blocks reload" question is moot for hot-reload (there is none); AssemblyResolve handles the dependency at load time, so two DLLs is fine.
- [x] **Green build loads after ACT restart** (confirms the restart-to-update path) and **a timer counting down no longer throws** — the `AssemblyResolve` fix is verified live. Dependency-resolution bug closed.
