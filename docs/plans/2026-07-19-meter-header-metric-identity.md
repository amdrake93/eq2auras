# Meter §Header redesign — primary metric as the left identity — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the encounter/mob name from the meter header and make the primary metric the left identity, with the right cluster as fixed cells (secondary label / total / cog) that align 1:1 with the row columns.

**Architecture:** Drop the encounter `Title` from the data model (`EncounterReading` → `MeterEngine` → `MeterFrame`) and stop computing `GetStrongestEnemy` in the adapter. In `MeterWindow`, the left cluster becomes `(duration) + metric-name` (the metric name is `MeterFrame.MetricLabel`, which already exists — it just moves out of the right cluster) laid out in a `DockPanel` so the name ellipsis-trims to the remaining width without a manual reserve; the right cluster keeps only the secondary label (now a fixed `NumberWidth` cell) and the total, retiring `UpdateTitleMaxWidth` entirely.

**Tech Stack:** C# / .NET — `eq2auras.Core` (netstandard2.0, xUnit, Mac-testable, strict TDD for the data-model change) + `eq2auras.Plugin` (net472/WPF, transcribe-only: CI-compile-verified, NOT runtime-verified on the Mac — the header layout is Alex's on-box gate).

## Global Constraints

- **Single-assembly packaging.** The Plugin compiles Core in via the glob `..\eq2auras.Core\**\*.cs` — no new files here, so no csproj change.
- **No `async` in the Plugin project.** (None introduced.)
- **Core tests are xUnit** (`[Fact]`, `Assert.Equal`), no namespace, matching `MeterEngineTests.cs`.
- **Core-only local build/test:** `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`. **Never** build the Plugin/solution on the Mac (net472+WPF) — the Plugin is verified by branch CI + Alex's on-box run.
- **Timer module untouched** — this change is meter-header only; no `OverlayTheme`/timer code is edited.
- **Every commit stays CI-green.** Task 1 removes the `Title` DTO fields *and* the one `MeterWindow` line that reads `frame.Title`, so the Plugin still compiles after Task 1; Task 2 is the pure layout restructure.

## Consumer impact analysis

Single repo. Exhaustive grep of the two removed fields:

- **`EncounterReading.Title`** (`MeterReading.cs:23`) — set at `EncounterProbe.cs:54`, read at `MeterEngine.cs:25` and `:94`, set in test helpers `MeterEngineTests.cs:9,33`. No others.
- **`MeterFrame.Title`** (`MeterFrame.cs`) — set at `MeterEngine.cs:25,94`, read at `MeterWindow.cs:272`, asserted in `MeterEngineTests.cs:24,63`. No others.
- **`MeterFrame.MetricLabel`** — unchanged field; today rendered into the right-cluster `_metricText` (`MeterWindow.cs:274`), moves to the left cluster. No signature change.
- `GetStrongestEnemy` remains defined in ACT's API; we simply stop calling it — no other caller in the repo.

## File structure

- **Modify** `src/eq2auras.Core/Meter/MeterReading.cs` — drop `EncounterReading.Title` (Task 1).
- **Modify** `src/eq2auras.Core/Meter/MeterFrame.cs` — drop `MeterFrame.Title` (Task 1).
- **Modify** `src/eq2auras.Core/Meter/MeterEngine.cs` — stop setting `Title` (both frame constructions) (Task 1).
- **Modify** `tests/eq2auras.Core.Tests/MeterEngineTests.cs` — drop `Title` inputs + assertions (Task 1).
- **Modify** `src/eq2auras.Plugin/Act/EncounterProbe.cs` — stop setting `Title` (Task 1, transcribe).
- **Modify** `src/eq2auras.Plugin/Overlay/MeterWindow.cs` — Task 1 removes the `frame.Title` read; Task 2 is the header restructure (transcribe).

---

### Task 1: Remove the encounter title from the data model

