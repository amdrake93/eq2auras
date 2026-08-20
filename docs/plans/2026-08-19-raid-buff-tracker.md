# Raid Buff Tracker + Timer Association Model — Implementation Plan

> **For agentic workers:** this plan is executed **inline** in the writer session via `superpowers:executing-plans` (eq2auras convention — Alex watches; not subagent-driven). Steps use checkbox (`- [ ]`) syntax for tracking. Every code task is **strict TDD in Core**; Plugin code is **transcribe-only** (CI-compile-gated + on-box live verification), matching the repo convention.

**Goal:** Track a bounded, curated set of raid buffs (duration + target) in a dedicated overlay window fed by our own timers injected into ACT, and — as the forcing function — invert timer routing from per-timer panel booleans to a general **association model** (windows select timers by `{type, value}` source rules), with no "panel C".

**Architecture:** Core owns the whole *pure*, Mac-tested model: the `SourceRule` + generic `SourceRules.Matches` predicate, `OverlayEngine` routing over it, the three seeded groups (`panel:1`, `panel:2`, `category:eq2auras Buffs`) via `Settings.Normalize`, the compiled `BuffCatalog`, and the `BuffSync` enabled-set→injection plan. The Plugin is a thin ACT adapter: `TimerProbe` gains a `Category` read; a new `BuffInjector` registers/withdraws each buff's `TimerData`+`CustomTrigger` pair transiently, sweeps stale defs on init, re-ensures triggers each poll (zone-rebuild resilience), and tears down on unload; the overlay host gains a third group (the buff window) and a title-cased target suffix. **A Windows-box injection spike (Task 0) gates the buff-specific work.**

**Tech Stack:** C# — Core `netstandard2.0`, tested with **xUnit** in `tests/eq2auras.Core.Tests` (flat, no subfolders; classes top-level, no namespace; method names `Sentence_case_with_underscores`), run on the Mac via `dotnet test`. Plugin `net472` + WPF, compiled into the single plugin DLL. ACT injection uses `ActGlobals.oFormActMain` / `ActGlobals.oFormSpellTimers` public API (decompile-verified, ACT 3.8.5.288).

## Global Constraints

_Copied from SPEC.md; every task's requirements implicitly include these._

- **Single-assembly packaging** — Core sources are `<Compile Include>`d into the Plugin. No second DLL; no non-GAC types in Plugin fields unless compiled in.
- **No `async` in the Plugin project.**
- **Never reference `System.Web.Extensions`** — JSON is `DataContractJsonSerializer` (DCJS).
- **DCJS skips field initializers on deserialize** → every enum/bool knob default must be the **0-value**; nullable numerics and null lists mean "unset, use default", never zero/empty.
- **Never `Assembly.LoadFrom`.**
- **The wall clock owns visuals; the poll updates state only.**
- **Core is ACT-free and Mac-testable** — no `Advanced_Combat_Tracker` types in Core. Plugin code is transcribe-only (CI-compile + on-box script), not TDD.
- **netstandard2.0 has no `Enumerable.ToHashSet`** — use `new HashSet<string>(seq, StringComparer.OrdinalIgnoreCase)`.
- **Reserved category** for all eq2auras-managed timers: **`eq2auras Buffs`** (`BuffCatalog.Category`) — the routing source, the management namespace, and ACT's UI segregation.

---

## File Structure

**Core (new):**
- `src/eq2auras.Core/Timers/SourceRule.cs` — `SourceRuleType` enum + `SourceRule` DCJS class.
- `src/eq2auras.Core/Timers/SourceRules.cs` — the `Matches` / `MatchesAny` predicate (the one routing primitive).
- `src/eq2auras.Core/Timers/BuffDef.cs` — one catalog entry (id, name, duration, pattern, target-flag) + a `TryMatch` validation helper.
- `src/eq2auras.Core/Timers/BuffCatalog.cs` — the compiled 7-entry catalog + `Category` const + `Find`.
- `src/eq2auras.Core/Timers/BuffSync.cs` — `BuffSyncPlan` + the pure `Desired`/`Plan` diff.
- `src/eq2auras.Core/Config/BuffPref.cs` — one raider preference (`{id, enabled, duration override}`).

**Core (modified):**
- `src/eq2auras.Core/Timers/TimerReading.cs` — add `Category`.
- `src/eq2auras.Core/Timers/OverlayEngine.cs` — route via source rules instead of `RoutesTo`.
- `src/eq2auras.Core/Config/PanelSettings.cs` — add `Sources` (`List<SourceRule>`).
- `src/eq2auras.Core/Config/Settings.cs` — `GroupCount` 2→3; seed the three groups' `Sources`; add `BuffPrefs` + `EnabledBuffIds()`/`EffectiveDuration()` + backfill in `Normalize`.

**Plugin (new):**
- `src/eq2auras.Plugin/Act/BuffInjector.cs` — the ACT injection adapter.

**Plugin (modified):**
- `src/eq2auras.Plugin/Act/TimerProbe.cs` — read `Category` into the reading.
- `src/eq2auras.Plugin/Overlay/OverlayHost.cs` — host the third (buff) group + its default placement.
- The timer row/label builder (`TimerListBuilder` / the row visual) — the title-cased target suffix.
- `src/eq2auras.Plugin/Eq2AurasPlugin.cs` — construct/init/teardown `BuffInjector`; the per-buff enable toggles UI.

**Test (new):** `SourceRulesTests.cs`, `OverlayEngineAssociationTests.cs`, `BuffCatalogTests.cs`, `BuffSyncTests.cs`, plus additions to `SettingsTests.cs`.

---

## Task 0 — Injection spike (Windows box, GO/NO-GO gate; Alex-owned)

**This is not a code task — it is a manual on-box verification that gates Tasks 4–8.** Tasks 1–3 (the routing inversion) are safe to implement regardless, but the buff-specific work must not commit until this returns **GO**.

**Objective:** confirm the decompile-derived injection mechanism works live, and capture the exact macro log-line format so the catalog regexes are finalized against real data.

**Procedure (throwaway ACT plugin or the `Eq2AurasPlugin` behind a debug flag, Alex on the Windows box):**

