# Recap-second hover — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the recap-second hover (SPEC §Deaths — "The recap-second hover"): hovering a Death Recap per-second row opens the hover card showing **that second's incoming events** — one row per swing, chronological, `Source · Ability` + a signed red/green amount, bar vs. the second's biggest event, no percent. A **synchronous, static** read (a past second's events are historical → no poll-refresh channel), reusing the `ReadHoverNow` seam, `HoverCard`, and `HoverPlacement`; the shape is a per-event **event log** with its own small Core builder, deliberately not the ranked breakdown engine.

**Architecture:** Six units. (1) **Core DTO additions** — a `RecapSecond` grouping value + a `Second` field on `DrillRequest`. (2) **Core `RecapEventDetail` + `RecapSecondEngine`** — the per-event DTO and the event-log builder (chronological, red/green, signed, bar-vs-top-magnitude, no percent), strict TDD. (3) **Core `DeathRecapEngine`** tags each recap row with its second offset (`DrillKey`), so the hover identifies the second without parsing `−Ns`. (4) **Plugin `EncounterProbe.TryReadRecapSecondNow`** — the synchronous one-second per-event read under the lock (mirrors `ReadRecap`/`TryReadNow`). (5) **Plugin `OverlayHost.ReadHoverRowsNow`** branches on the grouping. (6) **Plugin `MeterWindow`** hover lifecycle: fires in recap-drill mode on second-rows (by-ability drill rows stay no-hover), sync-only, auto-hides on recap exit.

**Tech Stack:** C# — Core `netstandard2.0` (Mac-testable, xUnit), Plugin `net472`/WPF (compile-verified in CI, behavior field-verified on the box). Baseline: **Core 282 green** (verified this session).

## Global Constraints

