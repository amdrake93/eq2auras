# Class Colors — Inference / Persistence / Coloring Pipeline — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Color meter rows by a combatant's EQ2 subclass, inferred from the ability names it casts, learned across sessions, at the row-fill seams the meter already owns.

**Architecture:** A pure Core identity layer (`Subclass`/`FinalClass` enums, `ClassTree`, `SubclassColors`, a compiled `ClassSignatures` catalog) feeds a stateful, thread-safe `ClassInferenceEngine` that holds a committed `name → {subclass, final?}` map. The Plugin's `EncounterProbe` gathers each *uncommitted ally's* cast ability-names (keys-only) under the existing lock; `OverlayHost` runs inference once per poll and passes a `name → ARGB` resolver into the four row-building engines. A DCJS `ClassCache` file warm-starts the map at init and is flushed (confident diffs) at encounter end.

**Tech Stack:** C# / .NET Standard 2.0 (Core), .NET Framework 4.7.2 + WPF (Plugin), xUnit (Core tests). Single-assembly packaging — the Plugin `<Compile Include>`s all Core sources.

## Global Constraints

Copied verbatim from the spec / repo CLAUDE.md — every task implicitly includes these:

- **Single shipped DLL.** Never reference a second assembly; new Core sources are auto-globbed into both `eq2auras.Core` and (via `<Compile Include="..\eq2auras.Core\**\*.cs">`) the Plugin. No non-GAC types in fields.
- **Never reference `System.Web.Extensions`.** JSON is `DataContractJsonSerializer` (DCJS) only.
- **DCJS skips field initializers on deserialize → every enum default must be the 0-value.** `Subclass.Unknown = 0`, `FinalClass.Unknown = 0`.
- **No `async` in the Plugin project.** The poll path is synchronous.
- **Core is Mac-testable (`dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`); the Plugin is Windows-only** — Plugin tasks are transcribe-only, verified by branch CI compile + Alex's on-box script, never Mac-run.
- **Read discipline:** all ACT reads happen briefly under `ActGlobals.oFormActMain.AfterCombatActionDataLock`, snapshot into Core DTOs, release. Combatant identity is **name-based, case-insensitive**.
- **Self-documenting code:** clear names, no trivial comments, K&R braces, early returns, `Func`/`Optional` where it clarifies; match surrounding style.
- Branch: `class-colors-pipeline` (already checked out). Commit format: `<description>` (this repo takes no ticket prefix). Never merge to `main` — present ready-for-review.

## Source of truth for the catalog

`spike-data/2026-07-27/signatures.md` — the ground-truth-pruned, census-validated catalog. Transcription rule (Task 3): a line's **STRONG** and **WEAK** names go under their **final** class; **SHARED** names go under their **subclass**; **CUT** names are excluded; `LOCK-IF-SEEN` / `SPEC` final-specific names are treated as STRONG (final-level). Premium every-cast procs are flagged (Task 4 `IsPremium`).

## File structure

**Core — new files** (`src/eq2auras.Core/Meter/`):
- `ClassIdentity.cs` — `Subclass` enum, `FinalClass` enum, `ClassTree` (Task 1).
- `SubclassColors.cs` — `Subclass → ARGB`, grey fallback (Task 2).
- `ClassSignatures.cs` — the compiled catalog + inverted lookup + collision guard (Task 3).
- `ClassInferenceEngine.cs` — committed map, commit/override, resolver, persistence export/import (Task 4).
- `ClassCache.cs` — DCJS record type + `Parse`/`ToJson` (Task 5).

**Core — modified:**
- `MeterReading.cs` — `CombatantReading.AbilityNames` (Task 6).
- `MeterFrame.cs` — `MeterRow.BackgroundArgb` (Task 6).
- `MeterEngine.cs` — Tick resolver param (Task 7).
- `BreakdownEngine.cs` — `colorForLabel` param (Task 8).
- `DeathsEngine.cs` — `colorForName` param (Task 9).
- `DeathRecapEngine.cs` — two-tone + `classArgb` param (Task 10).

**Plugin — modified:**
- `Act/EncounterProbe.cs` — ability-name gather (Task 11).
- `Overlay/OverlayHost.cs` + `Eq2AurasPlugin.cs` — inference wiring (Task 12).
- `SelfUpdate/ClassCacheStore.cs` (new) + `Eq2AurasPlugin.cs` — cache I/O (Task 13).
- `Overlay/BarRowVisual.cs` — name outline, two-tone bar, min-alpha floor (Task 14).

**Tests** (`tests/eq2auras.Core.Tests/`): one new test file per Core task.

---

# Phase 1 — Core identity + inference (Mac-testable, strict TDD)

### Task 1: Class identity — enums + tree

**Files:**
- Create: `src/eq2auras.Core/Meter/ClassIdentity.cs`
- Test: `tests/eq2auras.Core.Tests/ClassTreeTests.cs`

**Interfaces:**
- Produces: `enum Subclass` (12 + `Unknown=0`), `enum FinalClass` (24 + `Unknown=0`), `static Subclass ClassTree.SubclassOf(FinalClass)`.

- [ ] **Step 1: Write the failing test**

```csharp
using Eq2Auras.Core.Meter;
using Xunit;

public class ClassTreeTests
{
    [Theory]
    [InlineData(FinalClass.Paladin, Subclass.Crusader)]
    [InlineData(FinalClass.Shadowknight, Subclass.Crusader)]
    [InlineData(FinalClass.Monk, Subclass.Brawler)]
    [InlineData(FinalClass.Bruiser, Subclass.Brawler)]
    [InlineData(FinalClass.Guardian, Subclass.Warrior)]
    [InlineData(FinalClass.Berserker, Subclass.Warrior)]
    [InlineData(FinalClass.Templar, Subclass.Cleric)]
    [InlineData(FinalClass.Inquisitor, Subclass.Cleric)]
    [InlineData(FinalClass.Warden, Subclass.Druid)]
    [InlineData(FinalClass.Fury, Subclass.Druid)]
    [InlineData(FinalClass.Mystic, Subclass.Shaman)]
    [InlineData(FinalClass.Defiler, Subclass.Shaman)]
    [InlineData(FinalClass.Swashbuckler, Subclass.Rogue)]
    [InlineData(FinalClass.Brigand, Subclass.Rogue)]
    [InlineData(FinalClass.Troubador, Subclass.Bard)]
    [InlineData(FinalClass.Dirge, Subclass.Bard)]
    [InlineData(FinalClass.Ranger, Subclass.Predator)]
    [InlineData(FinalClass.Assassin, Subclass.Predator)]
    [InlineData(FinalClass.Wizard, Subclass.Sorcerer)]
    [InlineData(FinalClass.Warlock, Subclass.Sorcerer)]
    [InlineData(FinalClass.Conjuror, Subclass.Summoner)]
    [InlineData(FinalClass.Necromancer, Subclass.Summoner)]
    [InlineData(FinalClass.Illusionist, Subclass.Enchanter)]
    [InlineData(FinalClass.Coercer, Subclass.Enchanter)]
    public void Each_final_maps_to_its_subclass(FinalClass final, Subclass expected)
        => Assert.Equal(expected, ClassTree.SubclassOf(final));

    [Fact]
    public void Unknown_and_zero_defaults_hold()
    {
        Assert.Equal(0, (int)Subclass.Unknown);
        Assert.Equal(0, (int)FinalClass.Unknown);
        Assert.Equal(Subclass.Unknown, ClassTree.SubclassOf(FinalClass.Unknown));
    }
}
```

- [ ] **Step 2: Run — expect FAIL** (`Subclass`/`FinalClass`/`ClassTree` undefined).
Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter ClassTreeTests`

- [ ] **Step 3: Implement**

```csharp
using System.Collections.Generic;

namespace Eq2Auras.Core.Meter
{
    /// EQ2's class tree (SPEC Part III §Class colors): 4 archetypes → 12 subclasses → 24 finals.
    /// Color keys at the subclass; the final rides along as enrichment. Unknown = 0 (DCJS rule).
    public enum Subclass
    {
        Unknown = 0,
        Crusader, Brawler, Warrior,      // Fighter
        Cleric, Druid, Shaman,           // Priest
        Rogue, Bard, Predator,           // Scout
        Sorcerer, Summoner, Enchanter,   // Mage
    }

    public enum FinalClass
    {
        Unknown = 0,
        Paladin, Shadowknight,           // Crusader
        Monk, Bruiser,                   // Brawler
        Guardian, Berserker,             // Warrior
        Templar, Inquisitor,             // Cleric
        Warden, Fury,                    // Druid
        Mystic, Defiler,                 // Shaman
        Swashbuckler, Brigand,           // Rogue
        Troubador, Dirge,                // Bard
        Ranger, Assassin,                // Predator
        Wizard, Warlock,                 // Sorcerer
        Conjuror, Necromancer,           // Summoner
        Illusionist, Coercer,            // Enchanter
    }

