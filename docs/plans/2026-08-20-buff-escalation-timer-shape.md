# Buff-window Escalation Config + General Timer-Shape Overrides — Implementation Plan

> **For agentic workers:** this plan is executed **inline** via `superpowers:executing-plans` (eq2auras convention — Alex watches; not subagent-driven). Steps use checkbox (`- [ ]`) syntax. Every Core task is **strict TDD**; Plugin tasks are **transcribe-only** (net472/WPF — CI-compile-gated + on-box verification).

**Goal:** Make escalation a genuine per-window config with a `None` (no-escalation) option, default the buff window to `None`, generalize the per-buff prefs into timer-shape overrides (duration/warning/remove), and inject buff `TimerData` from effective shape values with panel-routing flags off (fixing the Panel A leak).

**Architecture:** Core owns the pure model: `EscalationStyle` gains `None`; `PanelSettings.EscalationStyle` becomes **nullable** with a **source-keyed** default (`EscalationDefaults.Resolve`: the `category:eq2auras Buffs` group → `None`, else `CenterRadial`); `EscalationTracker` short-circuits `None` to a calm live-only list; a **one-time migration marker** nulls a pre-existing buff window's escalation; `BuffPref` grows `warnOverride`/`removeOverride` with `Settings.EffectiveWarning`/`EffectiveRemove` (base 0) + the same out-of-range clamp as duration. The Plugin injector builds each `TimerData` from the effective shape with `Panel1Display = Panel2Display = false`; the tab gains a buff-tracker section (escalation dropdown + per-buff warning field).

**Tech Stack:** C# — Core `netstandard2.0`, xUnit in `tests/eq2auras.Core.Tests` (flat, no namespace, `Sentence_case_with_underscores`), run on the Mac. Plugin `net472` + WPF/WinForms, single DLL (CI-compile + on-box).

## Global Constraints

_Copied from SPEC.md; every task implicitly includes these._

- **Single-assembly packaging** — Core `<Compile Include>`d into the Plugin. No second DLL; no non-GAC field types unless compiled in.
- **No `async` in the Plugin.**
- **JSON is DCJS** — enum/bool knob defaults must be the **0-value**; nullable numerics/enums mean "unset, use default", never zero. **Carve-out (this slice):** `PanelSettings.EscalationStyle` becomes a **nullable enum** — a *missing* value materializes as **`null`**, not `0`, and `null` = "resolve the source-keyed default". `None` takes a **new** numeric value (`2`) — never renumber `CenterRadial (0)`/`HighlightInPlace (1)`, existing files persist those explicitly.
- **Never rewrite a valid value in `Normalize`** — the engine reads knobs per tick on other threads; assign only when out of range / on the one-time migration.
- **Core is ACT-free and Mac-testable**; Plugin code is transcribe-only (CI-compile + on-box), not TDD.
- **Escalation is driven by each timer's `WarningValue`** via `TimerMath.EffectiveWarning` — `WarningValue ≤ 0` (or `≥ total`) falls back to **last-25%-of-duration** (floor 1). A buff's warning base is 0, so under an escalating style it escalates at that fallback unless overridden.

---

## File Structure

**Core (new):**
- `src/eq2auras.Core/Timers/EscalationDefaults.cs` — `Resolve(PanelSettings) → EscalationStyle` (the source-keyed nullable default).

**Core (modified):**
- `src/eq2auras.Core/Config/Settings.cs` — `EscalationStyle` enum `+ None = 2`; new `BuffEscalationReset` marker bool; `Normalize` (one-time buff-escalation reset + warn/remove clamp); `EffectiveWarning`/`EffectiveRemove`; the flat-mirror `?? CenterRadial`.
- `src/eq2auras.Core/Config/PanelSettings.cs` — `EscalationStyle` → `EscalationStyle?` (nullable, no initializer).
- `src/eq2auras.Core/Config/BuffPref.cs` — `+ warnOverride?`, `+ removeOverride?`.
- `src/eq2auras.Core/Timers/EscalationTracker.cs` — resolve the nullable style; honor `None` (calm live-only list, no center, gone at zero).

