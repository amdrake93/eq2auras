# Styling / Theme System — Increment 5 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the meter header's **⚙ cog to the far right** (occupying the percent-column slot) so the **total caps the value column** — total above value, cog above percent, aligned down every row (SPEC Part III §Header). This brings the code in line with the §Header amendment; the total-caps-value *inset* already exists from slice 2b, so this is the cog relocation and the alignment bookkeeping.

**Architecture:** The header `Grid` reorders to `[left cluster (star)] [total (auto)] [cog (auto)]`. The cog's column is fixed at the **percent-column width** and right-aligned, so it sits above the rows' percent column at the far-right corner; the total's right margin drops from `PercentWidth + ColumnGap` to just `ColumnGap` (the cog now fills the percent slot the total used to inset around). The three sites that compute this geometry — ctor, `ApplyHeaderFont` (font change), `UpdateTitleMaxWidth` (trim budget) — all update together.

**Tech Stack:** C# / net472 / WPF (Plugin — transcribe; CI compile + code review + on-box field). No Core changes.

## Global Constraints

- Single-assembly; no `async`; no `System.Web.Extensions`; no `Assembly.LoadFrom`.
- **WPF: retain elements, animate properties** — this is a one-time layout change at construction/font-change, never per tick.
- The cog glyph is `"⚙"` with **no leading space** (a leading space pushed it off-center — slice 2b field fix; keep it bare).

**Branch:** continues on `styling-theme-system`.

## Scope boundaries

- Header layout only. The row columns (value/percent), the total's *width* (`NumberWidth`) and its right-alignment, and everything else are unchanged — the total already aligns above the value column via the inset; this increment relocates the cog into the percent slot and shrinks the total's now-redundant inset to a plain gap.

---

## File structure

| File | Responsibility | Change |
|---|---|---|
| `src/eq2auras.Plugin/Overlay/MeterWindow.cs` | the meter window header | **modify** — cog to far right; total margin; title-trim reserve |

---

### Task 1: Relocate the cog to the far right (transcribe)

**Files:** Modify `MeterWindow.cs` — the ctor header setup (`:110-127`), `ApplyHeaderFont` (`:374-375`), `UpdateTitleMaxWidth` (`:384-385`).

**Interfaces:** none new — internal layout.

**Verification:** WPF — CI compile + code review + field. No unit test.

- [ ] **Step 1: Reorder the header grid + set the cog's slot** — replace the total-setup + grid block (`:110-127`).

The current total setup (`:110-112`) insets the total by the percent-column width; change its right margin to a plain gap and set the cog to fill the percent-column slot:
```csharp
            _totalText.Width = MeterColumns.NumberWidth(style, style.RowText);
            _totalText.TextAlignment = TextAlignment.Right;
            _totalText.Margin = new Thickness(0, 0, MeterColumns.ColumnGap, 0);   // gap to the cog (which fills the percent-column slot)
            // The cog fills the percent-column slot at the far right, so the total to its left caps
            // the VALUE column (SPEC §Header): total over value, cog over percent, down every row.
            affordance.Width = MeterColumns.PercentWidth(style, style.RowText * 11.0 / 13.0);
            affordance.TextAlignment = TextAlignment.Right;
```
(was: `_totalText.Margin = new Thickness(0, 0, MeterColumns.PercentWidth(style, style.RowText * 11.0 / 13.0) + MeterColumns.ColumnGap, 0);` and no `affordance.Width`/`TextAlignment`.)

Then the grid block (`:116-127`) reorders — cluster first (star), total, cog last (far right):
```csharp
            // Outer header: [ (dur) title — metric (star; left cluster) ] [total (auto, above value)] [cog (auto, above percent, far right)].
            var headerGrid = new Grid { Margin = new Thickness(8 * hr, 0, 8 * hr, 0) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });  // (dur) title — metric
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });          // total (above the value column)
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });          // cog (far right, above the percent column)
            Grid.SetColumn(leftCluster, 0);
            Grid.SetColumn(_totalText, 1);
            Grid.SetColumn(affordance, 2);
            headerGrid.Children.Add(leftCluster);
            headerGrid.Children.Add(_totalText);
            headerGrid.Children.Add(affordance);
            UpdateTitleMaxWidth();   // now that total/cog widths exist, cap the title's trim budget
```
(was: three columns `[auto cog][star cluster][auto total]`, `Grid.SetColumn(affordance,0)/(leftCluster,1)/(_totalText,2)`, and `affordance.Margin = new Thickness(0, 0, 6 * hr, 0);` — that left-cog gap is removed.)