- [ ] **Register a timer definition:** `ActGlobals.oFormSpellTimers.AddEditTimerDef(new TimerData("eq2auras-spiketest", false, 20, false, false, "", "", 5, true) { Category = "eq2auras Buffs" });` then `ActGlobals.oFormSpellTimers.RebuildSpellTreeView();`
- [ ] **Register a trigger** into the runtime-only set: `var ct = new CustomTrigger("eq2auras SpikeTest(?: (?<attacker>[^\"]+))?", 0, "", true, "eq2auras-spiketest", false) { Category = "eq2auras Buffs" }; ActGlobals.oFormActMain.ActiveCustomTriggers[ct.Key] = ct;`
- [ ] **Emit the macro** in-game: `/g eq2auras SpikeTest %T` (targeted) and `/g eq2auras SpikeTest` (bare). **Capture the raw log lines** from `%APPDATA%\...\eq2auras\logs` or ACT's log view — record the exact wrapper (channel verb + quoting) so Task 4's regex tail anchor is confirmed.
- [ ] **Verify a frame spawns:** `ActGlobals.oFormSpellTimers.GetTimerFrames()` contains a frame named `eq2auras-spiketest` with `Combatant` = the (lowercased) target; the bare cast yields `Combatant` `"None"`.
- [ ] **Verify transience:** confirm the trigger is NOT written to ACT's saved config (inspect the saved triggers XML after a settings save — the `ActiveCustomTriggers`-only entry must be absent from persisted `CustomTriggers`).
- [ ] **Verify zone-rebuild eviction:** change zones; confirm the trigger vanishes from `ActiveCustomTriggers` (this is what Task 6's poll-based re-ensure defends against).
- [ ] **Verify clean removal:** `ActGlobals.oFormActMain.ActiveCustomTriggers.Remove(ct.Key)` + `ActGlobals.oFormSpellTimers.RemoveTimerDef(theDef)` + `RebuildSpellTreeView()` leaves ACT's timer list clean.

**GO/NO-GO:** GO if a macro line spawns a frame with the captured target, the trigger is non-persisted, and removal is clean. **NO-GO fallback:** if `ActiveCustomTriggers` proves unusable, fall back to `CustomTriggers` + a remove-before-save/self-heal-on-init discipline (persisted path) — a spec amendment, routed back through brainstorm. **Record the captured log-line format and the confirmed channel in `spike-data/` and hand the exact tail-anchor back to Task 4.**

---

## Phase 1 — Core (strict TDD; Mac loop: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`)

### Task 1: `TimerReading` gains `Category`

**Files:**
- Modify: `src/eq2auras.Core/Timers/TimerReading.cs` (add a field after `ShowInPanelB`, `:22`)
- Modify: `src/eq2auras.Plugin/Act/TimerProbe.cs` (read `data.Category`, near `:73`)
- Test: `tests/eq2auras.Core.Tests/SourceRulesTests.cs` (the field is exercised by Task 2's tests; this task only adds the property)

**Interfaces:**
- Produces: `TimerReading.Category` (`string`, defaults null).

- [ ] **Step 1: Add the property.** In `TimerReading.cs`, after `:22`:

```csharp
        public string Category { get; set; }     // TimerData.Category — the category: routing source (SPEC §Timer groups)
```

- [ ] **Step 2: Read it in the probe (transcribe-only).** In `TimerProbe.cs`, inside the `new TimerReading { ... }` initializer (after `ShowInPanelB = data.Panel2Display,`, `:74`):

```csharp
                ShowInPanelB = data.Panel2Display,
                Category = data.Category ?? "",
```

- [ ] **Step 3: Build Core to confirm it compiles.**

Run: `dotnet build src/eq2auras.Core/eq2auras.Core.csproj`
Expected: build succeeds.

- [ ] **Step 4: Commit.**

```bash
git add src/eq2auras.Core/Timers/TimerReading.cs src/eq2auras.Plugin/Act/TimerProbe.cs
git commit -m "feat(timers): TimerReading carries Category for the category: source rule"
```

---

### Task 2: `SourceRule` + the generic `SourceRules.Matches` predicate (the anti–panel-C core)

**Files:**
- Create: `src/eq2auras.Core/Timers/SourceRule.cs`
- Create: `src/eq2auras.Core/Timers/SourceRules.cs`
- Test: `tests/eq2auras.Core.Tests/SourceRulesTests.cs`

**Interfaces:**
- Produces: `enum SourceRuleType { Panel = 0, Category = 1, Name = 2 }`; `class SourceRule { SourceRuleType Type; string Value; }` (+ `SourceRule.Panel(int)`, `SourceRule.OfCategory(string)`, `SourceRule.OfName(string)` factories); `static bool SourceRules.Matches(TimerReading, SourceRule)`; `static bool SourceRules.MatchesAny(IEnumerable<SourceRule>, TimerReading)`.

- [ ] **Step 1: Write the failing tests** (the litmus test lives here — a `category:`/`name:` rule routes with no new code):

`tests/eq2auras.Core.Tests/SourceRulesTests.cs`:
```csharp
using System.Collections.Generic;
using Eq2Auras.Core.Timers;
using Xunit;

public class SourceRulesTests
{
    private static TimerReading Reading(bool a = false, bool b = false, string category = "", string name = "x")
        => new TimerReading { Name = name, Category = category, ShowInPanelA = a, ShowInPanelB = b };

    [Fact]
    public void Panel_type_is_the_zero_value()
        => Assert.Equal(0, (int)SourceRuleType.Panel);

    [Theory]
    [InlineData("1", true, false, true)]
    [InlineData("2", true, false, false)]
    [InlineData("2", false, true, true)]
    [InlineData("1", false, false, false)]
    public void Panel_rule_matches_the_named_panel_flag(string value, bool a, bool b, bool expected)
        => Assert.Equal(expected, SourceRules.Matches(Reading(a: a, b: b), SourceRule.Panel(int.Parse(value))));

    [Fact]
    public void Category_rule_matches_case_insensitively()
    {
        var rule = SourceRule.OfCategory("eq2auras Buffs");
        Assert.True(SourceRules.Matches(Reading(category: "eq2auras Buffs"), rule));
        Assert.True(SourceRules.Matches(Reading(category: "EQ2AURAS BUFFS"), rule));
        Assert.False(SourceRules.Matches(Reading(category: "Cooldowns"), rule));
    }

    [Fact]
    public void Name_rule_matches_the_timer_name_case_insensitively()
    {
        var rule = SourceRule.OfName("Bloodlust");
        Assert.True(SourceRules.Matches(Reading(name: "bloodlust"), rule));
        Assert.False(SourceRules.Matches(Reading(name: "Turtle Shell"), rule));
    }

    [Fact]
    public void Matches_any_is_a_union_over_a_windows_rules()
    {
        // The litmus test: a group bound to TWO rules (a category AND a name) catches
        // a reading matching EITHER, with zero new code beyond the generic predicate.
        var rules = new List<SourceRule> { SourceRule.OfCategory("eq2auras Buffs"), SourceRule.OfName("Special") };
        Assert.True(SourceRules.MatchesAny(rules, Reading(category: "eq2auras Buffs", name: "Bloodlust")));
        Assert.True(SourceRules.MatchesAny(rules, Reading(category: "other", name: "Special")));
        Assert.False(SourceRules.MatchesAny(rules, Reading(category: "other", name: "Nope")));
    }

    [Fact]
    public void Empty_or_null_rule_list_matches_nothing()
    {
        Assert.False(SourceRules.MatchesAny(null, Reading(a: true)));
        Assert.False(SourceRules.MatchesAny(new List<SourceRule>(), Reading(a: true)));
    }
}
```

- [ ] **Step 2: Run to verify failure.**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter SourceRulesTests`
Expected: FAIL — `SourceRule`/`SourceRules`/`SourceRuleType` not defined.

- [ ] **Step 3: Implement.**

`src/eq2auras.Core/Timers/SourceRule.cs`:
```csharp
using System.Runtime.Serialization;

namespace Eq2Auras.Core.Timers
{
    /// A rule type over a timer reading's raw attributes. Panel is the 0-value so a
    /// DCJS-deserialized rule with an absent type lands on it (the migration default).
    public enum SourceRuleType { Panel = 0, Category = 1, Name = 2 }

    /// One clause of a group's source: "match readings whose {Type} equals {Value}".
    /// A group's source is a LIST of these (union). This is the whole association model's
    /// data — routing lives on the window, not on the timer (SPEC §Timer groups).
    [DataContract]
    public sealed class SourceRule
    {
        [DataMember(Name = "type")]
        public SourceRuleType Type { get; set; }

        [DataMember(Name = "value")]
        public string Value { get; set; }

        public static SourceRule Panel(int n) => new SourceRule { Type = SourceRuleType.Panel, Value = n.ToString() };
        public static SourceRule OfCategory(string c) => new SourceRule { Type = SourceRuleType.Category, Value = c };
        public static SourceRule OfName(string n) => new SourceRule { Type = SourceRuleType.Name, Value = n };
    }
}
```

`src/eq2auras.Core/Timers/SourceRules.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace Eq2Auras.Core.Timers
{
    /// The one generic routing predicate. A reading feeds a group iff it matches ANY of
    /// the group's rules. No code knows what "the buff window" or "panel A" is — the only
    /// difference between groups is their rule DATA (SPEC §Timer groups; the anti-panel-C core).
    public static class SourceRules
    {
        public static bool Matches(TimerReading reading, SourceRule rule)
        {
            if (reading == null || rule == null) return false;
            switch (rule.Type)
            {
                case SourceRuleType.Panel:
                    return (rule.Value == "1" && reading.ShowInPanelA)
                        || (rule.Value == "2" && reading.ShowInPanelB);
                case SourceRuleType.Category:
                    return string.Equals(reading.Category, rule.Value, StringComparison.OrdinalIgnoreCase);
                case SourceRuleType.Name:
                    return string.Equals(reading.Name, rule.Value, StringComparison.OrdinalIgnoreCase);
                default:
                    return false;
            }
        }

        public static bool MatchesAny(IEnumerable<SourceRule> rules, TimerReading reading)
            => rules != null && rules.Any(rule => Matches(reading, rule));
    }
}
```

- [ ] **Step 4: Run to verify pass.**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter SourceRulesTests`
Expected: PASS (all cases).

- [ ] **Step 5: Commit.**

```bash
git add src/eq2auras.Core/Timers/SourceRule.cs src/eq2auras.Core/Timers/SourceRules.cs tests/eq2auras.Core.Tests/SourceRulesTests.cs
git commit -m "feat(timers): generic SourceRule + Matches predicate (the association model)"
```

---

### Task 3: Route `OverlayEngine` by source rules; seed three groups in `Settings.Normalize`

**Files:**
- Modify: `src/eq2auras.Core/Config/PanelSettings.cs` (add `Sources`, after `RowSpacing`, `:51`)
- Modify: `src/eq2auras.Core/Config/Settings.cs` (`GroupCount` `:37` 2→3; seed sources — `BuffPrefs` lands in Task 5; here only `GroupCount` + source-seeding)
- Modify: `src/eq2auras.Core/Timers/OverlayEngine.cs` (`Tick` routes via `MatchesAny`; delete `RoutesTo`)
- Test: `tests/eq2auras.Core.Tests/OverlayEngineAssociationTests.cs`, additions to `tests/eq2auras.Core.Tests/SettingsTests.cs`

**Interfaces:**
- Consumes: `SourceRules.MatchesAny`, `SourceRule.Panel/OfCategory` (Task 2).
- Produces: `PanelSettings.Sources` (`List<SourceRule>`, default null → seeded); `Settings.GroupCount == 3`; `Settings.Normalize` seeds `Panels[0].Sources=[panel:1]`, `Panels[1].Sources=[panel:2]`, `Panels[2].Sources=[category:"eq2auras Buffs"]` when a group's sources are null/empty.

- [ ] **Step 1: Write the failing tests.**

`tests/eq2auras.Core.Tests/OverlayEngineAssociationTests.cs`:
```csharp
using System.Collections.Generic;
using System.Linq;
using Eq2Auras.Core.Config;
using Eq2Auras.Core.Timers;
using Xunit;

public class OverlayEngineAssociationTests
{
    private static TimerReading R(string name, bool a = false, bool b = false, string category = "")
        => new TimerReading { Name = name, Category = category, ShowInPanelA = a, ShowInPanelB = b,
            TimeLeft = 25, TotalSeconds = 30, RawPreciseTimeLeft = 25, WarningValue = 10, RemoveValueSeconds = -15, IsMaster = true };

    [Fact]
    public void Panel_flagged_readings_route_to_the_two_panel_groups_as_before()
    {
        var frames = new OverlayEngine(new Settings()).Tick(new List<TimerReading> { R("boss", a: true), R("cd", b: true) });
        Assert.Equal("boss", Assert.Single(frames[0].ListRows).Name);   // panel:1
        Assert.Equal("cd", Assert.Single(frames[1].ListRows).Name);     // panel:2
    }

    [Fact]
    public void A_reserved_category_reading_routes_only_to_the_buff_group()
    {
        var frames = new OverlayEngine(new Settings()).Tick(new List<TimerReading> { R("Bloodlust", category: "eq2auras Buffs") });
        Assert.Empty(frames[0].ListRows);
        Assert.Empty(frames[1].ListRows);
        Assert.Equal("Bloodlust", Assert.Single(frames[2].ListRows).Name);   // category:eq2auras Buffs
    }

    [Fact]
    public void There_are_exactly_three_seeded_groups()
        => Assert.Equal(3, new OverlayEngine(new Settings()).Tick(new List<TimerReading>()).Count);
}
```

Additions to `tests/eq2auras.Core.Tests/SettingsTests.cs` (new `[Fact]`s in the existing class):
```csharp
    [Fact]
    public void Normalize_seeds_three_groups_with_panel_and_buff_sources()
    {
        var s = Settings.Parse("{}");
        Assert.Equal(3, s.Panels.Count);
        Assert.Equal(SourceRuleType.Panel, s.Panels[0].Sources[0].Type);
        Assert.Equal("1", s.Panels[0].Sources[0].Value);
        Assert.Equal("2", s.Panels[1].Sources[0].Value);
        Assert.Equal(SourceRuleType.Category, s.Panels[2].Sources[0].Type);
        Assert.Equal("eq2auras Buffs", s.Panels[2].Sources[0].Value);
    }

    [Fact]
    public void A_legacy_two_panel_file_migrates_forward_to_three_groups()
    {
        // A saved file from before the buff window: two panels, no sources, no buff group.
        var json = "{\"panels\":[{\"colorSource\":0},{\"colorSource\":0}]}";
        var s = Settings.Parse(json);
        Assert.Equal(3, s.Panels.Count);
        Assert.Equal("1", s.Panels[0].Sources[0].Value);
        Assert.Equal("eq2auras Buffs", s.Panels[2].Sources[0].Value);
    }
```

- [ ] **Step 2: Run to verify failure.**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter "OverlayEngineAssociationTests|SettingsTests"`
Expected: FAIL — `PanelSettings.Sources` missing; only two groups.

- [ ] **Step 3: Implement — `PanelSettings.Sources`.** In `PanelSettings.cs`, after `RowSpacing` (`:51`):

```csharp
        [DataMember(Name = "sources")]
        public List<SourceRule> Sources { get; set; }   // null/empty = seeded by Settings.Normalize (SPEC §Timer groups)
```

Add `using System.Collections.Generic;` and `using Eq2Auras.Core.Timers;` to `PanelSettings.cs` if absent.

- [ ] **Step 4: Implement — `GroupCount` + seeding in `Settings.cs`.** Change `GroupCount` (`:37`):

```csharp
    public const int GroupCount = 3;   // panel:1, panel:2, category:"eq2auras Buffs" (SPEC §Timer groups)
```

In `Normalize()`, after the pad/truncate block (`while (Panels.Count < GroupCount) ...; if (Panels.Count > GroupCount) ...`) and before the `legacyFile` block, add source-seeding:

```csharp
        // Seed each group's source when unset. v1 has no source-authoring UI, so the three
        // seeds are deterministic; a future UI writes user rules that survive here (SPEC §Timer groups).
        SeedSources(Panels[0], SourceRule.Panel(1));
        SeedSources(Panels[1], SourceRule.Panel(2));
        SeedSources(Panels[2], SourceRule.OfCategory(BuffCatalog.Category));
```

Add the helper (private static, alongside `OutOfRange`):

```csharp
    private static void SeedSources(PanelSettings group, SourceRule seed)
    {
        if (group.Sources == null || group.Sources.Count == 0)
            group.Sources = new List<SourceRule> { seed };
    }
```

Add `using Eq2Auras.Core.Timers;` to `Settings.cs` if absent. (`BuffCatalog.Category` is defined in Task 4 — if implementing Task 3 first, temporarily inline the literal `"eq2auras Buffs"` and swap to `BuffCatalog.Category` when Task 4 lands; the tests use the literal either way.)

- [ ] **Step 5: Implement — route `OverlayEngine` by rules.** Replace the `Tick` body and delete `RoutesTo` (`OverlayEngine.cs:26-40`):

```csharp
    public List<OverlayFrame> Tick(IReadOnlyList<TimerReading> readings)
    {
        return _trackers
            .Select((tracker, i) => tracker.Tick(
                readings.Where(r => SourceRules.MatchesAny(_settings.Panels[i].Sources, r)).ToList(),
                _settings.PaletteArgb))
            .ToList();
    }
```

Delete the `RoutesTo` method entirely. Keep the constructor and `_palette` sharing unchanged.

- [ ] **Step 6: Run to verify pass.**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter "OverlayEngineAssociationTests|SettingsTests|EscalationTrackerTests|OverlayEngineTests"`
Expected: PASS — including the pre-existing `OverlayEngineTests`/`EscalationTrackerTests` (the routing change must not regress them; if a pre-existing test asserted 2 groups, update it to 3 and note it in the commit).

- [ ] **Step 7: Commit.**

```bash
git add src/eq2auras.Core/Config/PanelSettings.cs src/eq2auras.Core/Config/Settings.cs src/eq2auras.Core/Timers/OverlayEngine.cs tests/eq2auras.Core.Tests/OverlayEngineAssociationTests.cs tests/eq2auras.Core.Tests/SettingsTests.cs
git commit -m "feat(timers): route by source rules; seed three groups (panel:1/2 + buff category)"
```

---

### Task 4: The buff catalog (`BuffDef` + `BuffCatalog`) — GATED on Task 0 GO

**Files:**
- Create: `src/eq2auras.Core/Timers/BuffDef.cs`
- Create: `src/eq2auras.Core/Timers/BuffCatalog.cs`
- Test: `tests/eq2auras.Core.Tests/BuffCatalogTests.cs`

**Interfaces:**
- Produces: `class BuffDef { string Id; string DisplayName; int DurationSeconds; string Pattern; bool IsTargeted; bool TryMatch(string line, out string name); }`; `static class BuffCatalog { const string Category = "eq2auras Buffs"; IReadOnlyList<BuffDef> All; BuffDef Find(string id); }`.

**DATA — durations now sourced (census, 2026-08-19):** all 22 base durations are filled from the Daybreak census `spell` collection (`duration.max_sec_tenths ÷ 10`; AAs carry `alternate_advancement:1`). Three are **tier-variable** (`†`: Tsunami 20.6, Adrenaline 33.0, Sanctuary 30.9 — rounded to Grandmaster max; the per-buff **override** corrects for a character running higher tiers). Our tracker names are kept **consistent with census** (Alex, 2026-08-19): two were mis-typed and corrected — **Tortoise Shell** (was "Turtle Shell") and **Advance Warning** (was "Advanced Warning") — so the display name, the expected log-line text, and the census ability name all match. **Still Task-0-confirmed:** the group-wide **wrapper** shape (`\S+ says to the group, "…"`) — the single-target payload patterns are robust.

- [ ] **Step 1: Write the failing tests** (regex correctness is the Mac-testable heart — validate against representative full log lines):

`tests/eq2auras.Core.Tests/BuffCatalogTests.cs`:
```csharp
using System.Linq;
using Eq2Auras.Core.Timers;
using Xunit;

public class BuffCatalogTests
{
    [Fact]
    public void The_category_is_the_reserved_namespace()
        => Assert.Equal("eq2auras Buffs", BuffCatalog.Category);

    [Fact]
    public void Seeds_the_twenty_two_v1_buffs()
    {
        var ids = BuffCatalog.All.Select(b => b.Id).ToList();
        Assert.Equal(22, ids.Count);
        Assert.Equal(12, BuffCatalog.All.Count(b => b.IsTargeted));
        Assert.Equal(10, BuffCatalog.All.Count(b => !b.IsTargeted));
        Assert.Contains("bolster", ids);
        Assert.Contains("advance-warning", ids);
    }

    [Fact]
    public void Every_buff_has_a_positive_duration_and_a_pattern()
        => Assert.All(BuffCatalog.All, b => { Assert.True(b.DurationSeconds > 0); Assert.False(string.IsNullOrEmpty(b.Pattern)); });

    [Fact]
    public void Ids_are_unique()
        => Assert.Equal(BuffCatalog.All.Count, BuffCatalog.All.Select(b => b.Id).Distinct().Count());

    [Fact]
    public void A_single_target_buff_captures_the_target_from_the_payload()
    {
        var bolster = BuffCatalog.Find("bolster");
        Assert.True(bolster.IsTargeted);
        var line = "(1734900000)[Wed Aug 19 20:00:00 2026] Alex says to the group, \"eq2auras Bolster Bob\"";
        Assert.True(bolster.TryMatch(line, out var name));
        Assert.Equal("Bob", name);   // the target
    }

    [Fact]
    public void A_group_wide_buff_captures_the_caster_from_the_wrapper()
    {
        var turtle = BuffCatalog.Find("tortoise-shell");
        Assert.False(turtle.IsTargeted);
        var line = "(1734900000)[Wed Aug 19 20:00:00 2026] Alex says to the group, \"eq2auras Tortoise Shell\"";
        Assert.True(turtle.TryMatch(line, out var name));
        Assert.Equal("Alex", name);   // the caster — the group proxy (SPEC §Display)
    }

    [Fact]
    public void A_non_matching_line_is_rejected()
        => Assert.False(BuffCatalog.Find("bolster").TryMatch("(1734900000)[date] Alex says, \"hello\"", out _));

    [Fact]
    public void Find_returns_null_for_an_unknown_id()
        => Assert.Null(BuffCatalog.Find("nope"));
}
```

- [ ] **Step 2: Run to verify failure.**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter BuffCatalogTests`
Expected: FAIL — `BuffDef`/`BuffCatalog` not defined.

- [ ] **Step 3: Implement — `BuffDef`.**

`src/eq2auras.Core/Timers/BuffDef.cs`:
```csharp
using System.Text.RegularExpressions;

namespace Eq2Auras.Core.Timers
{
    /// One catalog entry: the bounded library's atom. `Pattern` is handed verbatim to ACT's
    /// CustomTrigger (ACT does the matching); `TryMatch` is the Mac-testable validator that
    /// proves the shipped regex captures the right NAME (SPEC §Buff tracking). `IsTargeted`
    /// distinguishes single-target buffs (macro carries %T; regex captures the target from the
    /// payload) from group-wide ones (no %T; regex captures the CASTER from the chat wrapper).
    public sealed class BuffDef
    {
        public string Id { get; }
        public string DisplayName { get; }
        public int DurationSeconds { get; }   // catalog BASE duration (census); a raider may override (BuffPref)
        public string Pattern { get; }
        public bool IsTargeted { get; }

        private readonly Regex _rx;

        public BuffDef(string id, string displayName, int durationSeconds, string pattern, bool isTargeted)
        {
            Id = id;
            DisplayName = displayName;
            DurationSeconds = durationSeconds;
            Pattern = pattern;
            IsTargeted = isTargeted;
            _rx = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }

        /// True if the line matches; `name` is the captured `attacker` group (the target for a
        /// targeted buff, the caster for a group-wide one), or null when absent/empty.
        public bool TryMatch(string line, out string name)
        {
            name = null;
            if (line == null) return false;
            var m = _rx.Match(line);
            if (!m.Success) return false;
            var g = m.Groups["attacker"];
            if (g.Success && g.Value.Length > 0) name = g.Value;
            return true;
        }
    }
}
```

- [ ] **Step 4: Implement — `BuffCatalog`.** The pattern is a substring match on the chat payload; the `[^"]+` tail stops at EQ2's closing chat quote (confirm against Task 0). Targeted buffs carry `(?: (?<attacker>[^"]+))?`; targetless omit it.

`src/eq2auras.Core/Timers/BuffCatalog.cs`:
```csharp
using System.Collections.Generic;
using System.Linq;