**Plugin (modified):**
- `src/eq2auras.Plugin/Act/BuffInjector.cs` — build `TimerData` from effective duration/warn/remove + `Panel1Display = Panel2Display = false`.
- `src/eq2auras.Plugin/Eq2AurasPlugin.cs` — the buff-tracker section: escalation-style dropdown (buff group) + per-buff warning field.

**Test:** additions to `SettingsTests.cs`, `EscalationTrackerTests.cs`; new `EscalationDefaultsTests.cs`.

---

## Phase 1 — Core (strict TDD; `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`)

### Task 1: `EscalationStyle.None` + nullable `PanelSettings.EscalationStyle` + source-keyed resolver

**Files:**
- Modify: `src/eq2auras.Core/Config/Settings.cs` (enum at `:16`)
- Modify: `src/eq2auras.Core/Config/PanelSettings.cs` (the `escalationStyle` member)
- Create: `src/eq2auras.Core/Timers/EscalationDefaults.cs`
- Test: `tests/eq2auras.Core.Tests/EscalationDefaultsTests.cs`, additions to `SettingsTests.cs`

**Interfaces:**
- Produces: `enum EscalationStyle { CenterRadial=0, HighlightInPlace=1, None=2 }`; `PanelSettings.EscalationStyle` is `EscalationStyle?` (null = unset); `static EscalationStyle EscalationDefaults.Resolve(PanelSettings panel)` — `panel.EscalationStyle ?? (buff-category source ? None : CenterRadial)`.

- [ ] **Step 1: Write the failing tests.**

`tests/eq2auras.Core.Tests/EscalationDefaultsTests.cs`:
```csharp
using System.Collections.Generic;
using Eq2Auras.Core.Config;
using Eq2Auras.Core.Timers;
using Xunit;

public class EscalationDefaultsTests
{
    private static PanelSettings Panel(EscalationStyle? style, params SourceRule[] sources)
        => new PanelSettings { EscalationStyle = style, Sources = new List<SourceRule>(sources) };

    [Fact]
    public void None_is_a_new_value_not_a_renumber()
    {
        Assert.Equal(0, (int)EscalationStyle.CenterRadial);
        Assert.Equal(1, (int)EscalationStyle.HighlightInPlace);
        Assert.Equal(2, (int)EscalationStyle.None);
    }

    [Fact]
    public void Null_on_the_buff_category_group_resolves_to_None()
        => Assert.Equal(EscalationStyle.None,
            EscalationDefaults.Resolve(Panel(null, SourceRule.OfCategory("eq2auras Buffs"))));

    [Fact]
    public void Null_on_a_panel_group_resolves_to_CenterRadial()
        => Assert.Equal(EscalationStyle.CenterRadial,
            EscalationDefaults.Resolve(Panel(null, SourceRule.Panel(1))));

    [Fact]
    public void An_explicit_value_is_used_verbatim_even_on_the_buff_group()
        => Assert.Equal(EscalationStyle.CenterRadial,
            EscalationDefaults.Resolve(Panel(EscalationStyle.CenterRadial, SourceRule.OfCategory("eq2auras Buffs"))));
}
```

Additions to `SettingsTests.cs` (nullable serializer round-trip — the DCJS carve-out):
```csharp
    [Fact]
    public void A_missing_escalation_style_deserializes_to_null_not_zero()
    {
        // The nullable-enum carve-out: absent -> null (resolve default), NOT 0/CenterRadial.
        var s = Settings.Parse("{\"panels\":[{},{},{}]}");
        Assert.Null(s.Panels[2].EscalationStyle);   // buff group unset -> null
    }

    [Fact]
    public void An_explicit_escalation_style_round_trips_numerically()
    {
        var s = new Settings();
        s.Panels[0].EscalationStyle = EscalationStyle.None;
        var parsed = Settings.Parse(s.ToJson());
        Assert.Equal(EscalationStyle.None, parsed.Panels[0].EscalationStyle);
        Assert.Contains("\"escalationStyle\":2", parsed.ToJson());
    }
```