- **Single-assembly packaging.** New Core file `RecapSecondEngine.cs` is compiled into the plugin via the existing `<Compile Include="..\eq2auras.Core\**\*.cs">` glob; no new Plugin files, no `.csproj` edits.
- **No WPF in Core.** `RecapEventDetail`, `RecapSecondEngine`, the DTO additions use only `System`/`System.Collections.Generic` — no `System.Windows.*`.
- **No `async`; no `System.Web.Extensions`.** N/A here (synchronous, no JSON) — stated so it stays honored.
- **Transient, not persisted.** The hover / request are runtime-only — no `MeterWindowConfig`/`MeterSettings` changes, no DCJS surface. `BreakdownGrouping.RecapSecond` is a new enum value; `DrillRequest.Second` defaults to 0 (nothing here is serialized).
- **Static read, no poll channel.** The recap-second hover does **not** publish a poll request (`HoverTarget` stays null while drilled) — a past second's events are immutable, so a single synchronous on-enter read suffices and the card stays until the cursor leaves or the recap exits (SPEC §Deaths / §Assembly split).
- **Lock discipline (plan-watch #2).** `TryReadRecapSecondNow` runs inside its own `lock (form.AfterCombatActionDataLock)` on the overlay thread — the same synchronous-under-lock shape as `TryReadNow` — for one victim, never a fan-out.
- **Core-TDD, Plugin-transcribe.** Tasks 2–3 are strict TDD in Core. Tasks 4–6 are WPF/ACT transcribe: not Mac-buildable, gated by the branch verify-CI compile + the on-box field script (§Verification).
- **Reuse, don't reinvent.** `HoverCard`, `HoverPlacement`, the `ReadHoverNow` enter-seam, `MeterRowVisual`, `NumberFormat.SignedAbbreviate`, and `DeathRecapEngine.DmgArgb`/`HealArgb` are reused; the read mirrors `ReadRecap`.

---

## File Structure

- **Modify** `src/eq2auras.Core/Meter/Breakdown.cs` — `RecapSecond` grouping value + `DrillRequest.Second` (Task 1).
- **Modify** `src/eq2auras.Core/Meter/DeathRecap.cs` — add the `RecapEventDetail` DTO (Task 2).
- **Create** `src/eq2auras.Core/Meter/RecapSecondEngine.cs` — the event-log builder (Task 2).
- **Create** `tests/eq2auras.Core.Tests/RecapSecondEngineTests.cs` — the xUnit tests (Task 2).
- **Modify** `src/eq2auras.Core/Meter/DeathRecapEngine.cs` — tag each recap row with its second (`DrillKey`) (Task 3).
- **Modify** `tests/eq2auras.Core.Tests/DeathRecapEngineTests.cs` — assert the second tag (Task 3).
- **Modify** `src/eq2auras.Plugin/Act/EncounterProbe.cs` — `TryReadRecapSecondNow` + `CollectRecapSecond` (Task 4).
- **Modify** `src/eq2auras.Plugin/Overlay/OverlayHost.cs` — `ReadHoverRowsNow` grouping branch (Task 5).
- **Modify** `src/eq2auras.Plugin/Overlay/MeterWindow.cs` — the recap-second hover lifecycle (Task 6).

---

## Task 1: Core — the grouping value + second field (data only)

**Files:** Modify `src/eq2auras.Core/Meter/Breakdown.cs`

**Interfaces:** `BreakdownGrouping.RecapSecond`; `DrillRequest.Second` (int, default 0).

- [ ] **Step 1: Add the enum value and the field**

In `Breakdown.cs`, add `RecapSecond` to the `BreakdownGrouping` enum (after `ByCounterpart`):

```csharp
    public enum BreakdownGrouping
    {
        ByAbility = 0,
        ByCounterpart,
        RecapSecond,   // the recap-second hover — a per-event log, read via the ReadHoverNow seam (SPEC §Deaths)
    }
```

Add `Second` to `DrillRequest` (after `DeathKey`):

```csharp
        public int Second { get; set; }   // RecapSecond grouping: which recap second (0..9) to read; else unused
```

- [ ] **Step 2: Build Core (data-only)**

Run: `dotnet build src/eq2auras.Core/eq2auras.Core.csproj`
Expected: PASS. (No test: enum value + field are data holders; behavior is exercised in Tasks 2/4.)

- [ ] **Step 3: Commit**

```bash
git add src/eq2auras.Core/Meter/Breakdown.cs
git commit -m "Recap-second hover: Core RecapSecond grouping + DrillRequest.Second"
```

---

## Task 2: Core — `RecapEventDetail` + `RecapSecondEngine` (strict TDD)

**Files:**
- Modify `src/eq2auras.Core/Meter/DeathRecap.cs` (add the DTO)
- Create `src/eq2auras.Core/Meter/RecapSecondEngine.cs`
- Test `tests/eq2auras.Core.Tests/RecapSecondEngineTests.cs`

**Interfaces:**
- `Eq2Auras.Core.Meter.RecapEventDetail { string Source; string Ability; double Amount; bool IsHeal; int Order; }`
- `static List<MeterRow> RecapSecondEngine.Build(IReadOnlyList<RecapEventDetail> events)`

- [ ] **Step 1: Write the failing tests**

Create `tests/eq2auras.Core.Tests/RecapSecondEngineTests.cs`:

```csharp
using System.Collections.Generic;
using Eq2Auras.Core.Meter;
using Xunit;

public class RecapSecondEngineTests
{
    private static RecapEventDetail E(string source, string ability, double amount, bool isHeal, int order)
        => new RecapEventDetail { Source = source, Ability = ability, Amount = amount, IsHeal = isHeal, Order = order };

    [Fact]
    public void Empty_input_yields_no_rows()
    {
        Assert.Empty(RecapSecondEngine.Build(new List<RecapEventDetail>()));
        Assert.Empty(RecapSecondEngine.Build(null));
    }

    [Fact]
    public void Rows_are_time_ordered_by_Order_ascending()
    {
        var rows = RecapSecondEngine.Build(new List<RecapEventDetail>
        {
            E("Boss", "Cleave", 5000, false, 30),
            E("Priest", "Ward", 1000, true, 10),
            E("Boss", "Melee", 2000, false, 20),
        });
        Assert.Equal(new[] { "Priest · Ward", "Boss · Melee", "Boss · Cleave" },
            new[] { rows[0].Name, rows[1].Name, rows[2].Name });
    }

    [Fact]
    public void Damage_is_red_and_negative_heal_is_green_and_positive()
    {
        var rows = RecapSecondEngine.Build(new List<RecapEventDetail>
        {
            E("Boss", "Cleave", 5000, false, 10),
            E("Priest", "Ward", 1000, true, 20),
        });
        Assert.Equal("-5K", rows[0].FormattedValue);
        Assert.Equal(DeathRecapEngine.DmgArgb, rows[0].FillArgb);
        Assert.Equal("+1K", rows[1].FormattedValue);
        Assert.Equal(DeathRecapEngine.HealArgb, rows[1].FillArgb);
    }

    [Fact]
    public void Bar_is_magnitude_over_the_largest_event_regardless_of_kind()
    {
        var rows = RecapSecondEngine.Build(new List<RecapEventDetail>
        {
            E("Boss", "Cleave", 4000, false, 10),   // biggest
            E("Priest", "Ward", 1000, true, 20),
        });
        Assert.Equal(1.0, rows[0].BarFraction);
        Assert.Equal(0.25, rows[1].BarFraction);
    }

    [Fact]
    public void A_single_event_fills_the_bar()
    {
        var rows = RecapSecondEngine.Build(new List<RecapEventDetail> { E("Boss", "Cleave", 5000, false, 10) });
        Assert.Single(rows);
        Assert.Equal(1.0, rows[0].BarFraction);
    }

    [Fact]
    public void No_source_shows_the_ability_alone()
    {
        var rows = RecapSecondEngine.Build(new List<RecapEventDetail>
        {
            E(null, "Falling", 3000, false, 10),
            E("", "Bleed", 500, false, 20),
        });
        Assert.Equal("Falling", rows[0].Name);
        Assert.Equal("Bleed", rows[1].Name);
    }

    [Fact]
    public void Rows_carry_no_percent_and_no_secondaries()
    {
        var rows = RecapSecondEngine.Build(new List<RecapEventDetail> { E("Boss", "Cleave", 5000, false, 10) });
        Assert.Equal("", rows[0].FormattedPercent);
        Assert.Empty(rows[0].Secondaries);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter FullyQualifiedName~RecapSecondEngine`
Expected: FAIL — `RecapEventDetail` / `RecapSecondEngine` do not exist (compile error).

- [ ] **Step 3: Add the DTO**

In `src/eq2auras.Core/Meter/DeathRecap.cs`, add after `RecapReading`:

```csharp
    /// One incoming event for the recap-second hover (SPEC §Deaths — the recap-second hover): a
    /// single swing in the hovered second, keeping its source + ability for the event-log row.
    public sealed class RecapEventDetail
    {
        public string Source { get; set; }   // attacker (damage) or healer (heal); may be null/empty (environmental)
        public string Ability { get; set; }
        public double Amount { get; set; }   // positive magnitude
        public bool IsHeal { get; set; }
        public int Order { get; set; }       // MasterSwing.TimeSorter — chronological sort key
    }
```

- [ ] **Step 4: Write the builder**

Create `src/eq2auras.Core/Meter/RecapSecondEngine.cs`:

```csharp
using System.Collections.Generic;

namespace Eq2Auras.Core.Meter
{
    /// The recap-second hover's event log (SPEC §Deaths — the recap-second hover): one hovered
    /// second's incoming events → one MeterRow per swing, CHRONOLOGICAL (by TimeSorter), each a
    /// `Source · Ability` label + a signed amount (red damage / green heal) and a bar scaled to the
    /// second's largest-magnitude event; no percent. A sibling to DeathRecapEngine — a narrative, not
    /// the ranked breakdown engine. Colors are shared with the recap's dmg/heal columns.
    public static class RecapSecondEngine
    {
        public static List<MeterRow> Build(IReadOnlyList<RecapEventDetail> events)
        {
            var rows = new List<MeterRow>();
            if (events == null || events.Count == 0) return rows;

            double top = 0;
            foreach (var e in events)
                if (e.Amount > top) top = e.Amount;

            var ordered = new List<RecapEventDetail>(events);
            ordered.Sort((a, b) => a.Order.CompareTo(b.Order));   // chronological within the second

            foreach (var e in ordered)
            {
                rows.Add(new MeterRow
                {
                    Name = string.IsNullOrEmpty(e.Source) ? (e.Ability ?? "") : e.Source + " · " + e.Ability,
                    FormattedValue = NumberFormat.SignedAbbreviate(e.IsHeal ? e.Amount : -e.Amount),
                    FormattedPercent = "",
                    Percent = 0,
                    BarFraction = top > 0 ? e.Amount / top : 0,
                    FillArgb = e.IsHeal ? DeathRecapEngine.HealArgb : DeathRecapEngine.DmgArgb,
                    Secondaries = new List<SecondaryValue>(),
                });
            }
            return rows;
        }
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter FullyQualifiedName~RecapSecondEngine`
Expected: PASS — 7 passed.

- [ ] **Step 6: Commit**

```bash
git add src/eq2auras.Core/Meter/DeathRecap.cs src/eq2auras.Core/Meter/RecapSecondEngine.cs tests/eq2auras.Core.Tests/RecapSecondEngineTests.cs
git commit -m "Recap-second hover: Core RecapEventDetail + RecapSecondEngine event-log builder (TDD)"
```

---

## Task 3: Core — `DeathRecapEngine` tags each recap row with its second (TDD)

**Files:**
- Modify `src/eq2auras.Core/Meter/DeathRecapEngine.cs`
- Modify `tests/eq2auras.Core.Tests/DeathRecapEngineTests.cs`

The hover identifies the hovered second by the row's tag, not by parsing the `−Ns` label (plan-watch #3).

- [ ] **Step 1: Add the failing assertion**

In `tests/eq2auras.Core.Tests/DeathRecapEngineTests.cs`, add a self-contained fact (rows are oldest-first, `0s` last; each carries its second offset as `DrillKey`):

```csharp
    [Fact]
    public void Each_recap_row_is_tagged_with_its_second_offset()
    {
        var reading = new RecapReading
        {
            MaxHealthEstimate = 10000,
            Events = new List<RecapEvent>
            {
                new RecapEvent { SecondsBeforeDeath = 0, Amount = 5000, IsHeal = false },   // the death second
                new RecapEvent { SecondsBeforeDeath = 2, Amount = 3000, IsHeal = false },   // -2s
            },
        };
        var rows = DeathRecapEngine.Build(reading);   // oldest first, 0s last
        Assert.Equal("-2s", rows[0].Name);
        Assert.Equal("2", rows[0].DrillKey);
        Assert.Equal("0s", rows[1].Name);
        Assert.Equal("0", rows[1].DrillKey);
    }
```

(Requires `using System.Collections.Generic;` — already present in the test file.)

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter FullyQualifiedName~DeathRecapEngine`
Expected: FAIL — `DrillKey` is null on recap rows today.

- [ ] **Step 3: Tag the row**

In `DeathRecapEngine.Build`, add `DrillKey = s.ToString(),` to the `MeterRow` initializer (the block guarded by `if (present[s])`), alongside `Name = s == 0 ? "0s" : "-" + s + "s"`.

- [ ] **Step 4: Run the recap tests (pass) + full suite**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj --filter FullyQualifiedName~DeathRecapEngine` → PASS.
Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj` → PASS, **the prior 282 + Task 2's 7 + this new case**.

- [ ] **Step 5: Commit**

```bash
git add src/eq2auras.Core/Meter/DeathRecapEngine.cs tests/eq2auras.Core.Tests/DeathRecapEngineTests.cs
git commit -m "Recap-second hover: DeathRecapEngine tags each recap row with its second (TDD)"
```

---

## Task 4: Plugin — `TryReadRecapSecondNow` (transcribe)

**Files:** Modify `src/eq2auras.Plugin/Act/EncounterProbe.cs`

**Interfaces:** `static bool EncounterProbe.TryReadRecapSecondNow(DrillRequest request, out List<RecapEventDetail> events)`.

Mirrors `ReadRecap` (`EncounterProbe.cs:191-218`) for the death lookup and `TryReadNow` (`:331`) for the caller-thread lock, but filters to the one hovered second and emits **per-event** details with source + ability.

- [ ] **Step 1: Add `TryReadRecapSecondNow` + `CollectRecapSecond`**

Add near `ReadRecap`/`CollectRecap`:

```csharp
        /// Synchronous one-second per-event read for the recap-second hover (SPEC §Deaths — the
        /// recap-second hover). Runs on the overlay thread under the same AfterCombatActionDataLock —
        /// one victim, one second, never a fan-out. Mirrors ReadRecap's death lookup; emits one detail
        /// per incoming damage/heal swing in the hovered second, keeping its source + ability. Returns
        /// false (card stays absent) when the encounter/death is gone or the DeathKey is malformed.
        public static bool TryReadRecapSecondNow(DrillRequest request, out List<RecapEventDetail> events)
        {
            events = null;
            if (request == null || string.IsNullOrEmpty(request.DeathKey)) return false;
            try
            {
                var form = ActGlobals.oFormActMain;
                lock (form.AfterCombatActionDataLock)
                {
                    var encounter = form.ActiveZone?.ActiveEncounter;
                    if (encounter == null) return false;

                    int hash = request.DeathKey.LastIndexOf('#');
                    if (hash < 0) return false;
                    string victimName = request.DeathKey.Substring(0, hash);
                    if (!int.TryParse(request.DeathKey.Substring(hash + 1), out int ordinal)) return false;
                    if (!encounter.Items.TryGetValue(victimName.ToUpper(), out var victim)) return false;

                    string killingKey = ActGlobals.ActLocalization.LocalizationStrings["specialAttackTerm-killing"].DisplayedText;
                    var deathSwings = new List<MasterSwing>();
                    if (victim.AllInc.TryGetValue(killingKey, out var killingAt))
                        foreach (var sw in killingAt.Items)
                            if (sw.Damage == Dnum.Death) deathSwings.Add(sw);
                    deathSwings.Sort((a, b) => a.TimeSorter.CompareTo(b.TimeSorter));
                    if (ordinal < 1 || ordinal > deathSwings.Count) return false;
                    var death = deathSwings[ordinal - 1];

                    string allKey = ActGlobals.ActLocalization.LocalizationStrings["attackTypeTerm-all"].DisplayedText;
                    var list = new List<RecapEventDetail>();
                    CollectRecapSecond(victim, CombatantData.DamageTypeDataIncomingDamage, isHeal: false, death, allKey, request.Second, list);
                    CollectRecapSecond(victim, CombatantData.DamageTypeDataIncomingHealing, isHeal: true, death, allKey, request.Second, list);
                    events = list;
                }
            }
            catch
            {
                return false;
            }
            return events != null;
        }

        /// One bucket's incoming swings in a single recap second → per-event details (SPEC §Deaths).
        /// Same window/positive-only filter as CollectRecap, narrowed to the hovered second, keeping
        /// each swing's Attacker (source) + AttackType (ability) + TimeSorter (order).
        private static void CollectRecapSecond(CombatantData victim, string bucketName, bool isHeal,
            MasterSwing death, string allKey, int second, List<RecapEventDetail> details)
        {
            if (!victim.Items.TryGetValue(bucketName, out var bucket)) return;
            foreach (var pair in bucket.Items)
            {
                if (pair.Key == allKey) continue;
                foreach (var sw in pair.Value.Items)
                {
                    double secondsBefore = (death.Time - sw.Time).TotalSeconds;
                    if (sw.TimeSorter > death.TimeSorter || secondsBefore < 0 || secondsBefore >= 10) continue;
                    if ((int)System.Math.Floor(secondsBefore) != second) continue;   // only the hovered second
                    long amt = (long)sw.Damage;
                    if (amt <= 0) continue;   // real damage/heal only
                    details.Add(new RecapEventDetail { Source = sw.Attacker, Ability = sw.AttackType, Amount = amt, IsHeal = isHeal, Order = sw.TimeSorter });
                }
            }
        }
```

- [ ] **Step 2: Sanity-check Core builds standalone**

Run: `dotnet build src/eq2auras.Core/eq2auras.Core.csproj`
Expected: PASS (confirms `RecapEventDetail` compiles; the WPF/ACT probe is CI-compiled).

- [ ] **Step 3: Commit**

```bash
git add src/eq2auras.Plugin/Act/EncounterProbe.cs
git commit -m "Recap-second hover: probe TryReadRecapSecondNow — one second's incoming events, per swing, under the lock"
```

---

## Task 5: Plugin — `OverlayHost.ReadHoverRowsNow` grouping branch (transcribe)

**Files:** Modify `src/eq2auras.Plugin/Overlay/OverlayHost.cs`

- [ ] **Step 1: Branch on the grouping**

Replace `ReadHoverRowsNow` (`OverlayHost.cs:247-253`) so a `RecapSecond` request reads the event log, else the by-counterpart path (unchanged):

```csharp
        private List<MeterRow> ReadHoverRowsNow(MeterWindowConfig config, DrillRequest request)
        {
            if (request != null && request.Grouping == BreakdownGrouping.RecapSecond)
            {
                if (!EncounterProbe.TryReadRecapSecondNow(request, out var events)) return null;
                return RecapSecondEngine.Build(events);
            }
            var metric = MetricRegistry.ResolvePrimary(config.MetricKey);
            if (metric == null) return null;
            if (!EncounterProbe.TryReadNow(request, out var entries, out double duration)) return null;
            return BreakdownEngine.Build(entries, metric, duration);
        }
```

(`RecapSecondEngine` is `Eq2Auras.Core.Meter`, already imported via `using Eq2Auras.Core.Meter;`.)

- [ ] **Step 2: Sanity-check Core builds; commit**

```bash
dotnet build src/eq2auras.Core/eq2auras.Core.csproj
git add src/eq2auras.Plugin/Overlay/OverlayHost.cs
git commit -m "Recap-second hover: host routes a RecapSecond request to the event-log read"
```

---

## Task 6: Plugin — `MeterWindow` recap-second hover lifecycle (transcribe)

**Files:** Modify `src/eq2auras.Plugin/Overlay/MeterWindow.cs`

The recap-second hover fires in **recap-drill** mode on second-rows; by-ability drill rows stay no-hover; it is **sync-only** (no poll request); it auto-hides on recap exit. No slot-wiring change — recap rows reuse the same pooled slots whose `MouseEnter`/`MouseLeave` already call `OnRowHoverEnter`/`OnRowHoverLeave`.

- [ ] **Step 1: Add the hover-second field**

Below `private string _hoverCombatant;` (`MeterWindow.cs:58`), add:

```csharp
        private string _hoverRecapSecond;              // recap-drill: the hovered second-row's tag (DrillKey), or null
```

- [ ] **Step 2: Branch `OnRowHoverEnter` by mode**

Replace `OnRowHoverEnter` (`MeterWindow.cs:429-443`) — add the drill branches at the top, keep the list-mode body:

```csharp
        private void OnRowHoverEnter(MeterRowVisual slot)
        {
            if (_drilledCombatant != null)
            {
                if (_drillDeathKey != null) OnRecapSecondHoverEnter(slot);   // recap drill → per-second hover
                return;                                                       // by-ability drill → no hover (deferred seam)
            }
            var row = slot?.CurrentRow;
            if (row == null || string.IsNullOrEmpty(row.Name)) return;
            var metric = MetricRegistry.ResolvePrimary(_metricKey);
            if (metric == null || metric.IsEvent) return;       // cleared primary / event metric (Deaths) → no hover
            if (row.Name == _hoverCombatant) return;            // already the hovered row
            HideHover();                                        // clean switch: drop the prior card
            _hoverCombatant = row.Name;
            _hoverSlot = slot;
            _cb.DrillChanged?.Invoke();                         // publish the request → keeps the card live at the poll rate
            var rows = _cb.ReadHoverNow?.Invoke(HoverTarget);   // instant first paint: synchronous read of this one combatant
            if (rows != null) RenderHover(rows);
        }

        /// Recap-drill row mouseover → that second's event log, synchronous + static (a past second's
        /// events are historical, so no poll request/refresh — SPEC §Deaths — the recap-second hover).
        private void OnRecapSecondHoverEnter(MeterRowVisual slot)
        {
            var row = slot?.CurrentRow;
            if (row == null || string.IsNullOrEmpty(row.DrillKey)) return;   // needs the second tag
            if (row.DrillKey == _hoverRecapSecond) return;                   // already this second
            if (!int.TryParse(row.DrillKey, out int second)) return;
            HideHover();
            _hoverRecapSecond = row.DrillKey;
            _hoverSlot = slot;
            var request = new DrillRequest { DeathKey = _drillDeathKey, Second = second, Grouping = BreakdownGrouping.RecapSecond };
            var rows = _cb.ReadHoverNow?.Invoke(request);
            if (rows != null) ShowHoverRows(_drilledCombatant + " · " + row.Name, rows);
        }
```

- [ ] **Step 3: Generalize `OnRowHoverLeave`**

Replace `OnRowHoverLeave` (`MeterWindow.cs:445-452`) to clear either hover kind (guard on `_hoverSlot`, which both paths set):

```csharp
        private void OnRowHoverLeave()
        {
            if (_hoverSlot == null) return;
            _hoverCombatant = null;
            _hoverRecapSecond = null;
            _hoverSlot = null;
            HideHover();
            _cb.DrillChanged?.Invoke();   // drops the by-counterpart poll request; a no-op for the sync-only recap-second
        }
```

- [ ] **Step 4: Extract `ShowHoverRows` from `RenderHover`**

Replace `RenderHover` (`MeterWindow.cs:459-468`) so the card create/update/show is shared, and add `ShowHoverRows`:

```csharp
        public void RenderHover(List<MeterRow> rows)
        {
            if (_hoverCombatant == null) return;                 // left already
            var metric = MetricRegistry.ResolvePrimary(_metricKey);
            if (metric == null) return;
            string suffix = BreakdownDirection.IsIncoming(metric.BreakdownSource) ? " — by source" : " — by target";
            ShowHoverRows(_hoverCombatant + suffix, rows ?? new List<MeterRow>());
        }

        /// Create the card fresh per appearance (a reused hidden WPF window flashes its stale composited
        /// frame on re-show), then Update in place; place via Core HoverPlacement. Shared by the
        /// by-counterpart hover (live) and the recap-second hover (static).
        private void ShowHoverRows(string title, List<MeterRow> rows)
        {
            if (_hover == null) _hover = new HoverCard(_style, _opacity);
            _hover.Update(title, rows ?? new List<MeterRow>());
            _hover.ShowAt(HostRect(), AnchorRect());
        }
```

- [ ] **Step 5: Hide the card on recap exit**

In `ExitDrill` (`MeterWindow.cs:404-409`), drop any recap-second card so it never lingers past the recap (auto-exit):

```csharp
        public void ExitDrill()
        {
            if (_drilledCombatant == null) return;
            _drilledCombatant = null;
            _hoverRecapSecond = null;
            _hoverSlot = null;
            HideHover();
            _cb.DrillChanged?.Invoke();
        }
```

- [ ] **Step 6: Sanity-check Core builds; commit**

```bash
dotnet build src/eq2auras.Core/eq2auras.Core.csproj
git add src/eq2auras.Plugin/Overlay/MeterWindow.cs
git commit -m "Recap-second hover: meter recap-drill hover lifecycle (sync-only per-second event log)"
```

---

## Verification

The Plugin (WPF/ACT) does not build on the Mac, so its gate is CI + the box.

- [ ] **Push the branch; verify-only CI compiles the WPF plugin and runs Core tests.**

```bash
git push -u origin recap-second-hover
```

Watch: `gh run watch <id> --exit-status --interval 20`
Expected: **Run Core unit tests** ✓ (282 + 8) and **Build the plugin (MSBuild)** ✓. Publish skipped (branch).

- [ ] **Fix any compile errors** surfaced by CI (transcribe fixes only), re-push, re-watch until green.

- [ ] **On-box field script** (the SPEC §Testing strategy (Parse Meter — recap-second hover) merge-gate): drill a death → the recap; hover a per-second row → the card opens **instantly** listing that second's incoming events as `Source · Ability` with red `−dmg` / green `+heal`, the heaviest event's bar fullest, time-ordered; hover different seconds → each shows its own events; the **`0s`** row shows the killing blow; hovering rows on a **by-ability** drill (a non-Deaths window) opens **no** card; the card **auto-hides** when the recap exits (new encounter / clear); moving between second-rows re-anchors with no stale flash; left/right-click still land on the row underneath (click-through preserved). Light timer sanity check.

- [ ] **Present ready-for-review at the owner's merge gate.** Do NOT push `main` — the owner's sequence ends at implement; the dev release + the 1.2.0 stable promote are his calls.

---

## Self-Review Notes

- **Spec coverage:** SPEC §Deaths "The recap-second hover" — Tasks 1–6 (grouping/second, the event-log builder, the read, the lifecycle); §Assembly split (Core builder + DTO; Plugin read + lifecycle; DeathRecapEngine second-tag) — Tasks 2–6; §Slice map "Recap-second hover" — the whole plan; §Testing strategy (recap-second hover) — Task 2/3 tests + §Verification field script. No spec requirement left unimplemented.
- **Plan-watch items landed:** (1) read source per kind — Task 4 `CollectRecapSecond` reads Incoming Damage + Incoming Healing, `Source = sw.Attacker` for both (mirrors `CollectRecap`); pinned against `docs/act-parse-engine.md`. (2) lock discipline — Task 4 runs inside its own `lock (form.AfterCombatActionDataLock)` on the overlay thread, one victim, like `TryReadNow`. (3) second-tag round-trip — Task 3 tags each recap row `DrillKey = s.ToString()`; Task 6 parses it to `request.Second`; Task 4 filters swings to `floor(secondsBefore) == second`.
- **Type consistency:** `DrillRequest{Grouping=RecapSecond, DeathKey, Second}` (Task 1/6) → `ReadHoverNow` seam → `ReadHoverRowsNow` branch (Task 5) → `TryReadRecapSecondNow(out List<RecapEventDetail>)` (Task 4) → `RecapSecondEngine.Build` (Task 2) → `List<MeterRow>` → `ShowHoverRows` (Task 6) → `HoverCard.Update`. All match.
- **Static, no poll channel:** `HoverTarget` still returns null while drilled (`MeterWindow.cs:348`), so the recap-second hover publishes no poll request; the card is shown once by the sync read and cleared on leave / recap-exit. `OnRowHoverLeave`'s `DrillChanged` is a harmless no-op for it.
- **Reuse:** `HoverCard`, `HoverPlacement`, `AnchorRect`/`HostRect`, the `ReadHoverNow` callback, `MeterRowVisual`, `NumberFormat.SignedAbbreviate`, `DeathRecapEngine.DmgArgb`/`HealArgb` all reused unchanged; the read mirrors `ReadRecap`. No new file except `RecapSecondEngine.cs` (Core glob) — no `.csproj` edit.
