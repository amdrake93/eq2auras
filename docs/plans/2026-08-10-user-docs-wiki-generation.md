# User-Facing Docs — Generated Feature Wiki: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up the player-facing feature/usage wiki as a text projection of the SPEC, produced by a repo-scoped generator skill and regenerated (never hand-edited) at each stable promotion.

**Architecture:** A repo-scoped Claude skill (`.claude/skills/generate-user-docs/`) reads the SPEC **at the promoted `vX.Y.Z` source commit**, re-voices its shipped feature sections into player how-to prose (text-only, no screenshots), and writes markdown pages into a local clone of the separate `eq2auras.wiki.git` repo for the maintainer to review and push. The main repo carries only the skill + a README link; the generated pages live in the wiki repo. README and `docs/install.md` remain hand-maintained in-repo (overview / install), so the wiki is 100% generated and its whole upkeep is "regenerate."

**Tech Stack:** Markdown only. A Claude skill (`SKILL.md` + `references/`), GitHub wiki (`eq2auras.wiki.git`, a separate markdown git repo), `git`, `gh`. **No C#, no Core, no build, no unit tests.**

## Global Constraints

Copied verbatim from the SPEC amendment (§Development & test cycle — "Feature docs — the generated wiki") and CLAUDE.md. Every task implicitly includes these.

