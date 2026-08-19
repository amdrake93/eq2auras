# Page manifest — SPEC sections → wiki pages

**If a SPEC `###` section is not listed here as a page source, it is not player-facing and is not generated.** A new shipped feature is added to this manifest deliberately, never picked up automatically — so unbuilt / spec-first / internal sections never leak into player docs, even at the promoted commit.

Both the sources below and the EXCLUDE list name **header prefixes**: a name resolves to the SPEC section whose `###` header *begins with* it (so a header's descriptive suffix — `— warning-window semantics`, `: session-stable palette assignment` — needn't be copied and won't break resolution if it later changes). Each prefix must resolve to **exactly one** `###` header, so author entries to be unambiguous — the SPEC has genuine duplicate headers (e.g. `### Goal` appears twice), so a too-short prefix can collide; a multi-match is an authoring error, not a silent first-match (SKILL.md run step 3).

## Pages

```
Page: Timer-Overlay.md  ← §The core loop; §Timer groups: N instances of one pipeline;
                          §Escalation is driven by ACT's `WarningValue`;
                          §The timer lifecycle; §The escalated radial pie; §The Overdue visual;
                          §The center escalation zone; §Configuration: the knob model;
                          §Moving the overlay: unlock/move mode; §Element dimensions;
                          §Window growth: per-window grow direction; §Timer colors; §Typography: per-panel font
Page: Parse-Meter.md    ← §The metric registry; §The meter window; §Deaths & the Death Recap;
                          §Class colors; §The hover surface; §Segments mirror ACT's encounter list
Page: Home.md           ← generated index of the above (derived from this page list)
Page: _Sidebar.md       ← generated nav (derived from this page list)
```

Sub-features live *inside* their section, not as separate entries: `§The meter window` covers the multiple-windows, right-click menu, ⚙ settings, and row-drill-down surfaces; `§Segments mirror ACT's encounter list` covers the segment picker.

## Exclude (internals / meta — never mapped)

The rule at the top of this file is authoritative: **anything not listed as a page source is not generated** — whether or not it appears in this list. So this list is **not exhaustive**; it explicitly calls out the internal/meta sections most likely to be mistaken for player-facing, so a future editor doesn't map them by reflex. Sections that are neither a page source nor listed here (e.g. §Vision, the two §Goal sections, §Rendering technology, §Diagnostic logging) are still not generated — the same rule covers them.

`§Architecture: shared core + feature modules`, `§Packaging`, `§Platform facts`, `§The theme system`, `§The one hard constraint`, `§The one data rule`, `§The shared rendering substrate`, `§Assembly split & polling`, `§Slice map`, every `§Testing strategy …`, `§Development & test cycle`, `§Release channels & public distribution`, `§Resolved by the Phase-0 spike`, `§Roadmap`, `§Open decisions`.

(The "Forward-compatible vocabulary" material is a bolded paragraph inside `§The theme system`, already excluded — not its own section.)