    public static class ClassTree
    {
        private static readonly Dictionary<FinalClass, Subclass> Map = new Dictionary<FinalClass, Subclass>
        {
            { FinalClass.Paladin, Subclass.Crusader }, { FinalClass.Shadowknight, Subclass.Crusader },
            { FinalClass.Monk, Subclass.Brawler }, { FinalClass.Bruiser, Subclass.Brawler },
            { FinalClass.Guardian, Subclass.Warrior }, { FinalClass.Berserker, Subclass.Warrior },
            { FinalClass.Templar, Subclass.Cleric }, { FinalClass.Inquisitor, Subclass.Cleric },
            { FinalClass.Warden, Subclass.Druid }, { FinalClass.Fury, Subclass.Druid },
            { FinalClass.Mystic, Subclass.Shaman }, { FinalClass.Defiler, Subclass.Shaman },
            { FinalClass.Swashbuckler, Subclass.Rogue }, { FinalClass.Brigand, Subclass.Rogue },
            { FinalClass.Troubador, Subclass.Bard }, { FinalClass.Dirge, Subclass.Bard },
            { FinalClass.Ranger, Subclass.Predator }, { FinalClass.Assassin, Subclass.Predator },
            { FinalClass.Wizard, Subclass.Sorcerer }, { FinalClass.Warlock, Subclass.Sorcerer },
            { FinalClass.Conjuror, Subclass.Summoner }, { FinalClass.Necromancer, Subclass.Summoner },
            { FinalClass.Illusionist, Subclass.Enchanter }, { FinalClass.Coercer, Subclass.Enchanter },
        };

        public static Subclass SubclassOf(FinalClass final)
            => Map.TryGetValue(final, out var s) ? s : Subclass.Unknown;
    }
}
```

- [ ] **Step 4: Run — expect PASS.**
- [ ] **Step 5: Commit** — `git add src/eq2auras.Core/Meter/ClassIdentity.cs tests/eq2auras.Core.Tests/ClassTreeTests.cs && git commit -m "Class colors: Subclass/FinalClass enums + ClassTree"`

---

### Task 2: Subclass colors

**Files:**
- Create: `src/eq2auras.Core/Meter/SubclassColors.cs`
- Test: `tests/eq2auras.Core.Tests/SubclassColorsTests.cs`

**Interfaces:**
- Produces: `static int SubclassColors.ArgbFor(Subclass)`, `const int SubclassColors.Grey`.

- [ ] **Step 1: Write the failing test** (values are the locked palette, `docs/plans/2026-07-27-class-colors-palette.md`):

```csharp
using Eq2Auras.Core.Meter;
using Xunit;

public class SubclassColorsTests
{
    [Theory]
    [InlineData(Subclass.Crusader, unchecked((int)0xFFC9A227))]
    [InlineData(Subclass.Brawler, unchecked((int)0xFF00FF98))]
    [InlineData(Subclass.Warrior, unchecked((int)0xFFC69B6D))]
    [InlineData(Subclass.Cleric, unchecked((int)0xFFFFFFFF))]
    [InlineData(Subclass.Druid, unchecked((int)0xFFFF7C0A))]
    [InlineData(Subclass.Shaman, unchecked((int)0xFF0070DD))]
    [InlineData(Subclass.Rogue, unchecked((int)0xFFFFF468))]
    [InlineData(Subclass.Bard, unchecked((int)0xFF6C3FB5))]
    [InlineData(Subclass.Predator, unchecked((int)0xFFAAD372))]
    [InlineData(Subclass.Sorcerer, unchecked((int)0xFF3FC7EB))]
    [InlineData(Subclass.Summoner, unchecked((int)0xFF8788EE))]
    [InlineData(Subclass.Enchanter, unchecked((int)0xFF33937F))]
    public void Each_subclass_has_its_locked_color(Subclass s, int argb)
        => Assert.Equal(argb, SubclassColors.ArgbFor(s));

    [Fact]
    public void Unknown_is_neutral_grey()
    {
        Assert.Equal(unchecked((int)0xFF8B93A3), SubclassColors.ArgbFor(Subclass.Unknown));
        Assert.Equal(SubclassColors.Grey, SubclassColors.ArgbFor(Subclass.Unknown));
    }
}
```

- [ ] **Step 2: Run — expect FAIL.**
- [ ] **Step 3: Implement**

```csharp
using System.Collections.Generic;

namespace Eq2Auras.Core.Meter
{
    /// The locked 12-subclass palette (docs/plans/2026-07-27-class-colors-palette.md), the row-fill
    /// successor to MeterFamilyColors. Unknown/unclassed → neutral grey (SPEC Part III §Class colors).
    public static class SubclassColors
    {
        public const int Grey = unchecked((int)0xFF8B93A3);

        private static readonly Dictionary<Subclass, int> Map = new Dictionary<Subclass, int>
        {
            { Subclass.Crusader, unchecked((int)0xFFC9A227) },
            { Subclass.Brawler, unchecked((int)0xFF00FF98) },
            { Subclass.Warrior, unchecked((int)0xFFC69B6D) },
            { Subclass.Cleric, unchecked((int)0xFFFFFFFF) },
            { Subclass.Druid, unchecked((int)0xFFFF7C0A) },
            { Subclass.Shaman, unchecked((int)0xFF0070DD) },
            { Subclass.Rogue, unchecked((int)0xFFFFF468) },
            { Subclass.Bard, unchecked((int)0xFF6C3FB5) },
            { Subclass.Predator, unchecked((int)0xFFAAD372) },
            { Subclass.Sorcerer, unchecked((int)0xFF3FC7EB) },
            { Subclass.Summoner, unchecked((int)0xFF8788EE) },
            { Subclass.Enchanter, unchecked((int)0xFF33937F) },
        };

        public static int ArgbFor(Subclass subclass)
            => Map.TryGetValue(subclass, out var argb) ? argb : Grey;
    }
}
```

- [ ] **Step 4: Run — expect PASS.**
- [ ] **Step 5: Commit** — `git add src/eq2auras.Core/Meter/SubclassColors.cs tests/eq2auras.Core.Tests/SubclassColorsTests.cs && git commit -m "Class colors: SubclassColors palette"`

---

### Task 3: The signature catalog (transcription + collision guard)

**This is a data-transcription task. The source of truth is `spike-data/2026-07-27/signatures.md`. Work from it directly; the tests pin correctness.** (Plan-watch #3.)

**Files:**
- Create: `src/eq2auras.Core/Meter/ClassSignatures.cs`
- Test: `tests/eq2auras.Core.Tests/ClassSignaturesTests.cs`

**Interfaces:**
- Produces: `static bool ClassSignatures.TryResolve(string abilityName, out Subclass, out FinalClass)` (case-insensitive; `final == Unknown` for SHARED tells), `static IReadOnlyList<string> ClassSignatures.FindCrossSubclassCollisions()`, `static bool ClassSignatures.IsPremium(string abilityName)`.

- [ ] **Step 1: Write the failing tests** — the guard + presence + spot-checks + premiums:

```csharp
using System;
using System.Collections.Generic;
using Eq2Auras.Core.Meter;
using Xunit;

public class ClassSignaturesTests
{
    [Fact]
    public void No_name_resolves_to_two_subclasses()   // the load-bearing invariant (SPEC §Class colors)
        => Assert.Empty(ClassSignatures.FindCrossSubclassCollisions());

    [Theory]
    // Final-specific STRONG tells → both final and subclass.
    [InlineData("Lich's Siphoning", Subclass.Summoner, FinalClass.Necromancer)]
    [InlineData("Reaver's Mania", Subclass.Crusader, FinalClass.Shadowknight)]
    [InlineData("Consecration", Subclass.Crusader, FinalClass.Paladin)]
    [InlineData("Evade Blame", Subclass.Rogue, FinalClass.Swashbuckler)]
    [InlineData("Backstab", Subclass.Rogue, FinalClass.Brigand)]
    [InlineData("Fiery Annihilation", Subclass.Summoner, FinalClass.Conjuror)]
    [InlineData("Darksong Blade", Subclass.Bard, FinalClass.Dirge)]
    [InlineData("Chaos Anthem", Subclass.Bard, FinalClass.Troubador)]
    public void Final_tells_resolve_final_and_subclass(string name, Subclass sc, FinalClass fc)
    {
        Assert.True(ClassSignatures.TryResolve(name, out var gotSc, out var gotFc));
        Assert.Equal(sc, gotSc);
        Assert.Equal(fc, gotFc);
    }

    [Theory]
    // SHARED tells → subclass only, final Unknown.
    [InlineData("Interrupt", Subclass.Rogue)]
    [InlineData("Aura of Warding", Subclass.Shaman)]
    public void Shared_tells_resolve_subclass_only(string name, Subclass sc)
    {
        Assert.True(ClassSignatures.TryResolve(name, out var gotSc, out var gotFc));
        Assert.Equal(sc, gotSc);
        Assert.Equal(FinalClass.Unknown, gotFc);
    }