- [ ] **Step 2: Keep the geometry in sync on font change** — in `ApplyHeaderFont` (`:374-375`), the total width/margin re-set becomes:
```csharp
            _totalText.Width = MeterColumns.NumberWidth(_style, _style.RowText);
            _totalText.Margin = new Thickness(0, 0, MeterColumns.ColumnGap, 0);
            _affordance.Width = MeterColumns.PercentWidth(_style, _style.RowText * 11.0 / 13.0);
```
(was: `_totalText.Margin = new Thickness(0, 0, MeterColumns.PercentWidth(_style, _style.RowText * 11.0 / 13.0) + MeterColumns.ColumnGap, 0);` with no cog width.)

- [ ] **Step 3: Fix the title-trim reserve** — the cog is no longer in the left cluster, so `UpdateTitleMaxWidth` (`:384-385`) drops it from the left-text estimate and adds the cog's own column width:
```csharp
            double reserve = MeterColumns.TextWidth(_style, "(00:00)  — Cures ", _style.RowText)
                + _totalText.Width + _totalText.Margin.Right + _affordance.Width + 16;
```
(was: `TextWidth(_style, "⚙ (00:00)  — Cures ", _style.RowText) + _totalText.Width + _totalText.Margin.Right + 16;` — the `"⚙ "` prefix is gone from the reserve string, `+ _affordance.Width` added.)

- [ ] **Step 4: Verify** — `grep -n "affordance.Margin\|SetColumn(affordance, 0\|PercentWidth(.*+ MeterColumns.ColumnGap\|⚙ (00:00)" src/eq2auras.Plugin/Overlay/MeterWindow.cs` → no matches. The `PercentWidth(.*+ MeterColumns.ColumnGap` pattern (no `style`/`_style` prefix) catches **both** inset sites — the ctor (`style`, Step 1) and `ApplyHeaderFont` (`_style`, Step 2) — so a skipped Step 2 can't pass silently. Then `grep -n "SetColumn(affordance, 2)\|affordance.TextAlignment\|_affordance.Width = MeterColumns.PercentWidth" ...` → present at both the ctor and `ApplyHeaderFont`.

- [ ] **Step 5: Commit**
```bash
git add src/eq2auras.Plugin/Overlay/MeterWindow.cs
git commit -m "Meter header: cog to the far right (above the percent column); total caps the value column (increment 5)"
```

---

## Testing strategy

**Core (Mac):** no Core changes — run the suite once, confirm 188 hold.

**Plugin (CI):** push; the WPF plugin must compile with the reordered header grid.

**On-box field script (merge-gate):**
1. Update on `dev-latest`, open a meter with combat and a couple of rows.
2. **Cog far right:** the ⚙ sits at the header's far-right corner, directly above the rows' **percent** column; clicking it still opens the settings window.
3. **Total caps value:** the header total is right-aligned directly above the rows' **value** column (one column in from the cog), lined up with the per-row values down the list.
4. **Left cluster:** `(duration) title — metric` stays left-packed; a long mob name still ellipsis-trims without shoving the total/cog off (the title-trim budget accounts for the new layout).
5. **Font change:** ⚙ → change the font; the total and cog columns re-measure and stay aligned above value/percent.
6. Right-click still opens the popup (increment 4), timers unregressed.

## Self-review

**Spec coverage (increment 5):** §Header's "a **⚙ cog** at the **far right**, riding above the percent column" and "the total … capping the value column it sums … total and per-row values read as one aligned column" → Task 1. Plan-watch: none outstanding — this is the last increment; #1/#2/#3/#4/#5/#6 all landed across increments 1-4.

**Placeholder scan:** none — exact before/after for all three sites.

**Type consistency:** `affordance`/`_affordance` are the same `TextBlock` (`var affordance = _affordance;` in ctor); `TextBlock.Width`/`TextAlignment` are valid. `MeterColumns.PercentWidth`/`NumberWidth`/`ColumnGap` are the same members already used at these sites. `Grid.SetColumn` indices 0/1/2 match the three `ColumnDefinition`s. `UpdateTitleMaxWidth` reads `_affordance.Width` (a `double`), set in Step 1/Step 2 before it is called (ctor calls `UpdateTitleMaxWidth` after the grid block; `ApplyHeaderFont` sets the width before its `UpdateTitleMaxWidth` call at `:376`).

## Plan-watch items

None — increment 5 is the final increment of the styling arc; all six spec-review plan-watch items landed across increments 1-4.
