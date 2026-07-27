# Hover by-counterpart data pipeline — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the hover card's placeholder content (`MeterWindow.PlaceholderRows()`) with the metric's real **by-counterpart breakdown** (SPEC Part III §Row drill-down — "The by-row mouseover — by-counterpart data"): hover a list row → that row's number broken down by the other side of each swing — outgoing metrics **by victim**, incoming **by attacker** — across all seven scalar metrics and both scopes, via the drill's poll-driven request→read→rank channel generalized to carry a grouping dimension.

**Architecture:** Five units. (1) **Core DTO discriminator** — a `BreakdownGrouping` enum + a `Grouping` field on the existing `DrillRequest`/`BreakdownReading`, so one channel carries both the shipped by-ability drill and the new by-counterpart hover. (2) **Core `BreakdownDirection`** — a pure `IsIncoming(MetricBreakdownSource)` rule (incoming → by attacker, else by victim), strict TDD. (3) **Plugin `EncounterProbe`** — a `ReadByCounterpart` deep-read + a granularity-agnostic `GroupByCounterpart` swing-folder, serviced grouping-aware alongside the drill/recap reads, under the same lock. (4) **Plugin `OverlayHost`** — collect each window's hover request, and route by-counterpart readings to the card (with the drill match narrowed to by-ability so the two coexist). (5) **Plugin `MeterWindow`** — expose `HoverTarget`, rewire the enter/leave lifecycle to publish-a-request-then-render-when-the-reading-lands, add `RenderHover`, delete the placeholder.

**Tech Stack:** C# — Core `netstandard2.0` (Mac-testable, xUnit), Plugin `net472`/WPF (compile-verified in CI, behavior field-verified on the box). Baseline: **Core 274 green** (verified 2026-07-26).

## Global Constraints