    [Fact]
    public void Case_insensitive()
        => Assert.True(ClassSignatures.TryResolve("lich's siphoning", out _, out _));

    [Theory]
    [InlineData("Ambush")]          // CUT — union spans multiple subclasses
    [InlineData("Healing Blanket")] // CUT — cloak proc
    [InlineData("Vampiric Requiem")]// CUT — cross-class proc
    public void Cut_names_do_not_resolve(string name)
        => Assert.False(ClassSignatures.TryResolve(name, out _, out _));

    [Theory]
    [InlineData("Lich's Siphoning")]
    [InlineData("Reaver's Mania")]
    [InlineData("Lunar Attendant's Oracle's Blessing")]
    [InlineData("Spiritual Circle")]
    public void Premium_procs_are_flagged(string name)
        => Assert.True(ClassSignatures.IsPremium(name));

    [Fact]
    public void Every_subclass_and_final_has_at_least_one_signature()
    {
        var subclassesSeen = new HashSet<Subclass>();
        var finalsSeen = new HashSet<FinalClass>();
        foreach (var name in ClassSignatures.AllNames)   // exposed for the completeness check
        {
            ClassSignatures.TryResolve(name, out var sc, out var fc);
            subclassesSeen.Add(sc);
            if (fc != FinalClass.Unknown) finalsSeen.Add(fc);
        }
        foreach (Subclass s in Enum.GetValues(typeof(Subclass)))
            if (s != Subclass.Unknown) Assert.Contains(s, subclassesSeen);
        foreach (FinalClass f in Enum.GetValues(typeof(FinalClass)))
            if (f != FinalClass.Unknown) Assert.Contains(f, finalsSeen);
    }
}
```

- [ ] **Step 2: Run — expect FAIL.**

- [ ] **Step 3: Implement — the structure, then transcribe all of `signatures.md`.**

The class holds two authored dictionaries and inverts them once. **Transcribe every non-CUT name from `spike-data/2026-07-27/signatures.md`:** STRONG + WEAK + LOCK-IF-SEEN + final-specific SPEC names under their `FinalClass`; SHARED names under their `Subclass`. Two finals fully worked below as the pattern — transcribe the remaining 22 finals / 12 subclass-shared lists identically.

```csharp
using System;
using System.Collections.Generic;

namespace Eq2Auras.Core.Meter
{
    /// The compiled ability-name → class catalog (SPEC Part III §Class colors), authored
    /// subclass-first to mirror spike-data/2026-07-27/signatures.md: STRONG/WEAK names under
    /// their FinalClass, SHARED names under their Subclass, CUT excluded. Inverted at static
    /// init to a case-insensitive name→{subclass, final?} lookup, guarded so no name resolves
    /// to two subclasses. Names-only union model — the log carries a name, never a spell id.
    public static class ClassSignatures
    {
        // STRONG/WEAK — final-specific tells. Transcribe ALL 24 finals from signatures.md.
        private static readonly Dictionary<FinalClass, string[]> ByFinal = new Dictionary<FinalClass, string[]>
        {
            { FinalClass.Swashbuckler, new[] {
                "Evade Blame", "Flurry of Blades", "Snap of the Wrist", "Kidney Stab", "Flash of Steel",
                "Hamstring", "Lucky Gambit", "Dashing Swathe", "Razor Edge", "Viscerate", "Lung Puncture",
                "Storm of Steel", "Flamboyant Strike", "Daring Attack", "Arctic Blast" } },
            { FinalClass.Brigand, new[] {
                "Backstab", "Shank", "Baffle", "Puncture", "Battery and Assault", "Bum Rush",
                "Barroom Negotiation", "Desperate Thrust", "Stunning Blow", "Gouge", "Black Jack",
                "Dispatch", "Murderous Rake", "Double Blast", "Debilitate" } },
            // … transcribe the remaining 22 finals identically (Necromancer incl. "Lich's Siphoning";
            // Shadowknight incl. "Reaver's Mania"; Conjuror, Paladin, Troubador, Dirge, Monk, Bruiser,
            // Guardian, Berserker, Templar, Inquisitor incl. its melee "Strike of X", Warden, Fury,
            // Mystic, Defiler, Wizard, Warlock, Ranger, Assassin, Illusionist, Coercer).
        };

        // SHARED — subclass-level tells (both finals cast). Transcribe ALL 12 subclasses' SHARED lists.
        private static readonly Dictionary<Subclass, string[]> BySubclass = new Dictionary<Subclass, string[]>
        {
            { Subclass.Rogue, new[] {
                "Interrupt", "Pirate Stab", "Traumatic Swipe", "Walk the Plank", "Shadow Slip",
                "Boot Dagger", "Torporous Strike" } },
            // … transcribe the remaining 11 subclass SHARED lists identically.
        };

        // ★PREMIUM every-cast procs — class-unique, fire constantly (signatures.md METHOD NOTES).
        private static readonly HashSet<string> Premium = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Lich's Siphoning", "Reaver's Mania", "Lunar Attendant's Oracle's Blessing", "Spiritual Circle",
        };

        private struct Record { public Subclass Subclass; public FinalClass Final; }

        private static readonly Dictionary<string, Record> Lookup = BuildLookup(out _crossSubclassCollisions);
        private static readonly List<string> _crossSubclassCollisions;

        public static IReadOnlyList<string> FindCrossSubclassCollisions() => _crossSubclassCollisions;
        public static IEnumerable<string> AllNames => Lookup.Keys;

        public static bool IsPremium(string abilityName)
            => abilityName != null && Premium.Contains(abilityName);

        public static bool TryResolve(string abilityName, out Subclass subclass, out FinalClass final)
        {
            subclass = Subclass.Unknown;
            final = FinalClass.Unknown;
            if (abilityName == null || !Lookup.TryGetValue(abilityName, out var rec)) return false;
            subclass = rec.Subclass;
            final = rec.Final;
            return true;
        }

        private static Dictionary<string, Record> BuildLookup(out List<string> collisions)
        {
            var map = new Dictionary<string, Record>(StringComparer.OrdinalIgnoreCase);
            var collided = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            collisions = new List<string>();

            foreach (var pair in ByFinal)
            {
                var final = pair.Key;
                var subclass = ClassTree.SubclassOf(final);
                foreach (var name in pair.Value)
                    Add(map, collided, collisions, name, subclass, final);
            }
            foreach (var pair in BySubclass)
                foreach (var name in pair.Value)
                    Add(map, collided, collisions, name, pair.Key, FinalClass.Unknown);
            return map;
        }