**Files:**
- Modify: `src/eq2auras.Core/Meter/MeterReading.cs:23`
- Modify: `src/eq2auras.Core/Meter/MeterFrame.cs` (the `Title` property)
- Modify: `src/eq2auras.Core/Meter/MeterEngine.cs:25,94`
- Modify: `tests/eq2auras.Core.Tests/MeterEngineTests.cs:9,24,33,63`
- Modify (transcribe-only): `src/eq2auras.Plugin/Act/EncounterProbe.cs:54`, `src/eq2auras.Plugin/Overlay/MeterWindow.cs:272`

**Interfaces:**
- Produces: `MeterFrame` with **no `Title` property**; `EncounterReading` with **no `Title` property**; `MeterEngine.Tick` returns frames that carry only `DurationText`, `MetricLabel`, `SecondaryLabel`, `TotalText` for the header.

This task is a **field removal / refactor**, not new behavior — the guard is the Core suite compiling and passing with the field gone (no manufactured failing test).

- [ ] **Step 1: Drop the `Title` inputs and assertions from the tests**

In `tests/eq2auras.Core.Tests/MeterEngineTests.cs`:

Line 9 — remove `Title = "Vithnok", ` from the `Live` helper so it reads:

```csharp
    private static EncounterReading Live(double seconds) => new()
        { Exists = true, Active = true, LiveDurationSeconds = seconds, FinalDurationSeconds = 0 };
```

Line ~33 — remove `Title = "Vithnok", ` from the frozen encounter so it reads:

```csharp
        var frozen = new EncounterReading
            { Exists = true, Active = false, LiveDurationSeconds = 120, FinalDurationSeconds = 100 };
```

Delete the two `Title` assertions: `Assert.Equal("Vithnok", frame.Title);` (in `Rates_divide_totals_by_the_live_wall_clock_while_active`) and `Assert.Equal("", none.Title);` (in `Empty_encounter_and_empty_ally_list_render_an_empty_frame`).

- [ ] **Step 2: Remove the `Title` property from the DTOs**

In `src/eq2auras.Core/Meter/MeterReading.cs`, delete line 23:

```csharp
        public string Title { get; set; }              // strongest-enemy-so-far; may flip mid-fight
```

In `src/eq2auras.Core/Meter/MeterFrame.cs`, delete the `Title` property:

```csharp
        public string Title { get; set; }
```

- [ ] **Step 3: Stop setting `Title` in the engine**

In `src/eq2auras.Core/Meter/MeterEngine.cs`, delete the `Title = …` line from **both** frame constructions (the cleared-primary early return, currently `:25`, and the main return, currently `:94`):

```csharp
                    Title = encounter != null && encounter.Exists ? (encounter.Title ?? "") : "",
```

(Both occurrences are identical text; remove each.)

- [ ] **Step 4: Run the Core suite — expect green**

Run: `dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`
Expected: PASS — the suite compiles with no `Title` on either DTO and the two assertions gone.

- [ ] **Step 5: Remove the Plugin reads of the removed fields (transcribe-only — CI-verified)**

In `src/eq2auras.Plugin/Act/EncounterProbe.cs`, delete the `Title` line from the `EncounterReading` initializer (currently `:54`):

```csharp
                            Title = encounter.GetStrongestEnemy(ActGlobals.charName),
```

In `src/eq2auras.Plugin/Overlay/MeterWindow.cs`, delete the title read in `Render` (currently `:272`):

```csharp
            _titleText.Text = frame.Title;
```

(`_titleText` remains declared/constructed after this step — now never assigned, harmless — and is fully removed in Task 2. `UpdateTitleMaxWidth` still runs; it reads `_titleText.Text` (now always empty) without error.)

- [ ] **Step 6: Commit**