- **Wiki = generated feature pages only.** Overview + getting-started stay in the README; install/update in `docs/install.md`; both hand-maintained in-repo. Nothing hand-maintained lives in the wiki.
- **Text-only. No screenshots** in any generated page (per-feature shots can't stay honest across regen; a shot would need the Windows box, blocking future automation). General "hero" shots, if ever, live in the README — never the wiki.
- **Source = the SPEC at the promoted `vX.Y.Z` source commit**, never live `main`. The SPEC is design-truth (describes the system *as designed*, incl. unbuilt — SPEC Status line), and a promotion can pin bytes older than `main` HEAD, so live-`main` would leak unshipped/newer behavior into player docs.
- **The page manifest maps only shipped feature sections** — never the roadmap, forward-compatible scaffolding, testing-strategy, assembly-split, dev-cycle, release-channels, or open-decisions sections (those are internals/meta, not player features).
- **Regeneration is maintainer-run**, paired with the promote `workflow_dispatch`; the maintainer reviews the wiki-repo `git diff` before pushing (the human review gate on AI prose). CI automation is deferred (not in this plan).
- **The generator is a repo-scoped skill** (eq2auras-specific → travels with the repo), distinct from the generic user-scoped `requesting-design-review` skill.
- **No Core code.** This plan authors markdown artifacts and runs a generation; there is no `dotnet test` cycle. "Verification" is structural checks (coverage, scoping, rendering) + maintainer review.
- **Branch:** `user-docs-wiki` (carries the SPEC amendment already). No ticket prefix. Merge to `main` only on Alex's explicit call.

---

### Task 1: Author the generator skill

The core deliverable: a repo-scoped skill that encodes what to generate (the page manifest), how to voice it (the style guide), and the run process (read SPEC at the promoted commit → generate → hand off for review → push). The plan-watch item lands here concretely.

**Files:**
- Create: `.claude/skills/generate-user-docs/SKILL.md`
- Create: `.claude/skills/generate-user-docs/references/page-manifest.md`
- Create: `.claude/skills/generate-user-docs/references/style-guide.md`

**Interfaces:**
- Produces: an invocable skill `generate-user-docs` whose run steps a later maintainer (or Task 4) follows. No code interface; the "interface" is the manifest's page set (`Home`, `_Sidebar`, `Timer-Overlay`, `Parse-Meter`) and the `.generated-from` marker convention (below), which Task 4 consumes.

- [ ] **Step 1: Write `SKILL.md` — the run process + invariants.**

Frontmatter + body. The body MUST specify, in order:

1. **Resolve the source revision.** The maintainer supplies the promoted version (e.g. `1.4.0`); the skill reads the SPEC from that release's commit, never the working tree:
   - **Fetch tags first** — `git -C <eq2auras-repo> fetch --tags --force origin`. The `vX.Y.Z` tags are minted *remotely* by the promote workflow, so a local clone won't have them until fetched; without this, the `git show`/`git diff` below fail with `invalid object name 'v<VERSION>'`.
   - `git -C <eq2auras-repo> show v<VERSION>:docs/SPEC.md` is the source text. (The `vX.Y.Z` tag exists per SPEC §"A promotion mints a permanent versioned release.")
2. **Determine scope — full vs. incremental.**
   - **Full** (bootstrap, or `--full`): regenerate every page in the manifest.
   - **Incremental** (default at a promotion): read the wiki's `.generated-from` marker (the `vX.Y.Z` the wiki was last generated from — a plain-text file at the wiki root written by every run). Diff the two SPEC revisions — `git -C <repo> diff v<LAST>..v<VERSION> -- docs/SPEC.md` — map each changed section to its page(s) via the manifest, and **regenerate only those pages**. Pages whose source sections are unchanged are left **byte-identical** (this is what kills non-deterministic-LLM churn on unchanged docs and targets only what moved — the plan-watch mechanism).
3. **Generate**, per page, following `references/page-manifest.md` (which SPEC sections feed the page) and `references/style-guide.md` (voice, structure, no screenshots, no internals). **Absent-section rule (load-bearing):** the manifest is version-static but the source is a *pinned* revision, so a mapped section may be **missing or renamed** at `v<VERSION>` — e.g. a feature that shipped *after* this version, or a section renamed since (the SPEC's Part III→IV renumbering is a real example between `v1.4.0`'s pinned tree and current `main`). Resolve each manifest section in the pinned SPEC by its **exact header**; **if it is not present, skip it and log `skipped <section> — not in v<VERSION>` — never emit an empty page or a stub section.** A page whose sources are *all* absent is not written at all (that version doesn't have the feature).
4. **Write** the pages into a local clone of `eq2auras.wiki.git` (`git clone https://github.com/amdrake93/eq2auras.wiki.git`), update `.generated-from` to `v<VERSION>`, and regenerate `Home.md` + `_Sidebar.md` from the manifest's page list.
5. **Hand off — do NOT push.** Print the wiki-repo `git status` / `git diff --stat` and stop. The maintainer reviews the diff (coverage, accuracy vs. SPEC, player voice, no screenshots, no internals) and pushes manually. State this explicitly: the skill never pushes; the maintainer is the review gate on AI prose.

Invariants to state in `SKILL.md` (bold, non-negotiable):
- **Source is the promoted `vX.Y.Z` SPEC, never live `main`.**
- **Only manifest pages are written; nothing hand-maintained exists in the wiki to clobber.**
- **A manifest section absent at the pinned revision is skipped + logged, never emitted empty.**
- **No screenshots, no image references, ever.**
- **The skill stops before push; the maintainer reviews and pushes.**

- [ ] **Step 2: Write `references/page-manifest.md` — SPEC sections → wiki pages (shipped features only).**

An explicit table. Initial page set is two feature pages plus the generated index/nav:

```
Page: Timer-Overlay.md  ← SPEC §The core loop; §Timer groups: N instances of one pipeline;
                          §Escalation is driven by ACT's WarningValue;
                          §The timer lifecycle; §The escalated radial pie; §The Overdue visual;
                          §The center escalation zone; §Configuration: the knob model;
                          §Moving the overlay: unlock/move mode; §Element dimensions;
                          §Window growth: per-window grow direction; §Timer colors; §Typography: per-panel font
Page: Parse-Meter.md    ← SPEC §The metric registry; §The meter window (multiple windows, right-click
                          menu, ⚙ settings, row drill-down); §Deaths & the Death Recap;
                          §Class colors; §The hover surface; §Segments mirror ACT's encounter list (segment picker)
Page: Home.md           ← generated index of the above (derived from this manifest's page list)
Page: _Sidebar.md       ← generated nav (derived from this manifest's page list)
```

Then an explicit **EXCLUDE** list naming the SPEC sections the manifest must never map (they are internals/meta, not player features): `§Architecture: shared core + feature modules`, `§Packaging`, `§Platform facts`, `§The theme system`, `§The one hard constraint`, `§The one data rule`, `§The shared rendering substrate`, `§Assembly split & polling`, `§Slice map`, every `§Testing strategy …`, `§Development & test cycle`, `§Release channels & public distribution`, `§Resolved by the Phase-0 spike`, `§Roadmap`, `§Open decisions`. (The "Forward-compatible vocabulary" material is a bolded paragraph *inside* `§The theme system`, already excluded — it is not its own section, so it is not listed here.)

State the rule above the table: **if a SPEC section is not listed as a page source here, it is not player-facing and is not generated** — a new shipped feature is added to this manifest deliberately, not picked up automatically (so unbuilt/spec-first sections never leak into player docs even at the promoted commit).

- [ ] **Step 3: Write `references/style-guide.md` — the player voice.**

Concrete rules, with a before/after example transforming a SPEC sentence into player prose:
- **Audience:** an EQ2 player using the overlay, not a developer. No architecture, no class/field/file names, no ACT-internals, no line numbers.
- **Voice:** concise, glanceable, task-oriented — "what it is, how to turn it on / configure it," matching the overlay's own readable-at-a-glance ethos. Short sections, one per feature.
- **Structure per page:** a one-line "what this is," then per-feature sections (heading + 1–3 short paragraphs: what it does → how to use/configure it). No exhaustive every-knob tables; name the knobs a player sets, not every internal default.
- **Hard bans:** no screenshots / image markdown; no "SPEC says…"; no internal terms (`CombatantData`, `MasterSwing`, `EncounterData`, engine/file names).
- **Worked example** (include verbatim in the file): SPEC's "Escalation is driven by ACT's `WarningValue`…" → player prose like "As an ability gets close to ready, its timer grows and brightens so the urgent ones stand out at a glance — no config needed; it follows the warning time your ACT trigger already sets."

- [ ] **Step 4: Verify the skill is complete and correctly scoped (structural check — no unit test).**

Run these checks and confirm each:
- The skill loads: it appears in the available-skills list on a fresh Skill-tool listing (or `ls .claude/skills/generate-user-docs/` shows `SKILL.md` + both references).
- **Coverage:** every feature named in the SPEC amendment's feature list (Timer Overlay: escalation, knobs, timer groups, unlock/move placement; Parse Meter: metrics + scope, multiple windows, right-click menu + ⚙ settings, drill-down, Deaths & Recap, hover cards, class colors, segment picker) maps to a page in `page-manifest.md`.
- **Scoping:** spot-check that no EXCLUDE-list section is referenced as a page source.
- **Invariants present:** `SKILL.md` states the promoted-commit source rule, the no-screenshots ban, and the stop-before-push gate.

Expected: all four hold. Fix the manifest/style-guide/SKILL.md inline if any gap.

- [ ] **Step 5: Commit.**

```bash
git add .claude/skills/generate-user-docs/
git commit -m "Add generate-user-docs skill: SPEC→wiki generator (manifest + style guide + promoted-commit source)"
```

---

### Task 2: Enable the GitHub wiki (manual — Alex, on the box or web)

`eq2auras.wiki.git` does not exist until the wiki is enabled and given a first page (`gh repo view --json hasWikiEnabled` currently returns `false`). This is a prerequisite for Task 4's clone/push and cannot be done from the Mac toolchain.

**Files:** none in-repo (GitHub setting + wiki repo).

- [ ] **Step 1: Enable the wiki.** In the repo on GitHub: Settings → Features → tick **Wikis**.

- [ ] **Step 2: Create the first page.** Click the repo's **Wiki** tab → **Create the first page** → save a placeholder `Home` (any content — Task 4 overwrites it). This is what materializes `eq2auras.wiki.git`.

- [ ] **Step 3: Verify.** `gh repo view amdrake93/eq2auras --json hasWikiEnabled` → `true`; `git clone https://github.com/amdrake93/eq2auras.wiki.git /tmp/eq2auras-wiki` succeeds.

No commit (nothing in the main repo changes).

---

### Task 3: Add the README wiki link

The README is the front door; it must point players to the wiki for feature docs (the SPEC amendment states "the README links players to the wiki").

**Files:**
- Modify: `README.md` — the quick-links line (`README.md:5`) and/or the "Getting Started" area (`README.md:20-29`).

- [ ] **Step 1: Add the link.** In the top quick-links line (`README.md:5`), add a **using the features** link to the wiki alongside the existing install-guide / release links:

```markdown
[install guide](docs/install.md) · [using the features (wiki)](https://github.com/amdrake93/eq2auras/wiki) · [stable release](...) · ...
```

Optionally add a one-line pointer near the end of "Getting Started" (`README.md:29`): `**→ Feature guides (how to use each overlay): [the wiki](https://github.com/amdrake93/eq2auras/wiki).**`

- [ ] **Step 2: Verify.** The link renders and points at `…/eq2auras/wiki`. (The wiki must exist — Task 2 — for the link to resolve, but the README edit does not depend on Task 2 to be authored.)

- [ ] **Step 3: Commit.**

```bash
git add README.md
git commit -m "README: link players to the feature wiki"
```

---

### Task 4: First full generation + maintainer review + push

Bootstraps the wiki content by running the Task 1 skill in **full** mode against the current stable release, then the maintainer reviews and pushes. This is where Task 1's skill is validated in practice.

**Files:** the generated pages in the `eq2auras.wiki.git` clone (`Home.md`, `_Sidebar.md`, `Timer-Overlay.md`, `Parse-Meter.md`, `.generated-from`) — not in this repo.

**Interfaces:**
- Consumes: the `generate-user-docs` skill (Task 1); the enabled wiki (Task 2).

- [ ] **Step 1: Run the generator in full mode** against the current stable `vX.Y.Z` (today `v1.4.0`). Follow the skill — which **fetches tags first**, then reads `git show v1.4.0:docs/SPEC.md`. (`v1.4.0` now points at the true 1.4.0 source `2693c1d`, which carries the full Parse Meter, so both feature pages generate; the tag was corrected 2026-08-17 — see backlog.) Generate all manifest pages into a clone of `eq2auras.wiki.git`, write `.generated-from` = `v1.4.0`, regenerate `Home.md` + `_Sidebar.md`. The absent-section rule (Task 1) applies: any manifest section not present in `v1.4.0`'s SPEC is skipped + logged, never emitted empty.

- [ ] **Step 2: Verify structurally.** Each manifest page exists and is non-empty; no page contains image markdown (`![`) or internal terms (grep the clone for `![`, `CombatantData`, `MasterSwing`, `EncounterData`, `SPEC`); `_Sidebar.md` links every page; `.generated-from` reads `v1.4.0`.

- [ ] **Step 3: Maintainer review (Alex — the human gate).** Read the wiki-repo `git diff` (all-new on first run): does each feature read as accurate player how-to vs. the SPEC, in the right voice, complete, no internals, no screenshots? This is the review gate; it is not automatable and stands in for the deferred CI review.

- [ ] **Step 4: Push** (Alex, after approving). From the wiki clone: `git add -A && git commit -m "Generate feature docs from v1.4.0" && git push`. The wiki is live. (`git add -A` is fine **here** — this is the wiki clone, a separate 100%-generated repo; the eq2auras `-A` ban applies to the main repo, not this one.)

- [ ] **Step 5: Confirm rendering.** Open `https://github.com/amdrake93/eq2auras/wiki`; the sidebar + both feature pages render; the README link (Task 3) resolves to it.

No main-repo commit in this task (wiki content lives in the wiki repo).

---

## Notes for execution

- **Task order:** Task 1 (skill) and Task 3 (README) are independent and can go in either order. Task 2 (Alex enables the wiki) is a prerequisite for Task 4. Task 4 depends on Tasks 1 + 2.
- **What merges to `main`:** only Tasks 1 + 3 touch this repo (the skill + the README link) on branch `user-docs-wiki`, alongside the already-committed SPEC amendment. Task 4's output lives in the separate wiki repo; Task 2 is a GitHub setting. So the `user-docs-wiki` branch merges the SPEC amendment + the skill + the README link together.
- **Plan-watch item (from spec review, carried):** the promoted-`vX.Y.Z`-commit source read and the shipped-only manifest scope both land in Task 1 (`SKILL.md` Step 1.1 + the manifest EXCLUDE rule); the incremental release-diff targeting (churn mitigation) is Task 1 `SKILL.md` Step 2. Plan review verifies each is concrete.