        private static void Add(Dictionary<string, Record> map, HashSet<string> collided,
            List<string> collisions, string name, Subclass subclass, FinalClass final)
        {
            if (map.TryGetValue(name, out var existing))
            {
                if (existing.Subclass != subclass)   // cross-subclass collision — a catalog bug
                {
                    if (collided.Add(name)) collisions.Add(name);
                    return;
                }
                // Same subclass, two finals → a subclass-shared name: demote final to Unknown.
                if (existing.Final != final)
                    map[name] = new Record { Subclass = subclass, Final = FinalClass.Unknown };
                return;
            }
            map[name] = new Record { Subclass = subclass, Final = final };
        }
    }
}
```

- [ ] **Step 4: Run — expect PASS.** If `FindCrossSubclassCollisions` is non-empty, the transcription introduced a real cross-subclass name (per the census union model it should be CUT or SHARED) — fix the transcription, do not weaken the guard.
- [ ] **Step 5: Commit** — `git add src/eq2auras.Core/Meter/ClassSignatures.cs tests/eq2auras.Core.Tests/ClassSignaturesTests.cs && git commit -m "Class colors: ClassSignatures catalog + collision guard"`

---

### Task 4: The inference engine

**Files:**
- Create: `src/eq2auras.Core/Meter/ClassCacheEntry.cs` (the export/persist DTO — defined here so this task compiles standalone; Task 5's `ClassCache` container consumes it)
- Create: `src/eq2auras.Core/Meter/ClassInferenceEngine.cs`
- Test: `tests/eq2auras.Core.Tests/ClassInferenceEngineTests.cs`

**Interfaces:**
- Consumes: `ClassSignatures`, `SubclassColors`, `ClassTree`.
- Produces: `[DataContract] class ClassCacheEntry { string Name; Subclass Subclass; FinalClass Final; }`; `class ClassInferenceEngine` with `void Observe(string name, IReadOnlyList<string> abilityNames)`, `bool IsCommitted(string name)` (**confirmed *this encounter*** — the read-skip predicate; a warm-started name is colored but not committed), `int ColorForName(string name)` (immediate warm-start color), `void ResetEncounter()` (clear the per-encounter confirmation set — the Plugin calls it at each encounter start so a between-fight persona swap is re-read), `IReadOnlyList<ClassCacheEntry> Export()`, `void Import(IEnumerable<ClassCacheEntry> entries)` (colors only, unconfirmed), `bool HasDirty { get; }`, `void ClearDirty()`. All members are thread-safe (an internal lock — `IsCommitted` is read on ACT's UI thread; `Observe`/`ColorForName`/`ResetEncounter` on the WPF dispatcher thread).

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Collections.Generic;
using Eq2Auras.Core.Meter;
using Xunit;

public class ClassInferenceEngineTests
{
    private static readonly int Grey = SubclassColors.Grey;

    [Fact]
    public void Unseen_name_is_grey_and_uncommitted()
    {
        var e = new ClassInferenceEngine();
        Assert.False(e.IsCommitted("Aeralik"));
        Assert.Equal(Grey, e.ColorForName("Aeralik"));
    }

    [Fact]
    public void First_final_hit_commits_subclass_and_final()
    {
        var e = new ClassInferenceEngine();
        e.Observe("Aeralik", new[] { "Autoattack", "Lich's Siphoning" });
        Assert.True(e.IsCommitted("Aeralik"));
        Assert.Equal(SubclassColors.ArgbFor(Subclass.Summoner), e.ColorForName("Aeralik"));
    }

    [Fact]
    public void Case_insensitive_name_keying()
    {
        var e = new ClassInferenceEngine();
        e.Observe("Aeralik", new[] { "Lich's Siphoning" });
        Assert.True(e.IsCommitted("AERALIK"));
        Assert.Equal(SubclassColors.ArgbFor(Subclass.Summoner), e.ColorForName("aeralik"));
    }

    [Fact]
    public void Unknown_fight_never_demotes_a_commit()
    {
        var e = new ClassInferenceEngine();
        e.Observe("Bob", new[] { "Reaver's Mania" });          // Crusader
        e.Observe("Bob", new[] { "Autoattack", "Bandage" });   // no catalog hit
        Assert.Equal(SubclassColors.ArgbFor(Subclass.Crusader), e.ColorForName("Bob"));
    }

    [Fact]
    public void Confident_disagreement_overrides_live()        // persona / betrayal-final / misguess
    {
        var e = new ClassInferenceEngine();
        e.Observe("Bob", new[] { "Reaver's Mania" });          // Crusader
        e.Observe("Bob", new[] { "Chromatic Shower" });        // Illusionist → Enchanter
        Assert.Equal(SubclassColors.ArgbFor(Subclass.Enchanter), e.ColorForName("Bob"));
    }

    [Fact]
    public void Premium_hit_wins_within_a_call()
    {
        var e = new ClassInferenceEngine();
        // A shared/non-premium Rogue hit alongside a premium Necromancer proc in one list → premium wins.
        e.Observe("Bob", new[] { "Interrupt", "Lich's Siphoning" });
        Assert.Equal(SubclassColors.ArgbFor(Subclass.Summoner), e.ColorForName("Bob"));
    }

    [Fact]
    public void Committed_stays_committed_dirty_tracks_changes()
    {
        var e = new ClassInferenceEngine();
        Assert.False(e.HasDirty);
        e.Observe("Bob", new[] { "Reaver's Mania" });
        Assert.True(e.HasDirty);
        e.ClearDirty();
        e.Observe("Bob", new[] { "Reaver's Mania" });          // same subclass again → no new dirt
        Assert.False(e.HasDirty);
    }

    [Fact]
    public void Warmstart_import_colors_immediately_but_is_not_committed()
    {
        var source = new ClassInferenceEngine();
        source.Observe("Bob", new[] { "Lich's Siphoning" });   // Summoner
        var e = new ClassInferenceEngine();
        e.Import(source.Export());
        Assert.False(e.IsCommitted("Bob"));                    // still re-read this session (persona guard)
        Assert.Equal(SubclassColors.ArgbFor(Subclass.Summoner), e.ColorForName("Bob"));   // but colored now
    }

    [Fact]
    public void Reset_encounter_reopens_reads_but_keeps_color()
    {
        var e = new ClassInferenceEngine();
        e.Observe("Bob", new[] { "Reaver's Mania" });          // Crusader, confirmed this encounter
        Assert.True(e.IsCommitted("Bob"));
        e.ResetEncounter();
        Assert.False(e.IsCommitted("Bob"));                    // re-read next encounter
        Assert.Equal(SubclassColors.ArgbFor(Subclass.Crusader), e.ColorForName("Bob"));   // color survives
    }

    [Fact]
    public void Persona_swap_between_encounters_overrides()
    {
        var e = new ClassInferenceEngine();
        e.Observe("Bob", new[] { "Reaver's Mania" });          // Crusader
        e.ResetEncounter();                                    // Bob relogs as another class between fights
        e.Observe("Bob", new[] { "Chromatic Shower" });        // Illusionist → Enchanter
        Assert.Equal(SubclassColors.ArgbFor(Subclass.Enchanter), e.ColorForName("Bob"));
    }
}
```

- [ ] **Step 2: Run — expect FAIL.**

