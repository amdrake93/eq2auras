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
- `src/eq2auras.Core/Config/Settings.cs` — `EscalationStyle` enum `+ None = 2`; new `BuffEscalationReset` marker bool; the flat-mirror `?? CenterRadial` (compile-blocker, lands atomically in Task 1); `Normalize` (one-time buff-escalation reset + warn/remove clamp); `EffectiveWarning`/`EffectiveRemove`.
- `src/eq2auras.Core/Config/PanelSettings.cs` — `EscalationStyle` → `EscalationStyle?` (nullable, no initializer, **`EmitDefaultValue = false`** so an unset style omits the key — an old non-nullable build must never read `"escalationStyle":null`).
- `src/eq2auras.Core/Config/BuffPref.cs` — `+ warnOverride?`, `+ removeOverride?`.
- `src/eq2auras.Core/Timers/EscalationTracker.cs` — resolve the nullable style; honor `None` (calm live-only list, no center, gone at zero).

**Plugin (modified):**
- `src/eq2auras.Plugin/Act/BuffInjector.cs` — build `TimerData` from effective duration/warn/remove + `Panel1Display = Panel2Display = false`.
- `src/eq2auras.Plugin/Eq2AurasPlugin.cs` — the buff-tracker section (escalation dropdown + per-buff warning field) **and** the existing Panel A/B escalation combos at `BuildPanelGroupBox` (`:267-275`): make the null read crash-safe via `EscalationDefaults.Resolve` and add the `None` third item (per owner decision — `None` is a general per-group value).

**Test:** additions to `SettingsTests.cs`, `EscalationTrackerTests.cs`; new `EscalationDefaultsTests.cs`.

---

## Phase 1 — Core (strict TDD; `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`)

### Task 1: `EscalationStyle.None` + nullable `PanelSettings.EscalationStyle` + source-keyed resolver