Copied from the SPEC (every task's requirements implicitly include these):

- **Single-assembly packaging.** New Core file `BreakdownDirection.cs` is compiled into the plugin via the existing `<Compile Include="..\eq2auras.Core\**\*.cs">` glob; no new Plugin files, no `.csproj` edits.
- **No WPF in Core.** `BreakdownGrouping` and `BreakdownDirection` use only the `MetricBreakdownSource` enum — no `System.Windows.*`. Core must keep building on `netstandard2.0`.
- **No `async` added to the Plugin project; no `System.Web.Extensions`.** N/A here (poll-driven, no JSON) — stated so it stays honored.
- **Transient, not persisted.** The hover request/card is runtime-only — no `MeterWindowConfig`/`MeterSettings` changes, no DCJS surface. `BreakdownGrouping.ByAbility = 0` is the natural default (a `DrillRequest` built without setting it keeps today's drill behavior), though nothing here is serialized.
- **Core-TDD, Plugin-transcribe.** Task 2 is strict TDD in Core. Task 1 is a data-only Core change (compile-verified; the DTO fields carry no behavior, exercised by the Plugin). Tasks 3–5 are WPF/ACT transcribe: not Mac-buildable, so their gate is the branch verify-CI compile plus the on-box field script (§Verification).
- **Lock discipline (plan-watch #2).** The by-counterpart read runs inside the probe's existing `lock (form.AfterCombatActionDataLock)` block, for **one** combatant (a single `encounter.Items` lookup), snapshotting into Core `BreakdownEntry` DTOs before the lock releases — never a per-combatant fan-out, exactly like `ReadBreakdown`.
- **Reuse, don't reinvent.** `BreakdownEngine`, `HoverCard`, `MeterEngine`, the DTO shapes, and the request→read→route channel are reused; the only new Core logic is the direction rule, and the only new Plugin logic is the swing grouping + lifecycle rewire.

---

## File Structure

- **Modify** `src/eq2auras.Core/Meter/Breakdown.cs` — add the `BreakdownGrouping` enum and a `Grouping` field on `DrillRequest` and `BreakdownReading` (Task 1).
- **Create** `src/eq2auras.Core/Meter/BreakdownDirection.cs` — the pure `IsIncoming` rule (Task 2).
- **Create** `tests/eq2auras.Core.Tests/BreakdownDirectionTests.cs` — the xUnit `[Theory]` over the enum (Task 2).
- **Modify** `src/eq2auras.Plugin/Act/EncounterProbe.cs` — `ReadByCounterpart` + `GroupByCounterpart`, grouping-aware request servicing, `Grouping` echoed on the reading (Task 3).
- **Modify** `src/eq2auras.Plugin/Overlay/OverlayHost.cs` — collect `HoverTarget` in `RebuildDrillRequests`; route by-counterpart readings + narrow the drill match in `UpdateMeterSample` (Task 4).
- **Modify** `src/eq2auras.Plugin/Overlay/MeterWindow.cs` — `HoverTarget`, enter/leave rewire, `RenderHover`, clear-hover-on-drill, delete `ShowHoverCard`/`PlaceholderRows`/`PlaceholderRow` (Task 5).

---

## Task 1: Core — the grouping discriminator (data only)

**Files:**
- Modify: `src/eq2auras.Core/Meter/Breakdown.cs`

**Interfaces:**
- Produces: `enum Eq2Auras.Core.Meter.BreakdownGrouping { ByAbility = 0, ByCounterpart }`; `DrillRequest.Grouping` (default `ByAbility`); `BreakdownReading.Grouping`.
- Consumed by: Task 3 (probe echoes `request.Grouping`), Task 4 (host matches on it), Task 5 (window sets `ByCounterpart`).

- [ ] **Step 1: Add the enum and the two fields**

In `src/eq2auras.Core/Meter/Breakdown.cs`, add the enum (above `BreakdownEntry`) and a `Grouping` field to both `BreakdownReading` and `DrillRequest`:

```csharp
    /// Which data-grouping dimension a breakdown request/reading is keyed by — the drill's
    /// by-ability breakdown vs. the hover's by-counterpart breakdown (SPEC Part III §Row
    /// drill-down). One channel, two read shapes; a window is only ever drilling OR hovering,
    /// so it issues at most one request. ByAbility = 0 keeps a request built without it on the
    /// shipped drill path. Transient — never persisted.
    public enum BreakdownGrouping
    {
        ByAbility = 0,
        ByCounterpart,
    }
```

Add `public BreakdownGrouping Grouping { get; set; }` to `BreakdownReading` (after `Source`, `:19`) and to `DrillRequest` (after `Source`, `:28`). Leave `BreakdownEntry` and `DeathKey` unchanged.

- [ ] **Step 2: Build Core (data-only change; no test — the fields carry no behavior)**

Run: `dotnet build src/eq2auras.Core/eq2auras.Core.csproj`
Expected: PASS. (No test: `BreakdownGrouping`/`.Grouping` are data holders, like `BreakdownEntry`/`BreakdownReading` themselves; their behavior is exercised in the Plugin, verified live. The direction *logic* is Task 2, which is TDD'd.)

- [ ] **Step 3: Commit**

```bash
git add src/eq2auras.Core/Meter/Breakdown.cs
git commit -m "Hover by-counterpart data: Core BreakdownGrouping discriminator on the request/reading"
```

---

## Task 2: Core `BreakdownDirection` (strict TDD)

**Files:**
- Create: `src/eq2auras.Core/Meter/BreakdownDirection.cs`
- Test: `tests/eq2auras.Core.Tests/BreakdownDirectionTests.cs`

**Interfaces:**
- Produces: `static bool Eq2Auras.Core.Meter.BreakdownDirection.IsIncoming(MetricBreakdownSource source)`.
- Consumed by: Task 3 (probe picks `Attacker` vs `Victim`), Task 5 (window's card title "by source" vs "by target").

Plan-watch #1: this is the `breakdownSource` → counterpart direction rule. The two `Incoming*` sources group by the swing's **attacker** (by source); every other bucket by the swing's **victim** (by target). `MasterSwing` carries both `Attacker` and `Victim` (`docs/act-parse-engine.md:47`), so the Plugin reads whichever this rule selects. `None`/`Deaths` never reach the rule (a cleared primary has no rows; an event metric publishes no hover request), so `false` (by victim) is a harmless default for them.

- [ ] **Step 1: Write the failing tests**

Create `tests/eq2auras.Core.Tests/BreakdownDirectionTests.cs` (xUnit `[Theory]` over the enum, matching `MetricRegistryTests`/`MeterFamilyColorsTests` style — no namespace, `using Eq2Auras.Core.Meter; using Xunit;`):

```csharp
using Eq2Auras.Core.Meter;
using Xunit;

public class BreakdownDirectionTests
{
    [Theory]
    [InlineData(MetricBreakdownSource.IncomingDamage, true)]   // who hit me — by attacker
    [InlineData(MetricBreakdownSource.IncomingHealing, true)]  // who healed me — by attacker
    [InlineData(MetricBreakdownSource.OutgoingDamage, false)]  // what I hit — by victim
    [InlineData(MetricBreakdownSource.OutgoingHealing, false)] // whom I healed — by victim
    [InlineData(MetricBreakdownSource.PowerReplenish, false)]  // whom I fed power — by victim
    [InlineData(MetricBreakdownSource.Cures, false)]           // whom I cured — by victim
    [InlineData(MetricBreakdownSource.None, false)]            // never hovered — safe default
    [InlineData(MetricBreakdownSource.Deaths, false)]          // event metric, never hovered — safe default
    public void IsIncoming_is_true_only_for_the_two_incoming_buckets(MetricBreakdownSource source, bool expected)
    {
        Assert.Equal(expected, BreakdownDirection.IsIncoming(source));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter FullyQualifiedName~BreakdownDirection`
Expected: FAIL — `BreakdownDirection` does not exist (compile error).

- [ ] **Step 3: Write the implementation**

Create `src/eq2auras.Core/Meter/BreakdownDirection.cs`:

```csharp
namespace Eq2Auras.Core.Meter
{
    /// The by-counterpart direction rule (SPEC Part III §Row drill-down — the by-row mouseover).
    /// An INCOMING bucket groups the combatant's swings by the swing's ATTACKER (who hit/healed
    /// me — "by source"); every other bucket by the swing's VICTIM (whom I hit/healed/cured/fed
    /// — "by target"). Pure and WPF-free; the Plugin reads MasterSwing.Attacker/Victim per this
    /// rule (docs/act-parse-engine.md:47). None/Deaths never reach it (cleared primary / event
    /// metric publish no hover request), so the by-victim default is harmless for them.
    public static class BreakdownDirection
    {
        public static bool IsIncoming(MetricBreakdownSource source)
            => source == MetricBreakdownSource.IncomingDamage
            || source == MetricBreakdownSource.IncomingHealing;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter FullyQualifiedName~BreakdownDirection`
Expected: PASS — 8 passed.

- [ ] **Step 5: Run the full Core suite (no regressions)**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
Expected: PASS — **282** (274 + 8).

- [ ] **Step 6: Commit**

```bash
git add src/eq2auras.Core/Meter/BreakdownDirection.cs tests/eq2auras.Core.Tests/BreakdownDirectionTests.cs
git commit -m "Hover by-counterpart data: Core BreakdownDirection.IsIncoming (TDD)"
```

---

## Task 3: Plugin `EncounterProbe` — the by-counterpart deep-read (transcribe)

**Files:**
- Modify: `src/eq2auras.Plugin/Act/EncounterProbe.cs`

**Interfaces:**
- Consumes: `BreakdownGrouping`, `BreakdownDirection.IsIncoming` (Tasks 1–2); `DrillRequest.Grouping`; ACT `CombatantData`/`DamageTypeData`/`AttackType`/`MasterSwing` (`Attacker`/`Victim`/`Damage`).
- Produces: for a `Grouping == ByCounterpart` request, a `BreakdownReading { Grouping = ByCounterpart, Entries = (counterpart, value)[] }`; every produced reading now echoes `request.Grouping`.

- [ ] **Step 1: Route the request by grouping, and echo `Grouping` on the reading**

In the request-servicing loop (`EncounterProbe.cs:133-151`), replace the by-ability tail — the block from `if (request.Source == MetricBreakdownSource.None) continue;` through the `breakdowns.Add(...)` — with a grouping-aware read (the `Deaths` recap branch above it, `:139-144`, is unchanged):

```csharp
                                if (request.Source == MetricBreakdownSource.None) continue;
                                if (!encounter.Items.TryGetValue((request.CombatantName ?? "").ToUpper(), out var combatant)) continue;
                                var entries = request.Grouping == BreakdownGrouping.ByCounterpart
                                    ? ReadByCounterpart(combatant, request.Source)
                                    : ReadBreakdown(combatant, request.Source);
                                if (entries != null)
                                    breakdowns.Add(new BreakdownReading { CombatantName = request.CombatantName, Source = request.Source, Grouping = request.Grouping, Entries = entries });
```

(The only changes vs. today: the `entries` read forks on `Grouping`, and the reading sets `Grouping = request.Grouping`. Drill requests carry `ByAbility` (default), so their readings are tagged `ByAbility` — the coexistence guarantee, plan-watch #3.)

- [ ] **Step 2: Add `ReadByCounterpart` + the granularity-agnostic `GroupByCounterpart`**

Add both methods next to `ReadBreakdown` (after `:276`). `ReadByCounterpart` returns an **empty (non-null) list** when the combatant exists but the bucket is absent — a 0-value row still gets its (empty) card (SPEC §Row drill-down — "a zero-valued row still gets its card"); it returns null only for `None`/unmapped sources (guarded out above anyway):

```csharp
        /// One combatant's by-counterpart entries for a bucket, read under the ACT lock (SPEC
        /// Part III §Row drill-down — the by-row mouseover). Iterates the bucket's raw MasterSwings
        /// (skipping the aggregate "All" AttackType, docs/act-parse-engine.md:69-71) and folds them
        /// by counterpart. Returns an EMPTY list (not null) when the bucket is absent, so a
        /// zero-valued row still opens an honest empty card; null only for an unmapped source.
        private static List<BreakdownEntry> ReadByCounterpart(CombatantData combatant, MetricBreakdownSource source)
        {
            var bucketName = BucketName(source);
            if (bucketName == null) return null;
            var entries = new List<BreakdownEntry>();
            if (!combatant.Items.TryGetValue(bucketName, out var damageType)) return entries;

            string allKey = ActGlobals.ActLocalization.LocalizationStrings["attackTypeTerm-all"].DisplayedText;
            bool byAttacker = BreakdownDirection.IsIncoming(source);
            bool countMode = source == MetricBreakdownSource.Cures;   // cures = a swing COUNT (CombatantData.CureDispels is a count)
            var acc = new Dictionary<string, double>();
            foreach (var pair in damageType.Items)
            {
                if (pair.Key == allKey) continue;
                GroupByCounterpart(pair.Value.Items, byAttacker, countMode, acc);
            }
            foreach (var kv in acc)
                entries.Add(new BreakdownEntry { Label = kv.Key, Value = kv.Value });
            return entries;
        }

        /// Fold a swing list into a counterpart accumulator — the granularity-agnostic helper the
        /// reserved recap-second per-source breakdown reuses (SPEC §Reserved seams), fed one second's
        /// swings instead of a whole bucket. Value mode (damage/heal/power) sums positive Dnums only
        /// (skips misses/avoids/sentinels). Count mode (cures) counts every swing — mirroring the
        /// shipped drill's cures path (ReadValue reads at.Swings, a count); cure swings carry
        /// damage=1 (docs/act-parse-engine.md:326), so a value-mode sum would coincide here, but
        /// count mode keeps the count explicit and independent of that Dnum value.
        private static void GroupByCounterpart(IEnumerable<MasterSwing> swings, bool byAttacker, bool countMode, Dictionary<string, double> acc)
        {
            foreach (var sw in swings)
            {
                long amt = (long)sw.Damage;
                if (!countMode && amt <= 0) continue;
                string counterpart = (byAttacker ? sw.Attacker : sw.Victim) ?? "";
                acc.TryGetValue(counterpart, out double cur);
                acc[counterpart] = cur + (countMode ? 1 : amt);
            }
        }
```

Add `using System.Collections.Generic;` is already present (`:2`); `System.Linq` is not needed.

- [ ] **Step 3: Sanity-check Core still builds standalone (the probe references new Core members)**

Run: `dotnet build src/eq2auras.Core/eq2auras.Core.csproj`
Expected: PASS (confirms `BreakdownGrouping`/`BreakdownDirection` compile; the WPF/ACT probe itself is compile-verified in CI, §Verification).

- [ ] **Step 4: Commit**

```bash
git add src/eq2auras.Plugin/Act/EncounterProbe.cs
git commit -m "Hover by-counterpart data: probe by-counterpart deep-read + granularity-agnostic swing folder"
```

---

## Task 4: Plugin `OverlayHost` — collect + route the hover request (transcribe)

**Files:**
- Modify: `src/eq2auras.Plugin/Overlay/OverlayHost.cs`

**Interfaces:**
- Consumes: `MeterWindow.HoverTarget` (Task 5), `MeterWindow.RenderHover`/`HideHover` (Task 5); `BreakdownGrouping`; `BreakdownEngine.Build`.
- Produces: hover requests in the `_drillRequests` snapshot; by-counterpart readings routed to each list-mode window's card.

- [ ] **Step 1: Collect each window's `HoverTarget` alongside its `DrillTarget`**

In `RebuildDrillRequests` (`OverlayHost.cs:223-232`), add the hover target inside the loop (a window is only ever drilling OR hovering, so at most one of the two is non-null):

```csharp
            foreach (var window in _meterWindows.Values)
            {
                var target = window.DrillTarget;
                if (target != null) list.Add(target);
                var hover = window.HoverTarget;
                if (hover != null) list.Add(hover);
            }
```

- [ ] **Step 2: Narrow the drill match to by-ability (coexistence, plan-watch #3)**

In `UpdateMeterSample`, the drilled-combatant breakdown match (`OverlayHost.cs:300-303`) must ignore a by-counterpart reading for the same combatant+source (another window may be hovering it). Add the grouping guard:

```csharp
                    BreakdownReading breakdown = null;
                    if (breakdowns != null)
                        foreach (var b in breakdowns)
                            if (b.Grouping == BreakdownGrouping.ByAbility && b.CombatantName == target.CombatantName && b.Source == target.Source) { breakdown = b; break; }
```

- [ ] **Step 3: Route the by-counterpart reading to the card in list mode**

In `UpdateMeterSample`, the list-mode branch (`OverlayHost.cs:258-263`) currently renders the list and `continue`s. Replace it so a list-mode window with a hover target also drives its card:

```csharp
                    var target = window.DrillTarget;
                    if (target == null || metric == null)
                    {
                        window.Render(listFrame);

                        // Hover (list mode, real metric): route the by-counterpart reading to the card.
                        // A present reading (even empty) → show/update the card; none yet (first poll
                        // after enter, or the combatant left) → hide it. metric == null (cleared) has no
                        // HoverTarget, so nothing to show.
                        var hover = window.HoverTarget;
                        if (hover != null)
                        {
                            BreakdownReading hoverReading = null;
                            if (breakdowns != null)
                                foreach (var b in breakdowns)
                                    if (b.Grouping == BreakdownGrouping.ByCounterpart && b.CombatantName == hover.CombatantName && b.Source == hover.Source) { hoverReading = b; break; }
                            if (hoverReading != null)
                                window.RenderHover(BreakdownEngine.Build(hoverReading.Entries, metric, duration));
                            else
                                window.HideHover();
                        }
                        continue;
                    }
```

(`hover != null` already implies `metric != null` — `HoverTarget` returns null when the metric is cleared or an event, Task 5 — so `metric` is safe to pass to `Build`.)

- [ ] **Step 4: Sanity-check Core still builds**

Run: `dotnet build src/eq2auras.Core/eq2auras.Core.csproj`
Expected: PASS (the WPF host is CI-compiled, §Verification).

- [ ] **Step 5: Commit**

```bash
git add src/eq2auras.Plugin/Overlay/OverlayHost.cs
git commit -m "Hover by-counterpart data: host collects the hover request + routes the reading to the card"
```

---

## Task 5: Plugin `MeterWindow` — the hover lifecycle rewire (transcribe)

**Files:**
- Modify: `src/eq2auras.Plugin/Overlay/MeterWindow.cs`

**Interfaces:**
- Consumes: `BreakdownGrouping`, `BreakdownDirection.IsIncoming`, `MetricRegistry.ResolvePrimary`, `MetricDef.BreakdownSource`/`.IsEvent`, `DrillRequest`, `HoverCard`, `HoverRect`.
- Produces: `public DrillRequest HoverTarget`, `public void RenderHover(List<MeterRow> rows)`; the publish-then-render-on-reading enter/leave lifecycle. `HideHover`/`HostRect`/`AnchorRect` (existing) reused unchanged.

The card no longer opens synchronously on enter; enter publishes a request and the card opens when its first reading lands (SPEC §The hover surface — "first paint when the reading lands, no empty flash"; §Row drill-down — the by-row mouseover). Recreated per appearance (per row-enter), updated in place across polls while hovered.

- [ ] **Step 1: Add `HoverTarget`**

Add next to `DrillTarget` (after `:338`):

```csharp
        /// The window's current hover request, or null when drilled / not hovering / the primary is
        /// cleared or an event metric (Deaths — no by-counterpart hover). The host reads this to build
        /// the probe's request set and to route the card's reading (SPEC §Row drill-down — the by-row
        /// mouseover). Grouping = ByCounterpart distinguishes it from the by-ability DrillTarget.
        public DrillRequest HoverTarget
        {
            get
            {
                if (_drilledCombatant != null || _hoverCombatant == null) return null;
                var metric = MetricRegistry.ResolvePrimary(_metricKey);
                if (metric == null || metric.IsEvent) return null;
                return new DrillRequest { CombatantName = _hoverCombatant, Source = metric.BreakdownSource, Grouping = BreakdownGrouping.ByCounterpart };
            }
        }
```

- [ ] **Step 2: Rewire `OnRowHoverEnter`/`OnRowHoverLeave`**

Replace `OnRowHoverEnter` (`:410-419`) and `OnRowHoverLeave` (`:421-427`). Enter publishes the request (no card yet); leave drops it and hides the card. Both call `_cb.DrillChanged` (the host's request-rebuild — reused for hover, it just means "my requests changed"):

```csharp
        private void OnRowHoverEnter(MeterRowVisual slot)
        {
            if (_drilledCombatant != null) return;              // list mode only
            var row = slot?.CurrentRow;
            if (row == null || string.IsNullOrEmpty(row.Name)) return;
            var metric = MetricRegistry.ResolvePrimary(_metricKey);
            if (metric == null || metric.IsEvent) return;       // cleared primary / event metric (Deaths) → no hover
            if (row.Name == _hoverCombatant) return;            // already the hovered row
            HideHover();                                        // clean switch: drop the prior card before the new reading lands
            _hoverCombatant = row.Name;
            _hoverSlot = slot;
            _cb.DrillChanged?.Invoke();                         // publish the request; the card appears when its reading lands
        }

        private void OnRowHoverLeave()
        {
            if (_hoverCombatant == null) return;
            _hoverCombatant = null;
            _hoverSlot = null;
            HideHover();
            _cb.DrillChanged?.Invoke();                         // drop the hover request
        }
```

- [ ] **Step 3: Replace `ShowHoverCard` with `RenderHover`**

Delete `ShowHoverCard` (`:429-438`) and add `RenderHover`, called by the host each poll with the freshly-built rows. Creates the card lazily (per appearance), then updates in place; the title reflects the direction (by target for outgoing, by source for incoming):

```csharp
        /// Render the hovered combatant's by-counterpart breakdown into the card (host, each poll while
        /// hovered). Creates the card fresh on the first reading of an appearance — a reused hidden WPF
        /// window flashes its stale composited frame on re-show, so a fresh one only composites current
        /// content — then updates in place across polls. Title: "by source" for incoming metrics (who
        /// hit/healed me), "by target" otherwise (SPEC §Row drill-down — the by-row mouseover).
        public void RenderHover(List<MeterRow> rows)
        {
            if (_hoverCombatant == null) return;                 // left already
            var metric = MetricRegistry.ResolvePrimary(_metricKey);
            if (metric == null) return;
            if (_hover == null) _hover = new HoverCard(_style, _opacity);
            string suffix = BreakdownDirection.IsIncoming(metric.BreakdownSource) ? " — by source" : " — by target";
            _hover.Update(_hoverCombatant + suffix, rows ?? new List<MeterRow>());
            _hover.ShowAt(HostRect(), AnchorRect());
        }
```

- [ ] **Step 4: Clear the hover when entering drill mode**

In `EnterDrill`, before `_cb.DrillChanged?.Invoke();` (`:381`), drop any live hover card so it doesn't linger over the drilled body (the mouse is on the row when the click drills):

```csharp
            _hoverCombatant = null;
            _hoverSlot = null;
            HideHover();
            _cb.DrillChanged?.Invoke();
```

- [ ] **Step 5: Delete the placeholder**

Delete `PlaceholderRows` (`:471-484`) and `PlaceholderRow` (`:486-495`) — the placeholder comment block above `OnRowHoverEnter` (`:405-408`) should be updated to describe the real by-counterpart lifecycle (or removed). `HideHover` (`:440-444`), `HostRect` (`:446-447`), `AnchorRect` (`:453-469`), and the `MouseEnter`/`MouseLeave` wiring (`:315-316`) stay as-is. `OnClosed`'s `_hover?.Close()` (`:685`) stays.

- [ ] **Step 6: Sanity-check Core still builds**

Run: `dotnet build src/eq2auras.Core/eq2auras.Core.csproj`
Expected: PASS (the WPF window is CI-compiled, §Verification).

- [ ] **Step 7: Commit**

```bash
git add src/eq2auras.Plugin/Overlay/MeterWindow.cs
git commit -m "Hover by-counterpart data: meter hover lifecycle — publish request, render on reading, drop placeholder"
```

---

## Verification

The Plugin (WPF/ACT) does not build on the Mac, so its gate is CI + the box, per §Global Constraints.

- [ ] **Push the branch; the verify-only CI compiles the WPF plugin and runs Core tests.**

```bash
git push -u origin hover-by-counterpart
```

Watch: `gh run watch <id> --exit-status --interval 20`
Expected: **Run Core unit tests** ✓ (282) and **Build the plugin (MSBuild)** ✓. Publish is skipped (branch, not `main`).

- [ ] **Fix any compile errors** surfaced by CI (transcribe fixes only), re-push, re-watch until green.

- [ ] **On-box field script** (the SPEC §Testing strategy (Parse Meter — hover by-counterpart data) merge-gate). Hover a **DPS** ally → the card lists whom they hit, each with its share of that ally's own total and a bar vs. the top target; **Damage Taken** → who hit them; **HPS / Total Healing** → whom they healed; **Cures** → whom they cured; **Power Replenish** → whom they fed power; **Healing Taken** → who healed them; an **Enemy Damage Taken** enemy row → which allies hit that mob; an **Enemy Healing Done** enemy row → whom that enemy healed (the sole enemy-scope outgoing/by-victim case). The card **updates live** through the fight and **freezes** at end; moving between rows re-anchors with **no stale flash**; a **0-value** row (a healer on a DPS window) opens an **empty** card, no crash; a **Deaths** window's rows open **no** card; left/right-click still land on the row underneath (click-through preserved). Light timer sanity check (this slice re-extracts nothing from the shared substrate).

- [ ] **Present ready-for-review at the owner's merge gate.** Do NOT push `main` — the owner's sequence ends at implement; the dev release + promote are his calls.

---

## Self-Review Notes

- **Spec coverage:** §Row drill-down "The by-row mouseover — by-counterpart data" — Tasks 1–5 (direction rule, poll-driven channel, swing grouping, event/zero-state edges); §Assembly split (Core direction rule; Plugin request/read/route/lifecycle) — Tasks 1–5; §Slice map "Hover by-counterpart data" — the whole plan; §Testing strategy (hover by-counterpart data) — Task 2 tests + §Verification field script. `MeterWindow.PlaceholderRows()` deleted (Task 5). No spec requirement left unimplemented.
- **Plan-watch items landed:** (1) direction per metric — Task 2 (`BreakdownDirection`, exhaustive enum test) + Task 3 (`ReadByCounterpart` picks `Attacker`/`Victim` via `IsIncoming`, `MasterSwing.Attacker`/`Victim` per `docs/act-parse-engine.md:47`). (2) lock discipline — Task 3 reads inside the existing `lock (form.AfterCombatActionDataLock)` block (`EncounterProbe.cs:51`), one combatant (`encounter.Items.TryGetValue`), snapshot to `BreakdownEntry` DTOs, no fan-out. (3) coexistence — Task 1 (`Grouping` field) + Task 3 (reading echoes `request.Grouping`) + Task 4 (drill match narrowed to `ByAbility`, hover match to `ByCounterpart`) — a window drilling and another hovering the same combatant route to distinct readings.
- **Type consistency:** `BreakdownGrouping` defined (Task 1), set on `DrillRequest` by `HoverTarget` (Task 5) and `DrillTarget` (default `ByAbility`), echoed on `BreakdownReading` (Task 3), matched by the host (Task 4). `ReadByCounterpart` returns `List<BreakdownEntry>` → `BreakdownEngine.Build(entries, metric, duration)` → `List<MeterRow>` → `HoverCard.Update(title, rows)` (existing signature). `HoverTarget`/`RenderHover`/`HideHover` are `public` (called cross-file by the host, same assembly).
- **Zero-state vs. first-paint (the one subtle contract):** `ReadByCounterpart` returns an **empty list** (not null) for an existing combatant with an absent bucket, so the host gets a present-but-empty reading and shows an empty card (0-value row honesty); a genuinely missing reading (first poll after enter, before the probe runs; or the combatant left the population) is "no reading" → `HideHover`. Enter's `HideHover()` closes the prior card on a row switch so the gap shows no card, not stale content.
- **Threading:** `HoverTarget` (read in `RebuildDrillRequests` and `UpdateMeterSample`) and `RenderHover`/`HideHover`/enter/leave all run on the single overlay STA/dispatcher thread the windows live on — same thread as the shipped drill path; the `_drillRequests` swap stays the lock-free volatile reference the probe reads on ACT's thread.
- **No async, no new persisted state, no new file except `BreakdownDirection.cs` (Core glob) — no `.csproj` edit.**