- [ ] **Step 2: Run to verify failure.**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter "EscalationDefaultsTests|SettingsTests"`
Expected: FAIL — `EscalationStyle.None` / `EscalationDefaults` missing; `EscalationStyle` not nullable.

- [ ] **Step 3: Implement — enum + nullable field.** In `Settings.cs` change the enum (`:16`):
```csharp
    public enum EscalationStyle { CenterRadial = 0, HighlightInPlace = 1, None = 2 }
```
In `PanelSettings.cs`, make the member nullable (drop the initializer so DCJS-missing → null):
```csharp
        [DataMember(Name = "escalationStyle")]
        public EscalationStyle? EscalationStyle { get; set; }   // null = unset -> EscalationDefaults.Resolve
```

- [ ] **Step 4: Implement — the resolver.**

`src/eq2auras.Core/Timers/EscalationDefaults.cs`:
```csharp
using System.Linq;
using Eq2Auras.Core.Config;

namespace Eq2Auras.Core.Timers
{
    /// The source-keyed default for a group's nullable EscalationStyle (SPEC §Configuration):
    /// null resolves to None for the buff-category group (a duration tracker, not a cooldown),
    /// CenterRadial for every other group. An explicit value is always used as-is.
    public static class EscalationDefaults
    {
        public static EscalationStyle Resolve(PanelSettings panel)
        {
            if (panel.EscalationStyle.HasValue) return panel.EscalationStyle.Value;
            bool isBuffGroup = panel.Sources != null
                && panel.Sources.Any(r => r != null
                    && r.Type == SourceRuleType.Category
                    && string.Equals(r.Value, BuffCatalog.Category, System.StringComparison.OrdinalIgnoreCase));
            return isBuffGroup ? EscalationStyle.None : EscalationStyle.CenterRadial;
        }
    }
}
```

- [ ] **Step 5: Fix compile fallout — every read of `PanelSettings.EscalationStyle` now sees a nullable.** Search: `grep -rn "\.EscalationStyle" src/eq2auras.Core src/eq2auras.Plugin`. Each Core read routes through `EscalationDefaults.Resolve(panel)` (Task 2 does `EscalationTracker`); the `Settings.cs` legacy mirror/seed are handled in Task 3. The tab's escalation combo (Task 6) reads it too. For this task only the two edited files + `EscalationDefaults` must compile — run the Core build:

Run: `dotnet build src/eq2auras.Core/eq2auras.Core.csproj`
Expected: errors ONLY where a consumer dereferences the now-nullable enum (fixed in Tasks 2–3). If the build is red solely on `EscalationTracker.cs`/`Settings.cs`, proceed — those are the next tasks. (If any *other* Core file reads it, route that read through `EscalationDefaults.Resolve` now.)

- [ ] **Step 6: Run the two new test classes** (they don't depend on Tasks 2–3):

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter "EscalationDefaultsTests"`
Expected: PASS. (The `SettingsTests` additions compile once Task 3's `Normalize` is consistent — run them at the end of Task 3.)

- [ ] **Step 7: Commit.**
```bash
git add src/eq2auras.Core/Config/Settings.cs src/eq2auras.Core/Config/PanelSettings.cs src/eq2auras.Core/Timers/EscalationDefaults.cs tests/eq2auras.Core.Tests/EscalationDefaultsTests.cs tests/eq2auras.Core.Tests/SettingsTests.cs
git commit -m "feat(timers): EscalationStyle.None + nullable per-group style + source-keyed resolver"
```

---

### Task 2: `EscalationTracker` honors `None` (calm live-only list)

**Files:**
- Modify: `src/eq2auras.Core/Timers/EscalationTracker.cs` (the `Tick` body, `:44-94`)
- Test: additions to `tests/eq2auras.Core.Tests/EscalationTrackerTests.cs`

**Interfaces:**
- Consumes: `EscalationDefaults.Resolve` (Task 1).
- Produces: unchanged `Tick` signature; `None` groups return every **live** timer as a calm `ListRows` entry with **empty `CenterElements`** and nothing past zero.

- [ ] **Step 1: Write the failing tests.**

`tests/eq2auras.Core.Tests/EscalationTrackerTests.cs` (add; the file's `Reading(...)`/`R(...)` helpers already exist):
```csharp
    private static EscalationTracker NoneTracker()
        => new EscalationTracker(new PanelSettings { EscalationStyle = EscalationStyle.None }, new PaletteAssigner());

    [Fact]
    public void None_keeps_imminent_timers_as_calm_rows_with_no_center()
    {
        var frame = NoneTracker().Tick(R(Reading("boss", 3), Reading("calm", 25)));   // 3s would be Imminent
        Assert.Equal(new[] { "boss", "calm" }, frame.ListRows.Select(r => r.Name).ToArray());
        Assert.Empty(frame.CenterElements);
    }

    [Fact]
    public void None_drops_a_timer_at_zero_even_when_linger_configured()
    {
        // RemoveValue -15 would linger under CenterRadial; None shows nothing past zero.
        var frame = NoneTracker().Tick(R(Reading("gone", -2, removeValue: -15)));
        Assert.Empty(frame.ListRows);
        Assert.Empty(frame.CenterElements);
    }
```

- [ ] **Step 2: Run to verify failure.**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter EscalationTrackerTests`
Expected: FAIL — `EscalationStyle.None` currently falls through the `CenterRadial` path (the imminent "boss" would become a pie).

- [ ] **Step 3: Implement.** In `EscalationTracker.Tick`, replace the `inPlace` line (`:45`) and short-circuit `None`. Current `:45`:
```csharp
            bool inPlace = _settings.EscalationStyle == EscalationStyle.HighlightInPlace;
```
becomes:
```csharp
            var style = EscalationDefaults.Resolve(_settings);
            if (style == EscalationStyle.None)
                return new OverlayFrame
                {
                    ListRows = TimerListBuilder.Build(live, includeOverdue: false),   // live-only, gone at zero
                    CenterElements = new List<CenterElement>()
                };
            bool inPlace = style == EscalationStyle.HighlightInPlace;
```
(`live` is already computed just above at `:44` — `governing.Where(r => r.TimeLeft > 0)`. The `None` return uses it directly; the rest of the method — lates/centered/pies — is untouched and unreached under `None`.) Add `using Eq2Auras.Core.Config;` if the `EscalationStyle` reference needs it (already present via `_settings`).

- [ ] **Step 4: Run — full suite (the routing/escalation change must not regress).**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
Expected: PASS (all). Existing `CenterRadial`/`HighlightInPlace` tests still green — `Resolve` returns the explicit value for those panels.

- [ ] **Step 5: Commit.**
```bash
git add src/eq2auras.Core/Timers/EscalationTracker.cs tests/eq2auras.Core.Tests/EscalationTrackerTests.cs
git commit -m "feat(timers): EscalationTracker honors None — calm live-only list, no center, gone at zero"
```

---

### Task 3: `Settings` — one-time buff-escalation migration + nullable mirror

**Files:**
- Modify: `src/eq2auras.Core/Config/Settings.cs` (marker field; `Normalize`; the flat mirror `:184-185` and legacy seed `:110-114`)
- Test: additions to `tests/eq2auras.Core.Tests/SettingsTests.cs`

**Interfaces:**
- Produces: `Settings.BuffEscalationReset` (`bool`, DCJS 0-value `false`); `Normalize` nulls the buff group's `EscalationStyle` exactly once (marker gate); the flat mirror writes `Panels[0].EscalationStyle ?? CenterRadial`.

- [ ] **Step 1: Write the failing tests.**
```csharp
    [Fact]
    public void A_pre_amendment_buff_window_escalation_resets_to_null_once()
    {
        // Old file: buff group carries the escalating default (escalationStyle:0), marker absent.
        var json = "{\"panels\":[{},{},{\"escalationStyle\":0}]}";
        var s = Settings.Parse(json);
        Assert.Null(s.Panels[2].EscalationStyle);   // migrated to null -> resolves to None
        Assert.True(s.BuffEscalationReset);         // marker set
    }

    [Fact]
    public void A_migrated_files_explicit_buff_escalation_pick_survives()
    {
        // Post-migration: marker true, raider explicitly picked CenterRadial for the buff window.
        var json = "{\"buffEscalationReset\":true,\"panels\":[{},{},{\"escalationStyle\":0}]}";
        var s = Settings.Parse(json);
        Assert.Equal(EscalationStyle.CenterRadial, s.Panels[2].EscalationStyle);   // NOT reset
        Assert.True(s.BuffEscalationReset);
    }

    [Fact]
    public void The_flat_escalation_mirror_resolves_a_null_panel_A_to_center_radial()
    {
        // Panel A unset (null) must still mirror a concrete value into the legacy flat knob.
        var s = new Settings();
        s.Panels[0].EscalationStyle = null;
        Assert.Contains("\"escalationStyle\":0", s.ToJson());   // flat top-level = CenterRadial
    }
```

- [ ] **Step 2: Run to verify failure.**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter SettingsTests`
Expected: FAIL — `BuffEscalationReset` missing; migration absent; mirror still dereferences a non-nullable.

- [ ] **Step 3: Implement — marker field.** Add near `BetaChannel` (`Settings.cs:35`):
```csharp
    [DataMember(Name = "buffEscalationReset")]
    public bool BuffEscalationReset { get; set; }   // one-shot: false in pre-amendment files -> migrate once
```

- [ ] **Step 4: Implement — the one-time reset in `Normalize`.** After the `BuffPrefs` backfill block, before the per-panel clamps, add:
```csharp
        // One-time: a pre-amendment buff window carries the escalating default (escalationStyle:0),
        // indistinguishable by value from a later explicit CenterRadial pick — so the MARKER, not the
        // value, decides. First load (marker false): null the buff group's escalation (-> resolves to
        // None) and set the marker; thereafter leave it, so a user's later pick persists (SPEC §Configuration).
        if (!BuffEscalationReset)
        {
            var buffGroup = Panels.FirstOrDefault(p => p.Sources != null
                && p.Sources.Any(r => r != null && r.Type == SourceRuleType.Category
                    && string.Equals(r.Value, BuffCatalog.Category, StringComparison.OrdinalIgnoreCase)));
            if (buffGroup != null) buffGroup.EscalationStyle = null;
            BuffEscalationReset = true;
        }
```

- [ ] **Step 5: Implement — nullable-safe legacy mirror + seed.** The save mirror (`Settings.cs:184-185`) `EscalationStyle = Panels[0].EscalationStyle` → resolve the null:
```csharp
            EscalationStyle = Panels[0].EscalationStyle ?? EscalationStyle.CenterRadial;
```
The legacy seed (`Settings.cs:110-114`) `Panels[0].EscalationStyle = EscalationStyle;` assigns the non-nullable top-level into the nullable member — an **explicit** Panel-A choice; it compiles unchanged (implicit `EscalationStyle` → `EscalationStyle?`). Leave it.

- [ ] **Step 6: Run — the Settings additions + the Task-1 SettingsTests + full suite.**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
Expected: PASS (all, including Task 1's `A_missing_escalation_style_deserializes_to_null_not_zero` — now consistent).

- [ ] **Step 7: Commit.**
```bash
git add src/eq2auras.Core/Config/Settings.cs tests/eq2auras.Core.Tests/SettingsTests.cs
git commit -m "feat(config): one-time buff-escalation reset (marker-gated) + nullable-safe flat mirror"
```

---

### Task 4: `BuffPref` warn/remove overrides + effective resolution + clamp

**Files:**
- Modify: `src/eq2auras.Core/Config/BuffPref.cs` (+2 fields)
- Modify: `src/eq2auras.Core/Config/Settings.cs` (`EffectiveWarning`/`EffectiveRemove`; clamp in `Normalize`)
- Test: additions to `tests/eq2auras.Core.Tests/SettingsTests.cs`

**Interfaces:**
- Produces: `BuffPref.WarnOverride` (`int?`), `BuffPref.RemoveOverride` (`int?`); `Settings.EffectiveWarning(id)` (`WarnOverride ?? 0`), `Settings.EffectiveRemove(id)` (`RemoveOverride ?? 0`); out-of-range warn/remove overrides clamped in `Normalize` (like `DurationOverride`).

- [ ] **Step 1: Write the failing tests.**
```csharp
    [Fact]
    public void Effective_warning_and_remove_default_to_zero_and_honor_overrides()
    {
        var s = Settings.Parse("{\"buffPrefs\":[{\"id\":\"bolster\",\"enabled\":true}," +
                               "{\"id\":\"tsunami\",\"enabled\":true,\"warnOverride\":5,\"removeOverride\":-3}]}");
        Assert.Equal(0, s.EffectiveWarning("bolster"));
        Assert.Equal(0, s.EffectiveRemove("bolster"));
        Assert.Equal(5, s.EffectiveWarning("tsunami"));
        Assert.Equal(-3, s.EffectiveRemove("tsunami"));
    }

    [Fact]
    public void Out_of_range_warn_and_remove_overrides_revert_to_base_zero()
    {
        // A hand-edited absurd value must not survive into the tab's NumericUpDown (InitPlugin crash).
        var s = Settings.Parse("{\"buffPrefs\":[{\"id\":\"bolster\",\"enabled\":true,\"warnOverride\":99999,\"removeOverride\":-99999}]}");
        Assert.Equal(0, s.EffectiveWarning("bolster"));
        Assert.Equal(0, s.EffectiveRemove("bolster"));
    }
```

- [ ] **Step 2: Run to verify failure.**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter SettingsTests`
Expected: FAIL — `WarnOverride`/`EffectiveWarning` missing.

- [ ] **Step 3: Implement — `BuffPref` fields.** In `BuffPref.cs`, after `DurationOverride`:
```csharp
        [DataMember(Name = "warnOverride")]
        public int? WarnOverride { get; set; }

        [DataMember(Name = "removeOverride")]
        public int? RemoveOverride { get; set; }
```

- [ ] **Step 4: Implement — effective resolvers.** In `Settings.cs`, beside `EffectiveDuration`:
```csharp
        public int EffectiveWarning(string id)
            => (BuffPrefs ?? new List<BuffPref>()).FirstOrDefault(p => p != null && p.Id == id)?.WarnOverride ?? 0;

        public int EffectiveRemove(string id)
            => (BuffPrefs ?? new List<BuffPref>()).FirstOrDefault(p => p != null && p.Id == id)?.RemoveOverride ?? 0;
```

- [ ] **Step 5: Implement — clamp in `Normalize`.** Where the `DurationOverride` clamp lives (the `foreach (var pref in BuffPrefs)` guarding out-of-range overrides), extend it. Warning is `[0, 3600]`, remove is `[-3600, 0]` (a linger is negative; 0 = remove-at-zero; positive is nonsensical):
```csharp
                if (pref.WarnOverride.HasValue && (pref.WarnOverride.Value < 0 || pref.WarnOverride.Value > 3600))
                    pref.WarnOverride = null;
                if (pref.RemoveOverride.HasValue && (pref.RemoveOverride.Value < -3600 || pref.RemoveOverride.Value > 0))
                    pref.RemoveOverride = null;
```

- [ ] **Step 6: Run + commit.**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
Expected: PASS (all).
```bash
git add src/eq2auras.Core/Config/BuffPref.cs src/eq2auras.Core/Config/Settings.cs tests/eq2auras.Core.Tests/SettingsTests.cs
git commit -m "feat(config): general timer-shape overrides — warn/remove on BuffPref + effective + clamp"
```

---

## Phase 2 — Plugin (transcribe-only; CI-compile + on-box). No async; single DLL.

### Task 5: Inject `TimerData` from effective shape + panel flags off

**Files:**
- Modify: `src/eq2auras.Plugin/Act/BuffInjector.cs` (the `AddEditTimerDef` call in `SyncTo`)

- [ ] **Step 1: Build the def from effective shape (transcribe-only).** In `SyncTo`, the current construction hardcodes warning `0`/`5` and lets `RemoveValue` default (−15) with panel flags true. Replace it so warn/remove come from `settings` and panel routing is off (the `TimerData` ctor's 8th arg is `WarningValue`; `RemoveValue`, `Panel1Display`, `Panel2Display` are object-initializer properties):
```csharp
            foreach (var def in desired)
            {
                ActGlobals.oFormSpellTimers.AddEditTimerDef(new TimerData(
                        def.DisplayName, false, settings.EffectiveDuration(def.Id), false, false, "", "",
                        settings.EffectiveWarning(def.Id), true)
                    {
                        Category = Category,
                        RemoveValue = settings.EffectiveRemove(def.Id),
                        Panel1Display = false,
                        Panel2Display = false,
                    });
                ActGlobals.oFormActMain.ActiveCustomTriggers[DictKey(def)] = BuildTrigger(def);
            }
```
(Consumes `Settings.EffectiveWarning`/`EffectiveRemove` from Task 4.) **Transcribe note:** `RemoveValue`/`Panel1Display`/`Panel2Display` are the decompile-observed `TimerData` members; if the on-box build names them differently, the merge-gate compile surfaces it — adjust and re-verify. The **effect** of `Panel1Display = false` surviving `AddEditTimerDef` is a field gate (§Testing) — confirm on-box that a buff appears only in the Buffs window, never Panel A.

- [ ] **Step 2: Branch CI compile gate.**
```bash
git add src/eq2auras.Plugin/Act/BuffInjector.cs
git commit -m "fix(plugin): inject buff TimerData from effective duration/warn/remove + panel flags off"
git push   # branch push = verify-only CI (Core tests + WPF compile + artifact)
```
Expected: CI green.

---

### Task 6: Buff-tracker config section — escalation dropdown + per-buff warning field

**Files:**
- Modify: `src/eq2auras.Plugin/Eq2AurasPlugin.cs` (`BuildBuffBox`)

- [ ] **Step 1: Add the escalation-style dropdown for the buff window (transcribe-only).** At the top of `BuildBuffBox` (before the per-buff rows), add a labeled `ComboBox` over `{ CenterRadial, HighlightInPlace, None }` bound to the **buff group** (`_settings.Panels[2]`). Its selected item reflects `EscalationDefaults.Resolve(_settings.Panels[2])` (so a `null`/None shows "None"); on change, write the explicit enum and re-render:
```csharp
            var escLabel = new Label { Text = "Escalation:", Left = 10, Top = 20, Width = 70 };
            var escBox = new ComboBox { Left = 84, Top = 18, Width = 130, DropDownStyle = ComboBoxStyle.DropDownList };
            escBox.Items.AddRange(new object[] { "None", "Center radial", "Highlight in place" });
            var buffPanel = _settings.Panels[2];
            escBox.SelectedIndex = EscalationDefaults.Resolve(buffPanel) == EscalationStyle.None ? 0
                : EscalationDefaults.Resolve(buffPanel) == EscalationStyle.CenterRadial ? 1 : 2;
            escBox.SelectedIndexChanged += (s, e) =>
            {
                var chosen = escBox.SelectedIndex == 0 ? EscalationStyle.None
                    : escBox.SelectedIndex == 1 ? EscalationStyle.CenterRadial : EscalationStyle.HighlightInPlace;
                SettingsStore.Update(_settings, () => buffPanel.EscalationStyle = chosen);
                _overlay.Restyle();   // rebuild the buff window's visuals with the new style
            };
            box.Controls.Add(escLabel);
            box.Controls.Add(escBox);
```
Shift the per-buff rows down (start `y` after this control — e.g. hint label at `Top = 46`, rows begin `y = 82`), and grow the box `Height` accordingly. (`_overlay.Restyle()` is the existing per-tab-change rebuild path — grep `OverlayHost` for the method that re-applies knobs; if it is named differently, use that.)

- [ ] **Step 2: Add a per-buff warning field beside duration (transcribe-only).** In the per-buff row loop, add a second `NumericUpDown` for the warning override (0..3600, seeded with `_settings.EffectiveWarning(d.Id)`; = 0 clears the override), mirroring the duration field's persistence + `SyncTo`:
```csharp
                var warn = new NumericUpDown { Left = 306, Top = y - 2, Width = 54, Minimum = 0, Maximum = 3600, Value = _settings.EffectiveWarning(d.Id) };
                warn.ValueChanged += (s, e) =>
                {
                    int v = (int)warn.Value;
                    SettingsStore.Update(_settings, () => BuffPrefFor(d.Id).WarnOverride = v == 0 ? (int?)null : v);
                    _buffInjector?.SyncTo(_settings);
                };
                box.Controls.Add(warn);
```
Widen the group box + shift columns so duration (`Left≈236`) and warning (`Left≈306`) both fit with small `s`/`warn` labels; keep the box within the tab's `AutoScroll` region. **The wheel-suppression (`SuppressSpinnerWheel(tab)`, already called at the end of `BuildConfigTab`) covers these new spinners** — it walks the control tree, so no extra wiring.

- [ ] **Step 3: Branch CI compile gate.**
```bash
git add src/eq2auras.Plugin/Eq2AurasPlugin.cs
git commit -m "feat(plugin): buff-tracker config section — escalation dropdown + per-buff warning field"
git push
```
Expected: CI green.

---

## Merge-gate live script (Alex, Windows box)

1. **Update + reload** → version bumps, no crash. **Existing install migration:** the buff window is now a **plain draining list** (no flying-to-center) without touching any setting — the one-time reset fired.
2. **No Panel A leak:** cast a buff → it appears **only** in the Buffs window, never Panel A (the `Panel1Display=false` field gate).
3. **Escalation config:** in the buff-tracker section, flip the buff window to **Center radial** → buffs now escalate (at the 25% fallback for un-warned buffs); set a per-buff **warning** (e.g. 10s on one buff) → that buff escalates at 10s; flip back to **None** → calm list again.
4. **Warning override persists** across reload; an absurd hand-edited `warnOverride`/`removeOverride` in `settings.json` doesn't crash load (clamped to base).
5. **Regression:** Panels A/B escalate exactly as before (their `EscalationStyle` unchanged / resolves to CenterRadial).

---

## Self-review

**Spec coverage:** `None` mode (Tasks 1–2) ✓; nullable + source-keyed default (Task 1) ✓; one-time migration marker (Task 3) ✓; nullable-safe flat mirror (Task 3) ✓; general timer-shape overrides duration/warn/remove + clamp (Task 4) ✓; injection effective-shape + panel flags off (Task 5) ✓; buff-tracker UI section escalation + warning (Task 6) ✓; the three plan-watch items (new-enum-value + nullable serializer test — Task 1; marker exactly-once test — Task 3; warn/remove clamp — Task 4) all covered.

**Placeholder scan:** none — every step carries real code. The only field-confirmed item is the `Panel1Display=false`-survives-`AddEditTimerDef` effect (§Testing field gate, merge-gate step 2).

**Type consistency:** `EscalationStyle.None`, `PanelSettings.EscalationStyle` (`EscalationStyle?`), `EscalationDefaults.Resolve`, `Settings.BuffEscalationReset`/`EffectiveWarning`/`EffectiveRemove`, `BuffPref.WarnOverride`/`RemoveOverride`, `BuffInjector` effective-shape construction, `Eq2AurasPlugin.BuildBuffBox` additions — used consistently across tasks.
