# Session chronicle — Parse Meter slice 2b (single secondary data point) + header/row polish

*2026-07-17 → 07-18. Brainstorm → spec → plan → implement → ship → field polish, all one session, every phase gated by Alex. Shipped to `dev-latest 0.1.135`, field-verified "looks good." Styling deferred to a dedicated overhaul (the next thread).*

## What shipped

One optional **per-window secondary metric** on each meter row — a `None`-default dropdown in the ⚙ settings window (`MeterWindowConfig.secondaryKey`), computed by `MeterEngine.Tick` into the shipped `secondaries` list, rendered as a **muted, right-aligned, font-measured column**. Plus a jitter-proof fixed-column layout, the header total capping the value column, the cog moved to the header's left, and the K/M/B formatter moved to **3 significant figures**. See SPEC Part III §The metric registry / §Header / §Configuration / §Multiple windows / §Rows / §Settings; plan `docs/plans/2026-07-17-meter-slice2b-secondary.md`.

## The why-chains (the story the docs don't tell)

- **Multi → single secondary.** The brainstorm opened assuming a *multi-select of N secondaries* (the backlog's own framing). Alex cut it to **one**: "a primary metric + one singular other data point." That collapsed a pile of open questions (ordering, "how many is too busy," per-secondary layout) and simplified the config to a single nullable key. The `secondaries` DTO stays a **list** (0-or-1) — future-proof for N, but this slice is deliberately singular.

- **The crux was alignment, not "extra text."** The moment secondaries went on the row, Alex flagged the real problem: numbers **jitter horizontally** as digit counts change (`9%`→`100%`, `98K`→`1.24M`) because the trailing area was a right-aligned *stack*. He also nailed the constraint: we can't hardcode widths (font is a per-window knob). Resolution: **font-measured fixed columns** (`MeterColumns`), each reserved at its worst-case string, right-aligned, recomputed on font change. His idea, made precise.

- **"Total caps the column it sums."** Alex's spark: align the header total directly above the row's value column so it reads as the column's sum. This drove the cog to the header's **far left** (to free the right edge) and became a load-bearing design point.

- **Formatter: `0.##` → 3 sig-figs.** Alex proposed two fixed decimals ("max 5 chars, win-win"). Checked against the code: fixed decimals *widen* with magnitude (`999.99M` = 7 chars). His real intent — "1.24M, precise, capped" — is **significant figures** (`1.24M`→`12.4M`→`124M`, 5-char cap). Corrected to sig-figs; the reserve column *shrank* while precision rose.

- **Don't restrict reversible states.** Secondary == primary? Alex: "give it to them" — it renders twice, harmless and reversible. We restrict only *irreversible* states (closing the last window). No suppression logic. Simpler code, simpler UX.

- **Review loops, twice each.** Spec review (automated, isolated reviewer w/ worktree): round-1 **Important** finding — the fixed-column invariant was undefined for *count* metrics (uncapped plain integers); resolved with a uniform 5-char reserve (`max(9.99M, 99999)`) + the "configured size wins, clips" rule. Plan review: round-1 **Minor** — a header edit-boundary that stranded a stale comment. Both closed to approval.

- **Field polish → column-order reversal.** First on-box look: cog off-center (a stray leading space in the ⚙ glyph), orphaned `— DPS` (the star-title floated the metric right on short titles → left-packed cluster + live title `MaxWidth`), and — the real one — Alex wanted the **percent back on the right**: shipped `[secondary][percent][value]` → **`[secondary][value][percent]`**. That reopened the total-alignment (now inset by the percent-column width to keep capping the value column). Alex pushed back on my overcomplicated framing — "just use the absolute widths and right-align" — and he was right; it *is* trivial. Fix-flow (spec+code), **review skipped at his call** (beta on-box is the verification).

## Process notes (keep honoring)

- **Visual companion earned its keep 3×**: the row-column jitter mockup, the dropdown-styling before/after, and the reorder layout (terminal ASCII mangled the alignment — "let me show you rendered" is the move for any column/spacing question).
- Automated review loops spawned only on Alex's explicit per-artifact invocation; fix-flows can skip at his call.
- Execution inline (Alex watches), strict Core TDD (179 green), Plugin transcribe-only gated by CI compile — the header `MaxWidth` reserve is a heuristic that only truly verified on the box.

## Next thread — styling overhaul (deferred, on purpose)

Throughout, styling was **deliberately deferred**. The settings-window `Secondary` dropdown ships as a **raw light WPF `ComboBox`** in the dark window; the right-click menu is still "archaic ACT." Alex's call: styling control-by-control **paints us into a corner** — do it as ONE coherent dark-chrome/theme pass. Folded in: his aside that config could be **inline** rather than "constantly spawning windows" (the cog spawns a settings window; New spawns meter windows). That's a settings-surface question for the overhaul's own brainstorm — visual, mockups will earn their keep. Backlog: **"Styling centralization effort."**