> **This task is atomic on purpose.** Making `PanelSettings.EscalationStyle` nullable turns the flat mirror at `Settings.cs:185` into a hard `CS0266` (nullable → non-nullable), and the test project `ProjectReference`s Core — so the *whole tree stops compiling and no test runs* until the mirror is fixed. Therefore the enum change, the nullable field, the resolver, the mirror fix, **and** the one existing test the behavior change invalidates all land together. Splitting them would leave a task boundary on a non-compiling tree. (`EscalationTracker.cs:45`'s `_settings.EscalationStyle == HighlightInPlace` is a **lifted** `==` — it compiles unchanged; a null reads as "not HighlightInPlace" → the CenterRadial path, same as today. Task 2 replaces it with the real `None`-aware resolve. No existing test exercises a null style, so the suite stays green through this interim.)

**Files:**
- Modify: `src/eq2auras.Core/Config/Settings.cs` (enum at `:16`; the flat mirror at `:185`)
- Modify: `src/eq2auras.Core/Config/PanelSettings.cs` (the `escalationStyle` member)
- Create: `src/eq2auras.Core/Timers/EscalationDefaults.cs`
- Test: `tests/eq2auras.Core.Tests/EscalationDefaultsTests.cs`; additions to + one edit in `SettingsTests.cs`

**Interfaces:**
- Produces: `enum EscalationStyle { CenterRadial=0, HighlightInPlace=1, None=2 }`; `PanelSettings.EscalationStyle` is `EscalationStyle?` with `EmitDefaultValue = false` (null = unset, omitted from JSON); `static EscalationStyle EscalationDefaults.Resolve(PanelSettings panel)` — `panel.EscalationStyle ?? (buff-category source ? None : CenterRadial)`.

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

Additions to `SettingsTests.cs` (the DCJS nullable-enum carve-out + the old-build-compat guarantee):
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

    [Fact]
    public void An_unset_escalation_style_is_omitted_from_json_for_old_build_compat()
    {
        // EmitDefaultValue=false: a null (unset) style must NOT serialize as "escalationStyle":null,
        // else an older stable build's non-nullable field throws on read -> Settings.Parse catch -> full
        // reset (SPEC §Configuration old-build compat). The non-nullable flat top-level knob still emits.
        var s = new Settings();
        s.Panels[0].EscalationStyle = null;
        Assert.DoesNotContain("\"escalationStyle\":null", s.ToJson());
    }

    [Fact]
    public void The_flat_escalation_mirror_resolves_a_null_panel_A_to_center_radial()
    {
        // The non-nullable flat top-level knob (old-build compat anchor) must carry a concrete
        // value even when Panel A's per-group style is unset.
        var s = new Settings();
        s.Panels[0].EscalationStyle = null;
        Assert.Contains("\"escalationStyle\":0", s.ToJson());   // flat top-level = CenterRadial
    }
```

- [ ] **Step 2: Edit the one existing test the behavior change invalidates.** `Legacy_flat_file_seeds_panel_A_and_defaults_panel_B` (`SettingsTests.cs:146`) asserts the *stored* `Panels[1].EscalationStyle == CenterRadial`; a padded Panel B is now **null** (it *resolves* to CenterRadial, it no longer stores it). Change that one assertion to read the resolved value — the intent (Panel B behaves as CenterRadial) is preserved:
```csharp
        Assert.Equal(EscalationStyle.CenterRadial, EscalationDefaults.Resolve(parsed.Panels[1]));
```
(Panel A's assertion at `:144` is unchanged — the legacy seed sets it to an explicit, non-null value.)

- [ ] **Step 3: Run to verify failure.**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter "EscalationDefaultsTests|SettingsTests"`
Expected: FAIL — `EscalationStyle.None` / `EscalationDefaults` missing; `EscalationStyle` not nullable.

- [ ] **Step 4: Implement — enum + nullable field.** In `Settings.cs` change the enum (`:16`):
```csharp
    public enum EscalationStyle { CenterRadial = 0, HighlightInPlace = 1, None = 2 }
```
In `PanelSettings.cs`, make the member nullable and omit-when-unset (drop the initializer so DCJS-missing → null; `EmitDefaultValue = false` so a null never serializes as `"escalationStyle":null`):
```csharp
        [DataMember(Name = "escalationStyle", EmitDefaultValue = false)]
        public EscalationStyle? EscalationStyle { get; set; }   // null = unset -> EscalationDefaults.Resolve
```

- [ ] **Step 5: Implement — the resolver.**

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

- [ ] **Step 6: Fix the compile-blocking flat mirror.** The save mirror at `Settings.cs:185` — `EscalationStyle = Panels[0].EscalationStyle;` — is now `CS0266` (the top-level flat knob stays **non-nullable** for old-build compat). Resolve the null:
```csharp
            EscalationStyle = Panels[0].EscalationStyle ?? EscalationStyle.CenterRadial;
```
The legacy seed (`Settings.cs:113`) `Panels[0].EscalationStyle = EscalationStyle;` assigns the non-nullable top-level into the nullable member — an **explicit** Panel-A choice; it compiles unchanged (implicit `EscalationStyle` → `EscalationStyle?`). Leave it.

- [ ] **Step 7: Build + run the full suite.**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
Expected: PASS (**all** — the tree compiles end-to-end; the edited legacy test and every new test are green; no existing test exercises a null style, so `EscalationTracker.cs:45`'s interim lifted-`==` behavior regresses nothing).

- [ ] **Step 8: Commit.**
```bash
git add src/eq2auras.Core/Config/Settings.cs src/eq2auras.Core/Config/PanelSettings.cs src/eq2auras.Core/Timers/EscalationDefaults.cs tests/eq2auras.Core.Tests/EscalationDefaultsTests.cs tests/eq2auras.Core.Tests/SettingsTests.cs
git commit -m "feat(timers): EscalationStyle.None + nullable per-group style (omit-when-unset) + source-keyed resolver"
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

### Task 3: `Settings` — one-time buff-escalation migration marker

**Files:**
- Modify: `src/eq2auras.Core/Config/Settings.cs` (marker field near `:35`; `Normalize`)
- Test: additions to `tests/eq2auras.Core.Tests/SettingsTests.cs`

**Interfaces:**
- Consumes: the nullable `PanelSettings.EscalationStyle` (Task 1).
- Produces: `Settings.BuffEscalationReset` (`bool`, DCJS 0-value `false`); `Normalize` nulls the buff group's `EscalationStyle` **exactly once** (marker gate), so an old install's escalating buff window becomes calm without ever clobbering a later explicit pick.

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
```

- [ ] **Step 2: Run to verify failure.**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter SettingsTests`
Expected: FAIL — `BuffEscalationReset` missing; migration absent.

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

- [ ] **Step 5: Run — full suite.**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
Expected: PASS (all — the mirror/existing-test fallout was already resolved atomically in Task 1).

- [ ] **Step 6: Commit.**
```bash
git add src/eq2auras.Core/Config/Settings.cs tests/eq2auras.Core.Tests/SettingsTests.cs
git commit -m "feat(config): one-time marker-gated buff-escalation reset"
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

- [ ] **Step 1: Build the def from effective shape (transcribe-only).** In `SyncTo` the current `TimerData` construction (`BuffInjector.cs:36`) hardcodes only the warning arg (`5`) and sets just `Category`, leaving `RemoveValue` at its −15 default and the panel flags at their `TimerData` defaults (`Panel1Display` true, `Panel2Display` false — per `act-timer-engine.md:39`), which is the Panel-A leak. Replace it so warn/remove come from `settings` and both panel-routing flags are off (the `TimerData` ctor's 8th arg is `WarningValue`; `RemoveValue`, `Panel1Display`, `Panel2Display` are object-initializer properties):
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

### Task 6: Escalation combos (all windows) + buff-tracker warning field

**Files:**
- Modify: `src/eq2auras.Plugin/Eq2AurasPlugin.cs` (new `WireEscalationCombo` helper; `BuildPanelGroupBox` `:267-275`; `BuildBuffBox`)

**Interfaces:**
- Consumes: `EscalationDefaults.Resolve` (Task 1), `Settings.EffectiveWarning` / `BuffPref.WarnOverride` (Task 4).
- Produces (Plugin-internal): `private void WireEscalationCombo(ComboBox box, PanelSettings panel)` — the single source of truth for every escalation dropdown (3 items, null-safe read, persist).

> **Why the A/B combos are in scope:** once `PanelSettings.EscalationStyle` is nullable (Task 1), the existing `styleBox.SelectedIndex = (int)panel.EscalationStyle;` at `Eq2AurasPlugin.cs:273` throws `InvalidOperationException` on a null — and a **fresh install** hits exactly that (`SettingsStore.Load():17` returns a bare `new Settings()` with no `Normalize`, so A/B styles are null), so `InitPlugin` would crash and the plugin wouldn't load. The fix and the buff dropdown share one helper. Per owner decision, A/B also gain the `None` item (`None` is a general per-group value).

- [ ] **Step 1: Add the shared escalation-combo helper + fix the A/B combos (transcribe-only).** Add a private helper near `BuildPanelGroupBox`. Item order **must** match the enum's numeric values (`CenterRadial=0, HighlightInPlace=1, None=2`) so `(int)Resolve(...)` seeds the index and `(EscalationStyle)SelectedIndex` reads it back — no per-value branching, and null is handled by `Resolve`:
```csharp
        private void WireEscalationCombo(ComboBox box, PanelSettings panel)
        {
            box.DropDownStyle = ComboBoxStyle.DropDownList;
            box.Items.Clear();
            box.Items.AddRange(new object[] { "Center radial", "Highlight in place", "None" });
            box.SelectedIndex = (int)EscalationDefaults.Resolve(panel);
            box.SelectedIndexChanged += (s, e) =>
                SettingsStore.Update(_settings, () => panel.EscalationStyle = (EscalationStyle)box.SelectedIndex);
        }
```
Then in `BuildPanelGroupBox`, keep the existing `styleBox` creation with its bounds (`:267-271`) but replace the two-item populate + null-unsafe seed + handler (`:272-275`) with one call — no separate refresh (the tracker reads the same `PanelSettings` instance each tick, exactly as the combo did before):
```csharp
            var styleBox = new ComboBox { /* existing Left/Top/Width from :267-271 */ };
            WireEscalationCombo(styleBox, panel);
            // ... existing box.Controls.Add(styleBox); at :327
```

- [ ] **Step 2: Add the buff-window escalation dropdown (transcribe-only).** At the top of `BuildBuffBox` (before the per-buff rows), add a labeled combo bound to the **buff group** (`_settings.Panels[2]`) via the same helper — a `null`/None buff group shows "None":
```csharp
            var escLabel = new Label { Text = "Escalation:", Left = 10, Top = 20, Width = 70 };
            var escBox = new ComboBox { Left = 84, Top = 18, Width = 140 };
            WireEscalationCombo(escBox, _settings.Panels[2]);
            box.Controls.Add(escLabel);
            box.Controls.Add(escBox);
```
Shift the per-buff rows down (hint label ~`Top = 46`, rows begin ~`y = 82`) and grow the box `Height` accordingly.

- [ ] **Step 3: Add a per-buff warning field beside duration (transcribe-only).** In the per-buff row loop, add a second `NumericUpDown` for the warning override (0..3600, seeded with `_settings.EffectiveWarning(d.Id)`; = 0 clears the override), mirroring the duration field's persistence + `SyncTo`:
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

- [ ] **Step 4: Branch CI compile gate.**
```bash
git add src/eq2auras.Plugin/Eq2AurasPlugin.cs
git commit -m "feat(plugin): shared 3-item escalation combo (A/B + buffs, null-safe) + per-buff warning field"
git push
```
Expected: CI green.

---

## Merge-gate live script (Alex, Windows box)

1. **Update + reload** → version bumps, no crash. **Existing install migration:** the buff window is now a **plain draining list** (no flying-to-center) without touching any setting — the one-time reset fired.
2. **Fresh install (F2 crash gate):** move `settings.json` aside, reload → the plugin **loads without crashing** (the nullable A/B styles resolve, they don't throw in `InitPlugin`), buffs default to a calm list, Panels A/B default to Center radial.
3. **No Panel A leak:** cast a buff → it appears **only** in the Buffs window, never Panel A (the `Panel1Display=false` field gate).
4. **Escalation config (all three windows now offer None):** in the buff-tracker section, flip the buff window to **Center radial** → buffs now escalate (at the 25% fallback for un-warned buffs); set a per-buff **warning** (e.g. 10s on one buff) → that buff escalates at 10s; flip back to **None** → calm list again. On **Panel A**, confirm the dropdown now has a **None** item and selecting it makes that panel a calm list.
5. **Warning override persists** across reload; an absurd hand-edited `warnOverride`/`removeOverride` in `settings.json` doesn't crash load (clamped to base).
6. **Old-build downgrade compat (F4 + the `None=2` cousin):** with the new build installed, set Panel A to **None** and reload once (writes `escalationStyle:2`); then install a **pre-amendment** build (or flip the beta channel back to a build without `None`) and confirm it **reads the file without a full settings reset** — positions/palette/buff-prefs survive. If DCJS on net472 rejects the unknown enum value `2` here, that's the residual risk called out in planning — report it rather than shipping to the stable channel. (The `EmitDefaultValue=false` unset path — an *omitted* key — is already covered by Core tests; this step exercises only the *explicit-None-then-downgrade* path, unverifiable on the Mac.)
7. **Regression:** Panels A/B escalate exactly as before when left at their defaults (resolve to CenterRadial).

---

## Self-review

**Spec coverage:** `None` mode (Tasks 1–2) ✓; nullable + source-keyed default + old-build-compat serialization (Task 1) ✓; nullable-safe flat mirror (Task 1, atomic with the nullable change) ✓; one-time migration marker (Task 3) ✓; general timer-shape overrides duration/warn/remove + clamp (Task 4) ✓; injection effective-shape + panel flags off (Task 5) ✓; escalation combos on all windows + per-buff warning UI (Task 6) ✓; the three plan-watch items (new-enum-value + nullable serializer test — Task 1; marker exactly-once test — Task 3; warn/remove clamp — Task 4) all covered.

**Round-1 review fixes folded in:** F1 (Task 1 is now atomic — enum + nullable + `EmitDefaultValue` + resolver + mirror + the one invalidated test land together, every step compiles/runs); F2 (Task 6 fixes the fresh-install A/B null crash via the shared helper + adds the `None` item per owner decision); F3 (Task 1 Step 2 edits the `Legacy_flat_file...` assertion to read the resolved value); F4 (`EmitDefaultValue = false` + omit-when-unset test; explicit-`None`-downgrade cousin carried as merge-gate step 6); F5 (`_overlay.Restyle()` dropped — no refresh needed, matching the existing combo's live-read behavior); F6 (Task 5 narrative corrected).

**Every step compiles:** the only nullable-transition compile-blocker (the flat mirror) is fixed inside the same task that introduces the nullable field; no task boundary lands on a red tree.

**Placeholder scan:** none — every step carries real code. Field-confirmed residuals, both flagged with merge-gate checks: the `Panel1Display=false`-survives-`AddEditTimerDef` effect (merge-gate step 3) and net472 DCJS behavior on the explicit `escalationStyle:2` downgrade (merge-gate step 6).

**Type consistency:** `EscalationStyle.None`, `PanelSettings.EscalationStyle` (`EscalationStyle?`, `EmitDefaultValue=false`), `EscalationDefaults.Resolve`, `Settings.BuffEscalationReset`/`EffectiveWarning`/`EffectiveRemove`, `BuffPref.WarnOverride`/`RemoveOverride`, `WireEscalationCombo(ComboBox, PanelSettings)`, `BuffInjector` effective-shape construction — used consistently across tasks.