```bash
git add src/eq2auras.Core/Meter/MeterReading.cs src/eq2auras.Core/Meter/MeterFrame.cs src/eq2auras.Core/Meter/MeterEngine.cs tests/eq2auras.Core.Tests/MeterEngineTests.cs src/eq2auras.Plugin/Act/EncounterProbe.cs src/eq2auras.Plugin/Overlay/MeterWindow.cs
git commit -m "Meter: drop the encounter title from the data model

EncounterReading.Title and MeterFrame.Title removed; MeterEngine stops
sourcing/setting it; EncounterProbe stops computing GetStrongestEnemy;
MeterWindow stops reading frame.Title. Header restructure follows."
```

---

### Task 2: Restructure the header — metric to the left, right cluster to fixed cells

Plugin/WPF only; **not** Mac-testable. Verified by branch CI compile + the on-box merge-gate script (Testing strategy below). No unit tests.

**Files:**
- Modify: `src/eq2auras.Plugin/Overlay/MeterWindow.cs` — field decl (`:45`), header construction (`:81-102`, `:113-146`), `Render` (`:271-278`), `SetFont` (`:400-409`), `SetRowWidth` (`:447`), `UpdateTitleMaxWidth` (`:417-429`), `SetHeaderLabel` comment (`:225-227`).

**Interfaces:**
- Consumes: `MeterFrame.MetricLabel` / `SecondaryLabel` / `TotalText` / `DurationText` (unchanged); `MeterColumns.NumberWidth` / `PercentWidth` / `ColumnGap` (`MeterColumns.cs`); `Theme.TextPrimary` / `TextLabel` via `HeaderBlock`.

- [ ] **Step 1: Remove the `_titleText` field**

In `src/eq2auras.Plugin/Overlay/MeterWindow.cs`, delete the field declaration (currently `:45`):

```csharp
        private readonly TextBlock _titleText;
```

- [ ] **Step 2: Drop the `_titleText` construction; make `_metricText` the left identity**

In the constructor, delete the three `_titleText` lines (currently `:82-84`):

```csharp
            _titleText = HeaderBlock(style, dim: false);
            _titleText.FontWeight = FontWeights.SemiBold;
            _titleText.TextTrimming = TextTrimming.CharacterEllipsis;
```

The metric label `_metricText` is already built white (`HeaderBlock(style, dim: false)`, currently `:85`). Immediately after that line, give it the old title's identity treatment (semibold + ellipsis-trim):

```csharp
            _metricText.FontWeight = FontWeights.SemiBold;
            _metricText.TextTrimming = TextTrimming.CharacterEllipsis;
```

- [ ] **Step 3: Rebuild the left cluster as `(duration) metric` with a trimming DockPanel**

Replace the left-cluster block (currently the comment `:89-94` plus the `StackPanel` build `:95-102` that adds `_durationText` and `_titleText`) with a `DockPanel` — duration docked left, metric filling and trimming:

```csharp
            // Left cluster: (duration) + primary metric NAME (the meter's identity — SPEC §Header).
            // A DockPanel docks the duration left and lets the metric name fill the remaining width
            // and ellipsis-trim to it — the layout system bounds it (column 0 is the grid's star
            // column), so no manual reserve/UpdateTitleMaxWidth is needed. The encounter/mob name is
            // gone (SPEC §Header — two title-length strings competed).
            var leftCluster = new DockPanel
            {
                LastChildFill = true,
                VerticalAlignment = VerticalAlignment.Center,
            };
            DockPanel.SetDock(_durationText, Dock.Left);
            leftCluster.Children.Add(_durationText);
            leftCluster.Children.Add(_metricText);   // last child fills column 0's remaining width and trims
```

- [ ] **Step 4: Make the secondary label a fixed cell; drop the primary label from the right cluster**

In the right-cluster setup (currently `:113-134`), the total and cog already reserve their column widths. Update the secondary label to reserve the value-column width and right-align (so it sits over the row's secondary column), and remove the `_metricText` margin line (it's no longer in the right cluster). Replace the block that currently reads:

```csharp
            _totalText.Width = MeterColumns.NumberWidth(style, style.RowText);
            _totalText.TextAlignment = TextAlignment.Right;
            _totalText.Margin = new Thickness(0, 0, MeterColumns.ColumnGap, 0);          // gap to the cog
            _secondaryLabelText.Margin = new Thickness(0, 0, MeterColumns.ColumnGap, 0); // gap to the primary label
            _metricText.Margin = new Thickness(0, 0, MeterColumns.ColumnGap, 0);         // gap to the total
            affordance.Width = MeterColumns.PercentWidth(style, style.RowText * 11.0 / 13.0);
            affordance.TextAlignment = TextAlignment.Right;
```

with:

```csharp
            // Right cluster mirrors the rows' columns 1:1 (SPEC §Header): [secondary label][total] as
            // fixed NumberWidth cells over the secondary + value columns, cog in the PercentWidth slot
            // over the percent column. The primary label is NOT here — it's the left identity now.
            _totalText.Width = MeterColumns.NumberWidth(style, style.RowText);
            _totalText.TextAlignment = TextAlignment.Right;
            _totalText.Margin = new Thickness(0, 0, MeterColumns.ColumnGap, 0);          // gap to the cog
            _secondaryLabelText.Width = MeterColumns.NumberWidth(style, style.RowText);  // over the secondary column
            _secondaryLabelText.TextAlignment = TextAlignment.Right;
            _secondaryLabelText.Margin = new Thickness(0, 0, MeterColumns.ColumnGap, 0); // gap to the total
            affordance.Width = MeterColumns.PercentWidth(style, style.RowText * 11.0 / 13.0);
            affordance.TextAlignment = TextAlignment.Right;
```

Then in the `metricCluster` build (currently `:126-134`), remove `_metricText` from the children so it holds only the secondary label and total:

```csharp
            var metricCluster = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
            };
            metricCluster.Children.Add(_secondaryLabelText);
            metricCluster.Children.Add(_totalText);
```

- [ ] **Step 5: Drop the `UpdateTitleMaxWidth` call from `Render` and remove the `frame.Title`-era comment**

In `Render` (currently `:271-278`), remove the `UpdateTitleMaxWidth();` line (`:276`) so it reads:

```csharp
        public void Render(MeterFrame frame)
        {
            _lastFrame = frame;
            _durationText.Text = "(" + frame.DurationText + ") ";
            SetHeaderLabel(_secondaryLabelText, frame.SecondaryLabel);
            SetHeaderLabel(_metricText, frame.MetricLabel);
            SetHeaderLabel(_totalText, frame.TotalText);

            RenderSlots();
        }
```

- [ ] **Step 6: Update `SetFont` — re-measure the secondary cell, drop the title font + reserve call**

In `SetFont` (currently `:400-409`), remove the `_titleText` ApplyFont line and the `UpdateTitleMaxWidth()` call, and add the secondary label's width re-measure alongside the total's:

```csharp
            _style.ApplyFont(_durationText, _style.RowText);
            _style.ApplyFont(_metricText, _style.RowText);
            _style.ApplyFont(_secondaryLabelText, _style.RowText);
            _style.ApplyFont(_totalText, _style.RowText);
            _style.ApplyFont(_affordance, _style.RowText);
            _totalText.Width = MeterColumns.NumberWidth(_style, _style.RowText);
            _totalText.Margin = new Thickness(0, 0, MeterColumns.ColumnGap, 0);
            _secondaryLabelText.Width = MeterColumns.NumberWidth(_style, _style.RowText);
            _affordance.Width = MeterColumns.PercentWidth(_style, _style.RowText * 11.0 / 13.0);
```

- [ ] **Step 7: Remove `UpdateTitleMaxWidth` and its remaining call sites**

Delete the entire `UpdateTitleMaxWidth` method (currently `:412-429`, doc-comment through the closing brace). Remove its two remaining call sites: the constructor call (currently `:147`, `UpdateTitleMaxWidth();   // cap the title …`) and the `SetRowWidth` call (currently `:447`, `UpdateTitleMaxWidth();`).

- [ ] **Step 8: Refresh the `SetHeaderLabel` comment (drops the "title-trim budget" reference)**