namespace Eq2Auras.Core.Timers
{
    /// The bounded v1 buff library. The compiled sibling of MetricRegistry: adding a buff =
    /// appending a BuffDef. Category is the reserved routing/management/segregation namespace
    /// for ALL eq2auras-managed timers (SPEC §Buff tracking).
    public static class BuffCatalog
    {
        public const string Category = "eq2auras Buffs";

        // Durations are census BASE values (spell + AA collections), filled from the census pull.
        // Single-target: `attacker` captures the %T target from the payload. Group-wide: `attacker`
        // captures the CASTER from the chat wrapper — the exact wrapper (verb/quotes) is confirmed
        // by the Task-0 captured line; the `\S+ says to the group, "…"` shape below is provisional.
        public static readonly IReadOnlyList<BuffDef> All = new List<BuffDef>
        {
            // --- Single-target (12) — captures the target from the payload ---
            // Durations = census base (Grandmaster max ÷ 10s). †tier-variable → GM max, per-char override corrects.
            new BuffDef("bolster",             "Bolster",             36,  "eq2auras Bolster (?<attacker>[^\"]+)",             isTargeted: true),
            new BuffDef("jesters-cap",         "Jester's Cap",        30,  "eq2auras Jester's Cap (?<attacker>[^\"]+)",        isTargeted: true),
            new BuffDef("ritual-of-alacrity",  "Ritual of Alacrity",  30,  "eq2auras Ritual of Alacrity (?<attacker>[^\"]+)",  isTargeted: true),
            new BuffDef("holy-shield",         "Holy Shield",         30,  "eq2auras Holy Shield (?<attacker>[^\"]+)",         isTargeted: true),
            new BuffDef("animal-form",         "Animal Form",         60,  "eq2auras Animal Form (?<attacker>[^\"]+)",         isTargeted: true),
            new BuffDef("got-your-back",       "Got Your Back",       15,  "eq2auras Got Your Back (?<attacker>[^\"]+)",       isTargeted: true),
            new BuffDef("tsunami",             "Tsunami",             21,  "eq2auras Tsunami (?<attacker>[^\"]+)",             isTargeted: true),   // †20.6
            new BuffDef("divine-aura",         "Divine Aura",         10,  "eq2auras Divine Aura (?<attacker>[^\"]+)",         isTargeted: true),
            new BuffDef("adrenaline",          "Adrenaline",          33,  "eq2auras Adrenaline (?<attacker>[^\"]+)",          isTargeted: true),   // †33.0
            new BuffDef("unyielding-will",     "Unyielding Will",     180, "eq2auras Unyielding Will (?<attacker>[^\"]+)",     isTargeted: true),
            new BuffDef("brutal-inspiration",  "Brutal Inspiration",  30,  "eq2auras Brutal Inspiration (?<attacker>[^\"]+)",  isTargeted: true),
            new BuffDef("gravitas",            "Gravitas",            30,  "eq2auras Gravitas (?<attacker>[^\"]+)",            isTargeted: true),

            // --- Group/raid-wide (10) — captures the caster from the chat wrapper ---
            new BuffDef("tortoise-shell",            "Tortoise Shell",            30, "(?<attacker>\\S+) says to the group, \"eq2auras Tortoise Shell\"",            isTargeted: false),
            new BuffDef("bladedance",                "Bladedance",                30, "(?<attacker>\\S+) says to the group, \"eq2auras Bladedance\"",                isTargeted: false),
            new BuffDef("cacophony-of-blades",       "Cacophony of Blades",       12, "(?<attacker>\\S+) says to the group, \"eq2auras Cacophony of Blades\"",       isTargeted: false),
            new BuffDef("perfection-of-the-maestro", "Perfection of the Maestro", 20, "(?<attacker>\\S+) says to the group, \"eq2auras Perfection of the Maestro\"", isTargeted: false),
            new BuffDef("frigid-gift",               "Frigid Gift",               24, "(?<attacker>\\S+) says to the group, \"eq2auras Frigid Gift\"",               isTargeted: false),
            new BuffDef("curse-of-darkness",         "Curse of Darkness",         12, "(?<attacker>\\S+) says to the group, \"eq2auras Curse of Darkness\"",         isTargeted: false),
            new BuffDef("peace-of-mind",             "Peace of Mind",             20, "(?<attacker>\\S+) says to the group, \"eq2auras Peace of Mind\"",             isTargeted: false),
            new BuffDef("death-march",               "Death March",               60, "(?<attacker>\\S+) says to the group, \"eq2auras Death March\"",               isTargeted: false),
            new BuffDef("sanctuary",                 "Sanctuary",                 31, "(?<attacker>\\S+) says to the group, \"eq2auras Sanctuary\"",                 isTargeted: false), // †30.9
            new BuffDef("advance-warning",           "Advance Warning",           13, "(?<attacker>\\S+) says to the group, \"eq2auras Advance Warning\"",           isTargeted: false),
        };