- [ ] **Step 3a: Define the export DTO** — `src/eq2auras.Core/Meter/ClassCacheEntry.cs` (Unknown enum defaults at the 0-value; the `[DataContract]` shape Task 5's `ClassCache` serializes):

```csharp
using System.Runtime.Serialization;

namespace Eq2Auras.Core.Meter
{
    [DataContract]
    public sealed class ClassCacheEntry
    {
        [DataMember] public string Name { get; set; }
        [DataMember] public Subclass Subclass { get; set; }
        [DataMember] public FinalClass Final { get; set; }
    }
}
```

- [ ] **Step 3b: Implement the engine:**

```csharp
using System.Collections.Generic;

namespace Eq2Auras.Core.Meter
{
    /// The learned name→class map + inference rules (SPEC Part III §Class colors). The disk cache
    /// warm-starts the COLOR (shown immediately); a name is re-read each encounter until THIS encounter
    /// confirms it (first catalog hit) — so a between-fight persona swap is caught, while within a fight
    /// a confirmed combatant is skipped (the read shrinks toward zero). First hit commits (premium procs
    /// win within a call); confident disagreement overrides live + persists; an unknown fight never
    /// demotes. Thread-safe: IsCommitted runs on ACT's UI thread during the snapshot;
    /// Observe/ColorForName/ResetEncounter on the WPF dispatcher thread.
    public sealed class ClassInferenceEngine
    {
        private struct Record { public Subclass Subclass; public FinalClass Final; }

        private readonly object _gate = new object();
        private readonly Dictionary<string, Record> _map =
            new Dictionary<string, Record>(System.StringComparer.OrdinalIgnoreCase);              // the color (warm-started + learned)
        private readonly HashSet<string> _confirmedThisEncounter =
            new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);                         // drives the read-skip; reset each encounter
        private readonly HashSet<string> _dirty =
            new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        public bool HasDirty { get { lock (_gate) return _dirty.Count > 0; } }
        public void ClearDirty() { lock (_gate) _dirty.Clear(); }

        /// Reopen every combatant to re-read next encounter (a between-fight persona swap is caught);
        /// the warm-start colors survive. Called by the Plugin at each encounter start.
        public void ResetEncounter() { lock (_gate) _confirmedThisEncounter.Clear(); }

        /// The read-skip predicate (SPEC §Class colors — "committed combatants are skipped"): confirmed
        /// THIS encounter. A warm-started-but-unconfirmed name returns false, so it is still read.
        public bool IsCommitted(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            lock (_gate) return _confirmedThisEncounter.Contains(name);
        }

        /// The color — from this-session evidence OR the warm-start cache — shown immediately.
        public int ColorForName(string name)
        {
            if (string.IsNullOrEmpty(name)) return SubclassColors.Grey;
            lock (_gate)
                return _map.TryGetValue(name, out var rec)
                    ? SubclassColors.ArgbFor(rec.Subclass)
                    : SubclassColors.Grey;
        }

        public void Observe(string name, IReadOnlyList<string> abilityNames)
        {
            if (string.IsNullOrEmpty(name) || abilityNames == null) return;

            bool found = false, foundPremium = false;
            Subclass subclass = Subclass.Unknown;
            FinalClass final = FinalClass.Unknown;
            foreach (var ability in abilityNames)
            {
                if (!ClassSignatures.TryResolve(ability, out var sc, out var fc)) continue;
                bool premium = ClassSignatures.IsPremium(ability);
                if (!found || (premium && !foundPremium))
                {
                    subclass = sc;
                    final = fc;
                    foundPremium = premium;
                    found = true;
                    if (premium) break;   // premium is definitive — stop scanning
                }
            }
            if (found) Commit(name, subclass, final);
        }

        private void Commit(string name, Subclass subclass, FinalClass final)
        {
            lock (_gate)
            {
                _confirmedThisEncounter.Add(name);   // confirmed this encounter → skip the rest of this fight
                if (_map.TryGetValue(name, out var existing) && existing.Subclass == subclass)
                {
                    if (existing.Final == FinalClass.Unknown && final != FinalClass.Unknown)
                    {
                        _map[name] = new Record { Subclass = subclass, Final = final };
                        _dirty.Add(name);
                    }
                    return;
                }
                _map[name] = new Record { Subclass = subclass, Final = final };   // new or confident override
                _dirty.Add(name);
            }
        }

        public IReadOnlyList<ClassCacheEntry> Export()
        {
            lock (_gate)
            {
                var list = new List<ClassCacheEntry>(_map.Count);
                foreach (var pair in _map)
                    list.Add(new ClassCacheEntry { Name = pair.Key, Subclass = pair.Value.Subclass, Final = pair.Value.Final });
                return list;
            }
        }

        public void Import(IEnumerable<ClassCacheEntry> entries)
        {
            if (entries == null) return;
            lock (_gate)
                foreach (var entry in entries)
                    if (!string.IsNullOrEmpty(entry.Name) && entry.Subclass != Subclass.Unknown)
                        _map[entry.Name] = new Record { Subclass = entry.Subclass, Final = entry.Final };
            // Color only — NOT confirmed (so this session still reads and can override a persona swap)
            // and NOT dirty (came from disk).
        }
    }
}
```

- [ ] **Step 4: Run — expect PASS.**
- [ ] **Step 5: Commit** — `git add src/eq2auras.Core/Meter/ClassCacheEntry.cs src/eq2auras.Core/Meter/ClassInferenceEngine.cs tests/eq2auras.Core.Tests/ClassInferenceEngineTests.cs && git commit -m "Class colors: ClassInferenceEngine (commit/override/resolver) + cache entry DTO"`

---

### Task 5: The learned-cache DCJS type

**Files:**
- Create: `src/eq2auras.Core/Meter/ClassCache.cs`
- Test: `tests/eq2auras.Core.Tests/ClassCacheTests.cs`

**Interfaces:**
- Consumes: `ClassCacheEntry` (Task 4).
- Produces: `[DataContract] class ClassCache { List<ClassCacheEntry> Entries; static ClassCache Parse(string json); string ToJson(); }`.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Collections.Generic;
using Eq2Auras.Core.Meter;
using Xunit;

public class ClassCacheTests
{
    [Fact]
    public void Roundtrips_through_dcjs()
    {
        var cache = new ClassCache { Entries = new List<ClassCacheEntry>
        {
            new ClassCacheEntry { Name = "Aeralik", Subclass = Subclass.Summoner, Final = FinalClass.Necromancer },
            new ClassCacheEntry { Name = "Bob", Subclass = Subclass.Cleric, Final = FinalClass.Unknown },
        }};
        var back = ClassCache.Parse(cache.ToJson());
        Assert.Equal(2, back.Entries.Count);
        Assert.Equal("Aeralik", back.Entries[0].Name);
        Assert.Equal(Subclass.Summoner, back.Entries[0].Subclass);
        Assert.Equal(FinalClass.Necromancer, back.Entries[0].Final);
        Assert.Equal(FinalClass.Unknown, back.Entries[1].Final);
    }

    [Fact]
    public void Corrupt_or_empty_json_yields_empty_cache()
    {
        Assert.Empty(ClassCache.Parse("not json").Entries);
        Assert.Empty(ClassCache.Parse("").Entries);
    }
}
```

- [ ] **Step 2: Run — expect FAIL.**
- [ ] **Step 3: Implement** (mirrors `Settings.Parse`/`ToJson`, `src/eq2auras.Core/Config/Settings.cs:108-138`):

```csharp
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Eq2Auras.Core.Meter
{
    /// The persisted learned name→class store (SPEC Part III §Class colors, §Settings): its own DCJS
    /// file, eager-loaded at init, flushed with confident diffs at encounter end. Enum defaults at
    /// the 0-value (DCJS skips initializers). An Unknown subclass is never written (ClassInferenceEngine).
    /// `ClassCacheEntry` (Task 4) is the per-record shape.
    [DataContract]
    public sealed class ClassCache
    {
        [DataMember] public List<ClassCacheEntry> Entries { get; set; } = new List<ClassCacheEntry>();

        public static ClassCache Parse(string json)
        {
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(ClassCache));
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json ?? "")))
                {
                    var cache = (ClassCache)serializer.ReadObject(stream) ?? new ClassCache();
                    if (cache.Entries == null) cache.Entries = new List<ClassCacheEntry>();
                    return cache;
                }
            }
            catch
            {
                return new ClassCache();
            }
        }

        public string ToJson()
        {
            var serializer = new DataContractJsonSerializer(typeof(ClassCache));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, this);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
    }
}
```

- [ ] **Step 4: Run — expect PASS** (and re-run Task 4's tests — now `ClassCacheEntry` resolves).
- [ ] **Step 5: Commit** — `git add src/eq2auras.Core/Meter/ClassCache.cs tests/eq2auras.Core.Tests/ClassCacheTests.cs && git commit -m "Class colors: ClassCache DCJS persistence type"`

---

### Task 6: DTO additions — `CombatantReading.AbilityNames` + `MeterRow.BackgroundArgb`

**Files:**
- Modify: `src/eq2auras.Core/Meter/MeterReading.cs` (add to `CombatantReading`)
- Modify: `src/eq2auras.Core/Meter/MeterFrame.cs` (add to `MeterRow`)
- Test: `tests/eq2auras.Core.Tests/MeterRowShapeTests.cs`

**Interfaces:**
- Produces: `CombatantReading.AbilityNames` (`List<string>`, null when not gathered), `MeterRow.BackgroundArgb` (`int?`, null → single-tone fill; set → two-tone).

- [ ] **Step 1: Write the failing test**

```csharp
using Eq2Auras.Core.Meter;
using Xunit;

public class MeterRowShapeTests
{
    [Fact]
    public void CombatantReading_carries_ability_names()
    {
        var r = new CombatantReading { Name = "Bob", AbilityNames = new System.Collections.Generic.List<string> { "Lich's Siphoning" } };
        Assert.Single(r.AbilityNames);
    }

    [Fact]
    public void MeterRow_background_defaults_null()
    {
        Assert.Null(new MeterRow().BackgroundArgb);
        Assert.Equal(1, new MeterRow { BackgroundArgb = 1 }.BackgroundArgb);
    }
}
```

- [ ] **Step 2: Run — expect FAIL.**
- [ ] **Step 3: Implement.** In `MeterReading.cs`, add to `CombatantReading` (after `IsAlly`):

```csharp
        public System.Collections.Generic.List<string> AbilityNames { get; set; }   // uncommitted allies only; the class-inference read (SPEC §Class colors); null otherwise
```

In `MeterFrame.cs`, add to `MeterRow` (after `FillArgb`):

```csharp
        public int? BackgroundArgb { get; set; }   // null → single-tone fill; set → two-tone (Death Recap: class-color ground behind the current-HP bar, SPEC §Class colors)
```

- [ ] **Step 4: Run — expect PASS.**
- [ ] **Step 5: Commit** — `git add src/eq2auras.Core/Meter/MeterReading.cs src/eq2auras.Core/Meter/MeterFrame.cs tests/eq2auras.Core.Tests/MeterRowShapeTests.cs && git commit -m "Class colors: CombatantReading.AbilityNames + MeterRow.BackgroundArgb"`

---

### Task 7: `MeterEngine.Tick` — class-color row fill

**Files:**
- Modify: `src/eq2auras.Core/Meter/MeterEngine.cs:101` (+ signature)
- Test: `tests/eq2auras.Core.Tests/MeterEngineClassColorTests.cs`

**Interfaces:**
- Consumes: a `System.Func<string, int> classColorForName` resolver (the Plugin passes `inference.ColorForName`).
- Produces: `MeterEngine.Tick(EncounterReading, List<CombatantReading>, string metricKey, string secondaryKey = null, MeterScope scope = MeterScope.Allies, System.Func<string,int> classColorForName = null)` — `row.FillArgb` is the resolver's result (grey when null/unknown).

- [ ] **Step 1: Write the failing test**

```csharp
using System.Collections.Generic;
using Eq2Auras.Core.Meter;
using Xunit;

public class MeterEngineClassColorTests
{
    [Fact]
    public void Row_fill_comes_from_the_class_resolver()
    {
        var engine = new MeterEngine();
        var enc = new EncounterReading { Exists = true, Active = true, LiveDurationSeconds = 10 };
        var combatants = new List<CombatantReading>
        {
            new CombatantReading { Name = "Bob", Damage = 100, IsAlly = true },
        };
        int purple = SubclassColors.ArgbFor(Subclass.Summoner);
        var frame = engine.Tick(enc, combatants, "encdps", null, MeterScope.Allies,
            name => name == "Bob" ? purple : SubclassColors.Grey);
        Assert.Equal(purple, frame.Rows[0].FillArgb);
    }