In the `SetHeaderLabel` doc-comment (currently `:225-227`), replace the "title-trim budget" wording:

```csharp
        /// A header label that vanishes when blank: an empty label — no secondary selected, or a
        /// cleared primary (no metric name, no total) — collapses so it reserves no cluster width,
        /// and a cleared primary leaves just the duration on the left and the cog on the right (SPEC §Header).
```

- [ ] **Step 9: Commit**

```bash
git add src/eq2auras.Plugin/Overlay/MeterWindow.cs
git commit -m "Meter: header restructure — primary metric is the left identity

Left cluster = (duration) + metric name in a trimming DockPanel; right cluster
= secondary label (fixed NumberWidth cell) + total over the secondary/value
columns, cog over percent. _titleText and UpdateTitleMaxWidth removed."
```

---

## Testing strategy

**Core (Mac, automated):** Task 1's data-model removal is guarded by the full Core suite compiling and passing with `Title` gone (`dotnet test tests/eq2auras.Core.Tests/eq2auras.Core.Tests.csproj`).

**Plugin (branch CI):** the branch push runs verify-only CI — Core tests + the WPF plugin compile + build artifact. Task 1 Step 5 and all of Task 2 are transcribe-only and gated by that compile.

**On-box merge-gate live script (Alex, Windows box, dev-latest after merge):**
1. Open a DPS meter with an HPS secondary → the **left** reads `(m:ss) DPS` (no mob name); the **right** reads the `HPS` label over the secondary column, the **total** over the value column, cog far right — each directly above its row column, aligned down every row.
2. Switch the primary (DPS→HPS→Cures) → the **left identity** updates to the new metric name; colors/columns still align.
3. **No mob name anywhere** in the header, and it **doesn't flip** mid-fight (the strongest-enemy title is gone).
4. **Long metric name** (if/when one exists) ellipsis-trims within the left region without shoving the right cluster.
5. **No secondary** → no secondary-label cell; total stays over the value column, cog over percent.
6. **Clear the primary** → left shows just `(m:ss)`, right shows only the cog; body is the backdrop (no rows). Re-pick a metric from the right-click popup → identity + rows return.
7. Change **row height / font** and **resize width** → header and rows stay pixel-aligned (the shared `NumberWidth`/`PercentWidth` cells); no drift, no leftover title-reserve gap.

## Self-review

**Spec coverage** (SPEC §Header redesign → tasks):
- Left = `(duration) metric` identity, encounter name gone → Task 1 (drop Title) + Task 2 Steps 2-3. ✓
- Right cluster = fixed cells (secondary label / total / cog) aligned 1:1 via shared widths → Task 2 Step 4. ✓
- Retire strongest-enemy title read + mid-fight flip → Task 1 (EncounterProbe + engine + DTOs). ✓
- Retire title-reserve trimming machinery → Task 2 Steps 5-7 (remove `UpdateTitleMaxWidth` + all call sites). ✓
- Cleared primary → duration-only left + cog-only right, body backdrop → preserved by `SetHeaderLabel` collapse (Task 2 Step 8 comment) + existing empty-frame path. ✓
- §Two text tiers (metric name white identity; duration/secondary/cog subordinate) → unchanged brushes; `_metricText` stays white (`dim:false`), duration/secondary/cog stay `TextLabel` (`dim:true`). ✓

**Placeholder scan:** none — every code step carries the exact code; commands carry expected output.

**Type consistency:** `MeterFrame.MetricLabel` (rendered into `_metricText`) is unchanged; `_metricText` moves from `metricCluster` to `leftCluster` with no signature change. `_secondaryLabelText.Width`/`TextAlignment` set in both the ctor (Step 4) and `SetFont` (Step 6) use the same `MeterColumns.NumberWidth(style, style.RowText)`. `UpdateTitleMaxWidth` is removed in Step 7 and no surviving call references it (Render Step 5, SetFont Step 6, ctor + SetRowWidth Step 7).