        public static BuffDef Find(string id) => All.FirstOrDefault(b => b.Id == id);
    }
}
```

- [ ] **Step 5: Run to verify pass.**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter BuffCatalogTests`
Expected: PASS.

- [ ] **Step 6: Commit.**

```bash
git add src/eq2auras.Core/Timers/BuffDef.cs src/eq2auras.Core/Timers/BuffCatalog.cs tests/eq2auras.Core.Tests/BuffCatalogTests.cs
git commit -m "feat(timers): bounded buff catalog (7 v1 seeds) + regex validator"
```

---

### Task 5: The per-buff prefs (`BuffPref` + `Settings.BuffPrefs`) + the pure sync plan (`BuffSync`)

**Files:**
- Create: `src/eq2auras.Core/Config/BuffPref.cs`
- Modify: `src/eq2auras.Core/Config/Settings.cs` (add `BuffPrefs` + `EnabledBuffIds()`/`EffectiveDuration()` + backfill in `Normalize`)
- Create: `src/eq2auras.Core/Timers/BuffSync.cs`
- Test: `tests/eq2auras.Core.Tests/BuffSyncTests.cs`, additions to `SettingsTests.cs`

**Interfaces:**
- Produces: `class BuffPref { string Id; bool Enabled; int? DurationOverride; }`; `Settings.BuffPrefs` (`List<BuffPref>`, null → backfilled to one pref per catalog id, `Enabled=true`, no override = default all-on); `Settings.EnabledBuffIds()` (the enabled prefs' ids); `Settings.EffectiveDuration(string id)` (`pref.DurationOverride ?? BuffCatalog.Find(id).DurationSeconds`); `class BuffSyncPlan { IReadOnlyList<string> ToAdd; IReadOnlyList<string> ToRemove; }`; `static class BuffSync { IReadOnlyList<BuffDef> Desired(IEnumerable<string> enabledIds); BuffSyncPlan Plan(IReadOnlyCollection<string> currentlyInjectedIds, IEnumerable<string> enabledIds); }` (BuffSync stays id-based — it only diffs membership; the effective duration is applied by the injector at build time, Task 6).

- [ ] **Step 1: Write the failing tests.**

`tests/eq2auras.Core.Tests/BuffSyncTests.cs`:
```csharp
using System.Collections.Generic;
using System.Linq;
using Eq2Auras.Core.Timers;
using Xunit;

public class BuffSyncTests
{
    [Fact]
    public void Desired_is_catalog_intersect_enabled()
    {
        var desired = BuffSync.Desired(new[] { "bolster", "turtle-shell", "not-a-buff" }).Select(b => b.Id).ToList();
        Assert.Equal(new[] { "bolster", "turtle-shell" }, desired);
    }

    [Fact]
    public void Null_enabled_set_desires_nothing()
        => Assert.Empty(BuffSync.Desired(null));

    [Fact]
    public void Plan_adds_newly_enabled_and_removes_no_longer_enabled()
    {
        var plan = BuffSync.Plan(currentlyInjectedIds: new[] { "bolster", "bladedance" }, enabledIds: new[] { "bolster", "turtle-shell" });
        Assert.Equal(new[] { "turtle-shell" }, plan.ToAdd);
        Assert.Equal(new[] { "bladedance" }, plan.ToRemove);
    }

    [Fact]
    public void Plan_from_empty_current_is_the_init_and_zone_reinject_case()
    {
        // On init (nothing injected) or after a zone rebuild (triggers evicted), everything desired is added.
        var plan = BuffSync.Plan(currentlyInjectedIds: new string[0], enabledIds: new[] { "bolster", "turtle-shell" });
        Assert.Equal(new[] { "bolster", "turtle-shell" }, plan.ToAdd.OrderBy(x => x));
        Assert.Empty(plan.ToRemove);
    }

    [Fact]
    public void Plan_sweeps_a_stale_injected_id_not_in_the_catalog_or_enabled_set()
    {
        // A def left over from an older version (id no longer enabled) is swept.
        var plan = BuffSync.Plan(currentlyInjectedIds: new[] { "retired-buff" }, enabledIds: new[] { "bolster" });
        Assert.Equal(new[] { "bolster" }, plan.ToAdd);
        Assert.Equal(new[] { "retired-buff" }, plan.ToRemove);
    }
}
```

Additions to `SettingsTests.cs`:
```csharp
    [Fact]
    public void Buff_prefs_default_to_all_catalog_ids_enabled_and_no_override()
    {
        var s = Settings.Parse("{}");
        Assert.Equal(Eq2Auras.Core.Timers.BuffCatalog.All.Select(b => b.Id).OrderBy(x => x),
                     s.EnabledBuffIds().OrderBy(x => x));
        Assert.All(s.BuffPrefs, p => Assert.Null(p.DurationOverride));
    }

    [Fact]
    public void An_explicit_empty_pref_list_is_preserved_as_none_enabled()
    {
        var s = Settings.Parse("{\"buffPrefs\":[]}");
        Assert.Empty(s.EnabledBuffIds());
    }

    [Fact]
    public void A_duration_override_wins_over_the_catalog_base()
    {
        var s = Settings.Parse("{\"buffPrefs\":[{\"id\":\"bolster\",\"enabled\":true,\"durationOverride\":48}]}");
        Assert.Equal(48, s.EffectiveDuration("bolster"));
    }

    [Fact]
    public void Effective_duration_falls_back_to_the_catalog_base_with_no_override()
    {
        var s = Settings.Parse("{\"buffPrefs\":[{\"id\":\"bolster\",\"enabled\":true}]}");
        Assert.Equal(36, s.EffectiveDuration("bolster"));   // census base
    }
```

- [ ] **Step 2: Run to verify failure.**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter "BuffSyncTests|SettingsTests"`
Expected: FAIL — `BuffSync` / `BuffPref` / `Settings.BuffPrefs` missing.

- [ ] **Step 3: Implement — `BuffPref` + `Settings.BuffPrefs` + helpers + backfill.**

Create `src/eq2auras.Core/Config/BuffPref.cs`:
```csharp
using System.Runtime.Serialization;

namespace Eq2Auras.Core.Config
{
    /// One raider preference for one catalog buff: tracked or not, and an optional per-character
    /// duration override (null = use the catalog base). DCJS: Enabled's 0-value is false, so the
    /// backfill sets it explicitly (SPEC §Buff tracking — the tracked set and per-buff overrides).
    [DataContract]
    public sealed class BuffPref
    {
        [DataMember(Name = "id")]
        public string Id { get; set; }

        [DataMember(Name = "enabled")]
        public bool Enabled { get; set; }

        [DataMember(Name = "durationOverride")]
        public int? DurationOverride { get; set; }
    }
}
```

Add to `Settings.cs`, near `Panels` (`:50`):
```csharp
    [DataMember(Name = "buffPrefs")]
    public List<BuffPref> BuffPrefs { get; set; }   // null = never set → backfilled all-on (SPEC §Buff tracking)

    public IEnumerable<string> EnabledBuffIds()
        => (BuffPrefs ?? new List<BuffPref>()).Where(p => p != null && p.Enabled).Select(p => p.Id);

    public int EffectiveDuration(string id)
    {
        var pref = (BuffPrefs ?? new List<BuffPref>()).FirstOrDefault(p => p != null && p.Id == id);
        var def = BuffCatalog.Find(id);
        return pref?.DurationOverride ?? def?.DurationSeconds ?? 0;
    }
```

In `Normalize()`, after the source-seeding block (Task 3), backfill:
```csharp
        // null = never set → default the whole curated set on (harmless without macros), no overrides.
        // A non-null list is the raider's own choices (incl. an explicit empty = all off) — preserved.
        if (BuffPrefs == null)
            BuffPrefs = BuffCatalog.All.Select(b => new BuffPref { Id = b.Id, Enabled = true }).ToList();
```

- [ ] **Step 4: Implement — `BuffSync`.**

`src/eq2auras.Core/Timers/BuffSync.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace Eq2Auras.Core.Timers
{
    public sealed class BuffSyncPlan
    {
        public IReadOnlyList<string> ToAdd { get; }
        public IReadOnlyList<string> ToRemove { get; }
        public BuffSyncPlan(IReadOnlyList<string> toAdd, IReadOnlyList<string> toRemove)
        {
            ToAdd = toAdd;
            ToRemove = toRemove;
        }
    }

    /// Pure enabled-set → injection policy. One function serves toggles, init-sweep (of stale
    /// defs), and zone-rebuild re-injection (current = empty) (SPEC §Buff tracking).
    public static class BuffSync
    {
        public static IReadOnlyList<BuffDef> Desired(IEnumerable<string> enabledIds)
            => enabledIds == null
                ? new List<BuffDef>()
                : enabledIds.Select(BuffCatalog.Find).Where(b => b != null).ToList();

        public static BuffSyncPlan Plan(IReadOnlyCollection<string> currentlyInjectedIds, IEnumerable<string> enabledIds)
        {
            var desired = new HashSet<string>(Desired(enabledIds).Select(b => b.Id), StringComparer.OrdinalIgnoreCase);
            var current = new HashSet<string>(currentlyInjectedIds ?? new string[0], StringComparer.OrdinalIgnoreCase);
            return new BuffSyncPlan(
                toAdd: desired.Where(id => !current.Contains(id)).ToList(),
                toRemove: current.Where(id => !desired.Contains(id)).ToList());
        }
    }
}
```

- [ ] **Step 5: Run to verify pass.**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter "BuffSyncTests|SettingsTests"`
Expected: PASS.

- [ ] **Step 6: Full Core suite green.**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
Expected: PASS (no regressions across the whole suite).

- [ ] **Step 7: Commit.**

```bash
git add src/eq2auras.Core/Config/Settings.cs src/eq2auras.Core/Timers/BuffSync.cs tests/eq2auras.Core.Tests/BuffSyncTests.cs tests/eq2auras.Core.Tests/SettingsTests.cs
git commit -m "feat(timers): enabled-buff set (default all-on) + pure BuffSync plan"
```

---

## Phase 2 — Plugin (transcribe-only; CI-compile + on-box live verification). GATED on Task 0 GO.

> Plugin code is not TDD (no `Advanced_Combat_Tracker` on the Mac). Each task compiles via branch CI (WPF markup + msbuild) and is verified by the merge-gate live script. Keep changes minimal and mechanical.

### Task 6: `BuffInjector` — the ACT injection adapter

**Files:**
- Create: `src/eq2auras.Plugin/Act/BuffInjector.cs`
- Modify: `src/eq2auras.Plugin/Eq2AurasPlugin.cs` (construct, `InitPlugin` sweep+sync, hook the poll for ensure-present, `DeInitPlugin` teardown)

**Interfaces:**
- Consumes: `BuffCatalog`, `BuffSync`, `BuffSyncPlan`, `Settings.EnabledBuffIds()`, `Settings.EffectiveDuration()` (Core).
- Produces: `class BuffInjector { void SyncTo(IEnumerable<string> enabledIds); void EnsurePresent(); void TearDown(); }`.

- [ ] **Step 1: Write `BuffInjector` (transcribe-only).** Uses the Task-0-verified API. Each buff = a persisted `TimerData` def (registered once, swept on init) + a transient `CustomTrigger` in `ActiveCustomTriggers` (re-ensured each poll).

`src/eq2auras.Plugin/Act/BuffInjector.cs`:
```csharp
using System.Collections.Generic;
using System.Linq;
using Advanced_Combat_Tracker;
using Eq2Auras.Core.Config;
using Eq2Auras.Core.Timers;

namespace Eq2Auras.Plugin.Act
{
    /// Registers each enabled buff as a TimerData def + a transient ActiveCustomTriggers entry,
    /// so ACT matches the macro line and spawns our timer. The def persists (swept on init); the
    /// trigger is runtime-only and re-ensured each poll against zone-rebuild eviction
    /// (SPEC §Buff tracking; decompile-verified ACT 3.8.5.288).
    public sealed class BuffInjector
    {
        private static string TriggerKey(BuffDef b) => BuffCatalog.Category + "|" + b.Pattern; // mirrors CustomTrigger.Key = category + "|" + ShortRegexString
        private static string DefName(BuffDef b) => "eq2auras-" + b.Id;

        /// Idempotently make the live ACT state match `enabledIds`: sweep stale eq2auras defs,
        /// register/withdraw defs, and (re)inject triggers. Called on init and on every toggle.
        public void SyncTo(Settings settings)
        {
            var injectedNow = CurrentlyInjectedIds();
            var plan = BuffSync.Plan(injectedNow, settings.EnabledBuffIds());

            foreach (var id in plan.ToRemove) Remove(id);
            foreach (var id in plan.ToAdd)
            {
                var def = BuffCatalog.Find(id);
                if (def != null) Add(def, settings.EffectiveDuration(id));   // override ?? catalog base
            }
            ActGlobals.oFormSpellTimers.RebuildSpellTreeView();
        }

        /// Cheap per-poll self-heal: re-add any desired trigger evicted by a zone rebuild.
        /// Defs persist across zone change, so only the ActiveCustomTriggers entries need it.
        public void EnsurePresent()
        {
            foreach (var def in BuffSync.Desired(CurrentlyInjectedIds()))
            {
                if (!ActGlobals.oFormActMain.ActiveCustomTriggers.ContainsKey(TriggerKey(def)))
                    ActGlobals.oFormActMain.ActiveCustomTriggers[TriggerKey(def)] = BuildTrigger(def);
            }
        }

        public void TearDown()
        {
            foreach (var def in Eq2aurasDefs().ToList()) Remove(def.Id);
            ActGlobals.oFormSpellTimers.RebuildSpellTreeView();
        }

        // "Injected" is defined by our persisted defs (the durable record); triggers ride them.
        private IReadOnlyCollection<string> CurrentlyInjectedIds() => Eq2aurasDefs().Select(d => d.Id).ToList();

        private static IEnumerable<BuffDef> Eq2aurasDefs()
            => ActGlobals.oFormSpellTimers.TimerDefs.Values
                .Where(td => td.Category == BuffCatalog.Category)
                .Select(td => BuffCatalog.Find(td.Name.StartsWith("eq2auras-") ? td.Name.Substring("eq2auras-".Length) : td.Name))
                .Where(b => b != null);

        private static void Add(BuffDef def, int effectiveDuration)
        {
            ActGlobals.oFormSpellTimers.AddEditTimerDef(new TimerData(DefName(def), false, effectiveDuration, false, false, "", "", 5, true) { Category = BuffCatalog.Category });
            ActGlobals.oFormActMain.ActiveCustomTriggers[TriggerKey(def)] = BuildTrigger(def);
        }

        private static void Remove(string id)
        {
            var def = BuffCatalog.Find(id);
            var pattern = def?.Pattern;
            var key = pattern != null ? BuffCatalog.Category + "|" + pattern : null;
            if (key != null) ActGlobals.oFormActMain.ActiveCustomTriggers.Remove(key);
            var td = ActGlobals.oFormSpellTimers.TimerDefs.Values.FirstOrDefault(t => t.Category == BuffCatalog.Category && t.Name == "eq2auras-" + id);
            if (td != null) ActGlobals.oFormSpellTimers.RemoveTimerDef(td);
        }

        private static CustomTrigger BuildTrigger(BuffDef def)
            => new CustomTrigger(def.Pattern, 0, "", true, DefName(def), false) { Category = BuffCatalog.Category };
    }
}
```

> **Transcribe note:** `ActGlobals.oFormSpellTimers.TimerDefs` and `CustomTrigger.Key` shape are the decompile-observed members; if the on-box build reveals a different accessor (e.g. `TimerDefs` is non-public), the merge-gate step will surface the compile error — adjust to the real member and re-verify. `TimerData`'s constructor arg order is the decompiled `(name, onlyMasterTicks, timerValue, restrictToMe, absoluteTiming, sound, ..., warningValue, ...)`; confirm against the box in Task 0.

- [ ] **Step 2: Wire into the plugin lifecycle (transcribe-only).** In `Eq2AurasPlugin.cs`:
- Construct `private readonly BuffInjector _buffInjector = new BuffInjector();`
- In `InitPlugin`, after settings load: `_buffInjector.SyncTo(_settings);`
- In the poll handler (the existing `TimerProbe` `_onPollTick` or the 100 ms tick), on a divider (~every 5th tick): `_buffInjector.EnsurePresent();`
- In `DeInitPlugin`: `_buffInjector.TearDown();`

- [ ] **Step 3: Branch CI compile gate.**

```bash
git add src/eq2auras.Plugin/Act/BuffInjector.cs src/eq2auras.Plugin/Eq2AurasPlugin.cs
git commit -m "feat(plugin): BuffInjector — transient TimerData+CustomTrigger injection + zone re-ensure"
git push   # branch push = verify-only CI (Core tests + WPF compile + artifact)
```
Expected: CI green (compiles against the real ACT reference).

---

### Task 7: Host the buff window (third group) + the title-cased target suffix

**Files:**
- Modify: `src/eq2auras.Plugin/Overlay/OverlayHost.cs` (host a window pair for the third group; default placement)
- Modify: the timer row/label builder (`src/eq2auras.Core/Timers/TimerListBuilder.cs` and/or the row visual) — the target suffix

**OPEN DISPLAY DECISION (confirm at plan review):** the suffix rule. Recommended: render `{Name} — {TitleCase(Combatant)}` whenever `Combatant` is **meaningful** (not empty / `"none"` / `"you"`), as a **general** row rule consistent with ACT's own `{Name} - {Combatant}` label and the SPEC data-table intent (`SPEC.md:136`). Buffs get their target; existing panel timers with a `none`/self combatant are unchanged; existing timers with a *real* combatant would gain a caster suffix (covered by the merge-gate timer-regression check). Alternative if that regression is unwanted: scope the suffix to the buff group only. **This is the one behavior the plan can't derive purely from the spec — confirm before implementing.**

- [ ] **Step 1: Implement the meaningful-combatant suffix (Core, TDD since it's in `TimerListBuilder`).** Add a helper + test in Core:

`tests/eq2auras.Core.Tests/TimerListBuilderTests.cs` (add):
```csharp
    [Theory]
    [InlineData("Bloodlust", "bob", "Bloodlust — Bob")]
    [InlineData("Tortoise Shell", "none", "Tortoise Shell")]
    [InlineData("Tortoise Shell", "None", "Tortoise Shell")]
    [InlineData("Soul Paralysis", "you", "Soul Paralysis")]
    [InlineData("Bloodlust", "", "Bloodlust")]
    public void Label_appends_a_title_cased_target_only_when_meaningful(string name, string combatant, string expected)
        => Assert.Equal(expected, TimerListBuilder.Label(name, combatant));
```

Implement `public static string Label(string name, string combatant)` in `TimerListBuilder`: return `name` when `combatant` is null/empty or equals (ordinal-ignore-case) `"none"`/`"you"`; else `name + " — " + TitleCase(combatant)` (title-case = upper first char + lower rest, single-token EQ2 names).

- [ ] **Step 2: Route rows through `Label` (transcribe-only in the row visual/list builder), and host the third group in `OverlayHost`** — iterate the seeded `Settings.Panels` (now 3) rather than a hardcoded 2, and give the buff group (index 2) a distinct default position (offset from panels A/B so it doesn't overlap on first run).

- [ ] **Step 3: Core tests + CI compile.**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter TimerListBuilderTests`
Expected: PASS. Then commit + push for the WPF compile gate.

```bash
git add -u
git commit -m "feat: buff window (third group) + title-cased target suffix on meaningful combatant"
git push
```

---

### Task 8: Per-buff enable toggles (settings UI)

**Files:**
- Modify: `src/eq2auras.Plugin/Eq2AurasPlugin.cs` (the plugin tab — a checkbox + duration field per catalog buff, bound to `Settings.BuffPrefs`, re-syncing on change)

- [ ] **Step 1: Implement (transcribe-only).** In the plugin's config tab, add a group box "Tracked buffs" with one **row per `BuffCatalog.All` entry**: an enable **checkbox** (label = `DisplayName`, checked = the buff's `BuffPref.Enabled`) and a numeric **duration field** (the override — shows the effective value; a `NumericUpDown` seeded with `_settings.EffectiveDuration(id)`, its placeholder/reset being the catalog base). On any change: update that buff's `BuffPref` in `_settings.BuffPrefs` (`Enabled`, or `DurationOverride` = the field value when it differs from the base, else null to clear the override), persist, and call `_buffInjector.SyncTo(_settings)` so the toggle/override injects or re-injects live.

- [ ] **Step 2: Document the expected line format.** Beside the toggles, show the **normalized log line** each buff expects as reference — single-target `eq2auras <buff> <target>`, group-wide `eq2auras <buff>` to **group chat**. *How* the raider emits a matching line (macro, `%T`, a click-to-cast target var, etc.) is theirs; we document the format, never a prescribed macro (SPEC §Buff tracking).

- [ ] **Step 3: CI compile + commit.**

```bash
git add -u
git commit -m "feat(plugin): per-buff enable toggles + published macro text"
git push
```

---

## Merge-gate live script (Alex, Windows box — the field gate)

Run after `dev-latest` picks up the branch build. **This carries both the buff verification AND the timer-regression pass** (per the association-model re-seat).

1. **Timer regression:** existing Panel A / Panel B timers appear, escalate, drain, and color exactly as before (trigger a known panel-1 and panel-2 timer). Confirm no display change to existing timers (or, if the general suffix shipped, that any caster suffix is acceptable — the Task-7 decision).
2. **Buff spawn (targeted):** with the Bolster macro set up, `/g eq2auras Bolster <target>` → a `Bolster — <Target>` row appears in the buff window for the buff's duration, title-cased.
3. **Buff spawn (group-wide):** `/g eq2auras Tortoise Shell` → a `Tortoise Shell — <Caster>` row appears (the caster captured from the chat line, title-cased).
4. **Toggle:** disable Bolster in the tab → its macro no longer spawns a row; re-enable → it works again (inject/withdraw live).
5. **Zone re-injection:** zone into a new area, re-cast a buff macro → the row still appears (the poll re-ensure survived the `RebuildActiveCustomTriggers`).
6. **Clean teardown:** toggle the plugin off / reload → ACT's Spell Timers list has no lingering `eq2auras Buffs` category entries.

---

## Data dependencies & open items (resolve before merge)

- **Durations — RESOLVED (census, 2026-08-19):** all 22 base durations are filled from the Daybreak census `spell` collection. Three tier-variable buffs (Tsunami, Adrenaline, Sanctuary) use the Grandmaster max; the per-buff **override** is the correction for characters running higher tiers.
- **Macro channel + the group-wide wrapper (Task 0):** confirm the exact EQ2 chat log-line format on the box — the **single-target** payload patterns are robust, but the **group-wide** caster capture depends on the wrapper shape (`\S+ says to the group, "…"`); adjust the group-wide patterns to the captured sample.
- **Display suffix rule (Task 7):** with group-wide buffs now capturing the caster, *every* buff renders `{Buff} — {Name}`; confirm at plan review that the general meaningful-combatant suffix (vs. buff-group-only) is acceptable for existing timers.

---

## Self-review

**Spec coverage:** association model (Tasks 2–3) ✓; `Category` reading (Task 1) ✓; bounded 22-buff catalog + per-buff regex, single-target payload capture *and* group-wide caster capture (Task 4) ✓; per-buff prefs (enabled + duration override) + effective-duration resolution + sync/sweep/zone-reinject (Task 5–6) ✓; transient two-object injection at the effective duration (Task 6) ✓; buff window display — `{Buff} — {Name}` title-cased, target or caster (Task 7) ✓; per-buff toggles + duration field + published macro (Task 8) ✓; three-group migration (Task 3) ✓; the reviewer plan-watch field gates (merge-gate script) ✓. The display-suffix rule (general vs buff-group-only) is the one spec-underspecified point, flagged as an open decision rather than silently chosen.

**Type consistency:** `SourceRule.Panel/OfCategory/OfName`, `SourceRules.Matches/MatchesAny`, `TimerReading.Category`, `PanelSettings.Sources`, `Settings.GroupCount==3`/`BuffPrefs`/`EnabledBuffIds`/`EffectiveDuration`, `BuffPref.{Id,Enabled,DurationOverride}`, `BuffDef.{Id,DisplayName,DurationSeconds,Pattern,IsTargeted,TryMatch}`, `BuffCatalog.{Category,All,Find}`, `BuffSync.{Desired,Plan}`/`BuffSyncPlan.{ToAdd,ToRemove}`, `BuffInjector.{SyncTo,EnsurePresent,TearDown}` — used consistently across tasks.

**Placeholder scan:** durations are resolved (census); no code-vagueness placeholders remain. The only field-confirmed item is the group-wide chat-wrapper shape (Task 0).