    [Fact]
    public void Null_resolver_falls_back_to_grey()
    {
        var engine = new MeterEngine();
        var enc = new EncounterReading { Exists = true, Active = true, LiveDurationSeconds = 10 };
        var combatants = new List<CombatantReading> { new CombatantReading { Name = "Bob", Damage = 100, IsAlly = true } };
        var frame = engine.Tick(enc, combatants, "encdps");
        Assert.Equal(SubclassColors.Grey, frame.Rows[0].FillArgb);
    }
}
```

- [ ] **Step 2: Run — expect FAIL** (Tick has no resolver param).
- [ ] **Step 3: Implement.** Change the `Tick` signature to add the trailing param, and replace line 101:

Signature (line 13-14):
```csharp
        public MeterFrame Tick(EncounterReading encounter, List<CombatantReading> combatants,
            string metricKey, string secondaryKey = null, MeterScope scope = MeterScope.Allies,
            System.Func<string, int> classColorForName = null)
```

Line 101 (`row.FillArgb = MeterFamilyColors.ArgbFor(metric.Category);`) →
```csharp
                row.FillArgb = classColorForName != null ? classColorForName(row.Name) : SubclassColors.Grey;
```

- [ ] **Step 4: Run — expect PASS.** Re-run the full Core suite: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj` — existing `MeterEngine` tests still pass (the new param defaults to null → grey; assert any existing test that checked the old family fill is updated to expect grey or passes a resolver).
- [ ] **Step 5: Commit** — `git add src/eq2auras.Core/Meter/MeterEngine.cs tests/eq2auras.Core.Tests/MeterEngineClassColorTests.cs && git commit -m "Class colors: MeterEngine row fill from the class resolver"`

---

### Task 8: `BreakdownEngine.Build` — per-label color

**Files:**
- Modify: `src/eq2auras.Core/Meter/BreakdownEngine.cs:13,44`
- Test: `tests/eq2auras.Core.Tests/BreakdownEngineClassColorTests.cs`

**Interfaces:**
- Produces: `BreakdownEngine.Build(IReadOnlyList<BreakdownEntry> entries, MetricDef metric, double durationSeconds, System.Func<string,int> colorForLabel = null)`. Drill callers pass a constant (`_ => drilledColor`); the by-counterpart hover passes `label => inference.ColorForName(label)`.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Collections.Generic;
using Eq2Auras.Core.Meter;
using Xunit;

public class BreakdownEngineClassColorTests
{
    private static readonly MetricDef Dps = MetricRegistry.Resolve("encdps");

    [Fact]
    public void Each_row_fill_comes_from_color_for_label()
    {
        var entries = new List<BreakdownEntry>
        {
            new BreakdownEntry { Label = "Alice", Value = 10 },
            new BreakdownEntry { Label = "Bob", Value = 5 },
        };
        int red = 111, blue = 222;
        var rows = BreakdownEngine.Build(entries, Dps, 1.0, label => label == "Alice" ? red : blue);
        Assert.Equal(red, rows.Find(r => r.Name == "Alice").FillArgb);
        Assert.Equal(blue, rows.Find(r => r.Name == "Bob").FillArgb);
    }

    [Fact]
    public void Constant_resolver_paints_every_drill_row_one_color()
    {
        var entries = new List<BreakdownEntry> { new BreakdownEntry { Label = "Fireball", Value = 10 }, new BreakdownEntry { Label = "Ice", Value = 5 } };
        int purple = SubclassColors.ArgbFor(Subclass.Sorcerer);
        var rows = BreakdownEngine.Build(entries, Dps, 1.0, _ => purple);
        Assert.All(rows, r => Assert.Equal(purple, r.FillArgb));
    }
}
```

- [ ] **Step 2: Run — expect FAIL.**
- [ ] **Step 3: Implement.** Signature (line 13):
```csharp
        public static List<MeterRow> Build(IReadOnlyList<BreakdownEntry> entries, MetricDef metric, double durationSeconds, System.Func<string, int> colorForLabel = null)
```
Line 44 (`row.FillArgb = MeterFamilyColors.ArgbFor(metric.Category);`) →
```csharp
                row.FillArgb = colorForLabel != null ? colorForLabel(row.Name) : SubclassColors.Grey;
```

- [ ] **Step 4: Run — expect PASS** (+ full Core suite; update any existing BreakdownEngine test that expected the family fill).
- [ ] **Step 5: Commit** — `git add src/eq2auras.Core/Meter/BreakdownEngine.cs tests/eq2auras.Core.Tests/BreakdownEngineClassColorTests.cs && git commit -m "Class colors: BreakdownEngine per-label fill (drill + hover)"`

---

### Task 9: `DeathsEngine.BuildList` — per-victim color

**Files:**
- Modify: `src/eq2auras.Core/Meter/DeathsEngine.cs:14,17,42`
- Test: `tests/eq2auras.Core.Tests/DeathsEngineClassColorTests.cs`

**Interfaces:**
- Produces: `DeathsEngine.BuildList(IReadOnlyList<DeathRecord> deaths, double durationSeconds, System.Func<string,int> colorForName = null)` — each row's `FillArgb` is `colorForName(victim)`.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Collections.Generic;
using Eq2Auras.Core.Meter;
using Xunit;

public class DeathsEngineClassColorTests
{
    [Fact]
    public void Death_row_fill_is_the_victims_class_color()
    {
        var deaths = new List<DeathRecord>
        {
            new DeathRecord { Victim = "Bob", Ordinal = 1, TimeOfDeathSeconds = 5, DrillKey = "Bob#1" },
        };
        int purple = SubclassColors.ArgbFor(Subclass.Summoner);
        var frame = DeathsEngine.BuildList(deaths, 10, name => name == "Bob" ? purple : SubclassColors.Grey);
        Assert.Equal(purple, frame.Rows[0].FillArgb);
    }
}
```

- [ ] **Step 2: Run — expect FAIL.**
- [ ] **Step 3: Implement.** Signature (line 14):
```csharp
        public static MeterFrame BuildList(IReadOnlyList<DeathRecord> deaths, double durationSeconds, System.Func<string, int> colorForName = null)
```
Delete the `int fill = MeterFamilyColors.ArgbFor(Category);` line (17). In the row build (line 42), replace `FillArgb = fill,` with:
```csharp
                        FillArgb = colorForName != null ? colorForName(d.Victim) : SubclassColors.Grey,
```
The `private const string Category = "Damage";` (line 12) is now unused → remove it.

- [ ] **Step 4: Run — expect PASS** (+ full Core suite; update existing DeathsEngine tests expecting the red fill).
- [ ] **Step 5: Commit** — `git add src/eq2auras.Core/Meter/DeathsEngine.cs tests/eq2auras.Core.Tests/DeathsEngineClassColorTests.cs && git commit -m "Class colors: DeathsEngine per-victim fill"`

---

### Task 10: `DeathRecapEngine.Build` — two-tone bar

**Files:**
- Modify: `src/eq2auras.Core/Meter/DeathRecapEngine.cs:14,16,55`
- Test: `tests/eq2auras.Core.Tests/DeathRecapEngineTwoToneTests.cs`

**Interfaces:**
- Produces: `DeathRecapEngine.Build(RecapReading reading, int classArgb)`; each row: `BackgroundArgb = classArgb` (the victim's color, full-width ground), `FillArgb = DeathRecapEngine.CurrentHpArgb` (dark bar), `BarFraction = hp%`. `public const int CurrentHpArgb` (dark slate).

- [ ] **Step 1: Write the failing test**

```csharp
using System.Collections.Generic;
using Eq2Auras.Core.Meter;
using Xunit;

public class DeathRecapEngineTwoToneTests
{
    [Fact]
    public void Recap_rows_are_two_tone_class_ground_dark_hp_bar()
    {
        var reading = new RecapReading
        {
            DrillKey = "Bob#1",
            MaxHealthEstimate = 1000,
            Events = new List<RecapEvent>
            {
                new RecapEvent { SecondsBeforeDeath = 2, Amount = 300, IsHeal = false },
                new RecapEvent { SecondsBeforeDeath = 1, Amount = 400, IsHeal = false },
                new RecapEvent { SecondsBeforeDeath = 0, Amount = 500, IsHeal = false },
            },
        };
        int purple = SubclassColors.ArgbFor(Subclass.Summoner);
        var rows = DeathRecapEngine.Build(reading, purple);
        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Equal(purple, r.BackgroundArgb));
        Assert.All(rows, r => Assert.Equal(DeathRecapEngine.CurrentHpArgb, r.FillArgb));
    }
}
```

- [ ] **Step 2: Run — expect FAIL.**
- [ ] **Step 3: Implement.** Replace the static `FillArgb` field (line 14) with a public dark current-HP constant, and add the `classArgb` param:

Line 13-14 region →
```csharp
        private const int WindowSeconds = 10;
        public const int CurrentHpArgb = unchecked((int)0xFF2B2F3A);   // dark current-HP bar over the class-color ground (SPEC §Class colors)
```
Signature (line 16):
```csharp
        public static List<MeterRow> Build(RecapReading reading, int classArgb)
```
In the row build (line 55), replace `FillArgb = FillArgb,` with:
```csharp
                        FillArgb = CurrentHpArgb,
                        BackgroundArgb = classArgb,
```

- [ ] **Step 4: Run — expect PASS** (+ full Core suite; update existing DeathRecapEngine tests for the new signature — pass a `classArgb` like `SubclassColors.Grey`).
- [ ] **Step 5: Commit** — `git add src/eq2auras.Core/Meter/DeathRecapEngine.cs tests/eq2auras.Core.Tests/DeathRecapEngineTwoToneTests.cs && git commit -m "Class colors: DeathRecap two-tone bar (class ground + current-HP)"`

- [ ] **Phase 1 gate:** `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj` — ALL green. This is the Mac-verifiable heart of the feature; Phase 2 is transcribe-only.

---

# Phase 2 — Plugin integration (transcribe-only; CI-compile + on-box verified)

> No Mac tests. Each task compiles under branch CI (`gh run watch`); correctness is Alex's on-box script (§Testing strategy in the spec). Match existing Plugin patterns exactly.

### Task 11: `EncounterProbe` — gather uncommitted allies' ability names

**Files:**
- Modify: `src/eq2auras.Plugin/Act/EncounterProbe.cs` (the per-combatant snapshot loop ~78-91; constructor + a new `Func<string,bool>` field)

**Interfaces:**
- Consumes: `Func<string,bool> isClassCommitted` (from `OverlayHost`, Task 12).
- Produces: `CombatantReading.AbilityNames` populated for allies where `!isClassCommitted(name)`; null otherwise.

- [ ] **Step 1:** Add a constructor param + field `private readonly Func<string, bool> _isClassCommitted;` (default the field to `_ => false` if the ctor arg is null, so the probe is safe before wiring). Thread it through `Eq2AurasPlugin`'s construction in Task 12.

- [ ] **Step 2:** In the snapshot loop (inside `lock (form.AfterCombatActionDataLock)`, ~line 78-91), for each `combatant`, after building the `CombatantReading`, gather ability names **only for uncommitted allies**, keys-only, outgoing buckets, skipping the localized `"All"`:

```csharp
var reading = new CombatantReading
{
    Name = combatant.Name,
    Damage = combatant.Damage,
    Healed = combatant.Healed,
    CureDispels = combatant.CureDispels,
    DamageTaken = combatant.DamageTaken,
    HealsTaken = combatant.HealsTaken,
    PowerReplenish = combatant.PowerReplenish,
    IsAlly = allySet.Contains(combatant),
};
if (reading.IsAlly && !_isClassCommitted(reading.Name))
    reading.AbilityNames = ReadOutgoingAbilityNames(combatant);
combatants.Add(reading);
```

- [ ] **Step 3:** Add the keys-only reader (a static helper on `EncounterProbe`), modeled on `ReadBreakdown` (`EncounterProbe.cs:332-346`) but taking only outgoing buckets' keys and skipping `ReadValue`:

```csharp
private static readonly string[] OutgoingBuckets =
{
    CombatantData.DamageTypeDataOutgoingDamage,
    CombatantData.DamageTypeDataOutgoingHealing,
};

private static List<string> ReadOutgoingAbilityNames(CombatantData combatant)
{
    string allKey = ActGlobals.ActLocalization.LocalizationStrings["attackTypeTerm-all"].DisplayedText;
    var names = new List<string>();
    foreach (var bucketName in OutgoingBuckets)
    {
        if (!combatant.Items.TryGetValue(bucketName, out var damageType)) continue;
        foreach (var key in damageType.Items.Keys)   // keys-only — does NOT trigger the lazy metric folds
            if (key != allKey) names.Add(key);
    }
    return names;
}
```

*Note (plan-watch #1): outgoing buckets only — a combatant's own casts. Incoming buckets hold the attacker's/healer's ability names and would misattribute their class. Pet-proc → owner attribution (the pet is a separate combatant) is deferred; shamans/etc. still resolve via their own outgoing wards. If a field signature is missed, broaden `OutgoingBuckets` to the other outgoing category aliases.*

- [ ] **Step 4: Compile check** — push the branch, `gh run watch <id> --exit-status` (verify-only CI: Core tests + WPF compile). Expect green.
- [ ] **Step 5: Commit** — `git add src/eq2auras.Plugin/Act/EncounterProbe.cs && git commit -m "Class colors: EncounterProbe gathers uncommitted allies' outgoing ability names"`

---

### Task 12: Inference wiring — `OverlayHost` + `Eq2AurasPlugin`

**Files:**
- Modify: `src/eq2auras.Plugin/Overlay/OverlayHost.cs` (new `ClassInferenceEngine` field; `Observe` in `UpdateMeterSample`; pass `ColorForName` into the four engine calls at `:252/257/277/278/307/333/358`; expose `IsClassCommitted`)
- Modify: `src/eq2auras.Plugin/Eq2AurasPlugin.cs` (pass `_overlay.IsClassCommitted` into the `EncounterProbe` ctor)

**Interfaces:**
- Produces: `OverlayHost.IsClassCommitted(string name)`; the inference engine threaded into every row-fill call.

- [ ] **Step 1:** Add to `OverlayHost`: `private readonly ClassInferenceEngine _inference = new ClassInferenceEngine();` and:
```csharp
public bool IsClassCommitted(string name) => _inference.IsCommitted(name);
```

- [ ] **Step 2:** In `UpdateMeterSample`, **before** the window loop (after `double duration = ...`, line 269), run inference once on this poll's snapshot:
```csharp
if (combatants != null)
    foreach (var c in combatants)
        if (c.AbilityNames != null)
            _inference.Observe(c.Name, c.AbilityNames);
```

- [ ] **Step 3:** Thread `_inference.ColorForName` into the engine calls:
  - `MeterEngine.Tick` (line 278): add the resolver arg →
    ```csharp
    : _meterEngine.Tick(encounter, combatants, config.MetricKey, config.SecondaryKey, config.Scope, _inference.ColorForName);
    ```
  - `DeathsEngine.BuildList` (line 277):
    ```csharp
    ? DeathsEngine.BuildList(deaths, duration, _inference.ColorForName)
    ```
  - by-counterpart hover `BreakdownEngine.Build` (line 307) — per-counterpart:
    ```csharp
    window.RenderHover(BreakdownEngine.Build(b.Entries, metric, duration, _inference.ColorForName));
    ```
  - the synchronous hover first-paint (`ReadHoverRowsNow`, line 257) — per-counterpart:
    ```csharp
    return BreakdownEngine.Build(entries, metric, duration, _inference.ColorForName);
    ```
  - drill `BreakdownEngine.Build` (line 358) — the whole body is one combatant → a constant resolver:
    ```csharp
    ? BreakdownEngine.Build(breakdown.Entries, metric, duration, _ => _inference.ColorForName(target.CombatantName))
    ```
  - recap `DeathRecapEngine.Build` (line 333) — the victim's color:
    ```csharp
    var recapRows = recap != null ? DeathRecapEngine.Build(recap, _inference.ColorForName(deathRow.Name)) : new List<MeterRow>();
    ```

- [ ] **Step 4:** In `Eq2AurasPlugin.InitPlugin`, pass `IsClassCommitted` into the `EncounterProbe` ctor (add the arg per Task 11's signature):
```csharp
_encounterProbe = new EncounterProbe(
    () => _settings.Meter.Enabled,
    () => _overlay.CurrentDrillRequests(),
    (encounter, combatants, breakdowns, deaths, recaps) => _overlay.UpdateMeterSample(encounter, combatants, breakdowns, deaths, recaps),
    _overlay.IsClassCommitted);
```

- [ ] **Step 5: Compile check** (branch CI green) — **Commit** — `git add src/eq2auras.Plugin/Overlay/OverlayHost.cs src/eq2auras.Plugin/Eq2AurasPlugin.cs && git commit -m "Class colors: wire inference into OverlayHost + probe"`

---

### Task 13: Learned-cache I/O — `ClassCacheStore` + load/flush

**Files:**
- Create: `src/eq2auras.Plugin/SelfUpdate/ClassCacheStore.cs` (mirrors `SettingsStore.cs`)
- Modify: `src/eq2auras.Plugin/Overlay/OverlayHost.cs` (import at construction; flush at encounter end)

**Interfaces:**
- Produces: `static ClassCache ClassCacheStore.Load()`, `static void ClassCacheStore.Save(ClassCache)`.

- [ ] **Step 1:** Create `ClassCacheStore` (same shape as `SettingsStore`, sibling file `learned-classes.json`):
```csharp
using System;
using System.IO;
using Advanced_Combat_Tracker;
using Eq2Auras.Core.Meter;

namespace Eq2Auras.Plugin.SelfUpdate
{
    public static class ClassCacheStore
    {
        private static readonly object Gate = new object();

        private static string PathOnDisk => Path.Combine(
            ActGlobals.oFormActMain.AppDataFolder.FullName, "eq2auras", "learned-classes.json");

        public static ClassCache Load()
        {
            try { return File.Exists(PathOnDisk) ? ClassCache.Parse(File.ReadAllText(PathOnDisk)) : new ClassCache(); }
            catch { return new ClassCache(); }
        }

        public static void Save(ClassCache cache)
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PathOnDisk));
                File.WriteAllText(PathOnDisk, cache.ToJson());
            }
        }
    }
}
```

- [ ] **Step 2:** In `OverlayHost` construction (where `_inference` is created), warm-start it:
```csharp
_inference.Import(ClassCacheStore.Load().Entries);
```

- [ ] **Step 3:** Handle the encounter lifecycle. In `UpdateMeterSample`, track the prior `encounter.Active` in a field: on **start** (false→true) reset the per-encounter confirmations so everyone is re-read (a between-fight persona swap is caught); on **end** (true→false) flush confident diffs:
```csharp
// field: private bool _wasActive;
bool active = encounter != null && encounter.Active;
if (active && !_wasActive)
    _inference.ResetEncounter();                     // new encounter → re-read everyone this fight
if (_wasActive && !active && _inference.HasDirty)    // encounter end → flush confident diffs
{
    ClassCacheStore.Save(new ClassCache { Entries = new List<ClassCacheEntry>(_inference.Export()) });
    _inference.ClearDirty();
}
_wasActive = active;
```
Place this at the top of the dispatched body (it reads only `encounter` + inference state). Note the `Observe` loop (Task 12 Step 2) runs after this, so a fight's first sample resets *then* observes.

- [ ] **Step 4: Compile check** (branch CI green) — **Commit** — `git add src/eq2auras.Plugin/SelfUpdate/ClassCacheStore.cs src/eq2auras.Plugin/Overlay/OverlayHost.cs && git commit -m "Class colors: learned-cache warm-start + encounter-end flush"`

---

### Task 14: Row visuals — name outline, two-tone bar, min-alpha floor

**Files:**
- Modify: `src/eq2auras.Plugin/Overlay/BarRowVisual.cs` (name `Effect`; a background layer behind `_fill`; the min-alpha floor in `SetFillColor`)
- Modify: `src/eq2auras.Plugin/Overlay/MeterRowVisual.cs` (drive the background layer from `MeterRow.BackgroundArgb`)

- [ ] **Step 1: Global name text-outline.** In `BarRowVisual` where `_name` is built (~line 71-78), add a dark drop-shadow so light class fills stay legible (Cleric-white / Rogue-yellow):
```csharp
_name.Effect = new System.Windows.Media.Effects.DropShadowEffect
{
    Color = System.Windows.Media.Colors.Black,
    ShadowDepth = 0,
    BlurRadius = 2,
    Opacity = 0.9,
};
```
(`Effect` sits on the `TextBlock` element, so it survives `MeterRowVisual.Update`'s inline-`Run` replacement.)

- [ ] **Step 2: Two-tone background layer.** In `BarRowVisual`, add a background `Border` (`_background`) into the same `Grid` as `_fill`, **behind** it (add it to `grid.Children` before `_fill`, ~line 97-100), full-width, initially transparent:
```csharp
_background = new Border { Background = System.Windows.Media.Brushes.Transparent };
grid.Children.Add(_background);   // before _fill so the fill draws on top
```
Add a setter:
```csharp
public void SetBackgroundColor(int? argb)
{
    if (argb == null) { _background.Background = System.Windows.Media.Brushes.Transparent; return; }
    var c = OverlayTheme.FromArgbInt(argb.Value);
    _background.Background = new SolidColorBrush(Color.FromArgb(_fillAlpha, c.R, c.G, c.B));
}
```

- [ ] **Step 3: Min-alpha fill floor.** In `SetFillColor` (~line 115-120), clamp the effective alpha so a low fill-opacity knob can't dissolve class identity into the backplate:
```csharp
public void SetFillColor(int argb)
{
    var color = OverlayTheme.FromArgbInt(argb);
    byte alpha = _fillAlpha < MinFillAlpha ? MinFillAlpha : _fillAlpha;   // const byte MinFillAlpha = 90;
    _fill.Background = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
    if (_spark) _fill.BorderBrush = new SolidColorBrush(OverlayTheme.Spark(color));
}
```
(Add `private const byte MinFillAlpha = 90;`. Tune on-box.)

- [ ] **Step 4: Drive the background from the row.** In `MeterRowVisual.Update` where `SetFillColor(row.FillArgb)` is called (~line 132), add:
```csharp
SetBackgroundColor(row.BackgroundArgb);
```
So recap rows (BackgroundArgb set) render the class-color ground with the dark current-HP bar on top; every other row (null) stays single-tone.

- [ ] **Step 5: Compile check** (branch CI green) — **Commit** — `git add src/eq2auras.Plugin/Overlay/BarRowVisual.cs src/eq2auras.Plugin/Overlay/MeterRowVisual.cs && git commit -m "Class colors: name outline, two-tone recap bar, min-alpha fill floor"`

---

## Testing strategy

Mirrors SPEC §Testing strategy (Parse Meter — class colors). Phase 1 = Core xUnit (the guard test, commit/override, tree/colors, cache round-trip) — all green on the Mac. Phase 2 = branch CI compile + Alex's on-box merge-gate script:

- Each ally row **snaps** grey → class color within a second or two of engaging; a mob stays grey; an enemy-scoped window is all-grey.
- A raider seen a prior session is **already** colored on first resolving cast (warm-start via `learned-classes.json`).
- A **persona swap** (log out → back in as another class) re-colors within seconds.
- **Drill** an ally → every ability row in that ally's color; **hover** a by-counterpart card → each row its counterpart's color; **Death Recap** → victim's color ground with the dark current-HP bar shrinking to full color at the killing blow; the **recap-second hover** stays **red/green** (event-colored, unchanged).
- **Headers + popup family columns unchanged.** Names legible over Cleric-white / Rogue-yellow (outline); identity survives a low fill-opacity (min-alpha floor).
- Learned colors **persist across a plugin reload**.
- **Timer regression:** the shared `BarRowVisual` gained a name `Effect`, a background layer (transparent unless `BackgroundArgb` set), and a min-alpha floor — confirm timer bars/spark/drain and the by-ability drill are visually unaffected.

## Plan-watch items (from the spec review — this plan lands each)

1. **Ability-name source** — Task 11: outgoing `DamageTypeData` bucket `AttackType` **keys** (`damageType.Items.Keys`), skipping the localized `"All"`, keys-only (no `ReadValue`), inside the existing lock loop. Not the aggregate deep-read.
2. **Read discipline** — Task 11 gathers only for `IsAlly && !isClassCommitted`; Task 12 runs `Observe`/`ColorForName` after the lock releases (on the dispatcher). `IsCommitted` cross-thread safety via the engine's internal lock (Task 4).
3. **Catalog transcription** — Task 3 transcribes all 12/24 from `signatures.md` (STRONG/WEAK→final, SHARED→subclass, CUT excluded, thin-class firm-ups); the guard + presence tests run on the transcribed whole.
4. **Resolver covers all 4 sites** — Tasks 7-10 (MeterEngine, BreakdownEngine, DeathsEngine, DeathRecapEngine incl. the ex-static one); `MeterPopup.cs:161` keeps `MeterFamilyColors` (untouched).
5. **Case-insensitive name keying** — Task 4 (`StringComparer.OrdinalIgnoreCase` on the map) + Task 3 (catalog lookup).

## Scoping decisions recorded (chosen, not forgotten)

- **Outgoing buckets only** for the ability-name read (own casts; incoming would misattribute the attacker's class). Pet-proc → owner attribution is **deferred** (the pet is a separate combatant); classes still resolve via their own outgoing signatures.
- **Beastlord / Channeler** out of scope (no enum/catalog entry → grey), per Alex 2026-07-31.
- **Warm-start colors immediately.** A cached record colors the row from the first frame (not literally "on first cast") — `ColorForName` reads the cache, shown before any this-session evidence. This is the reading the spec's own persona **stale-window** requires ("shows their *previous* color until the new class's first resolving cast" — only possible if the cached color is displayed first). The read-skip (`IsCommitted`) is a *separate* flag from having a color.
- **Per-encounter confirmation reset** reconciles "skip committed combatants" (SPEC §Class colors) with "detect persona swaps": confirmation resets at each encounter start, so everyone is re-read each fight *until they re-confirm* (bounded, shrinking per fight), while the color survives the reset. Personas require log out/in — which happens **between** fights, never mid-combat — so a per-encounter re-read catches a swap without reading every combatant on every sample within a fight. This is the plan's mechanism for the spec's stated behavior, not a new design choice.
