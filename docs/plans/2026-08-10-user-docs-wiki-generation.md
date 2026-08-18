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

### Task 1: Author the generator skill (TDD, via `superpowers:writing-skills`)

The core deliverable: a repo-scoped skill (`generate-user-docs`) encoding what to generate (the page manifest), how to voice it (the style guide), and the run process (read SPEC at the promoted commit → generate → hand off → push). It is a **technique/reference skill, not a discipline skill** — its failure mode is *wrong-shaped output* (a bad generated page), not rule-violation under pressure. Three things follow from `superpowers:writing-skills`, all load-bearing:

- **REQUIRED SUB-SKILL: `superpowers:writing-skills`.** Author it **TDD-style** — a skill you didn't *run and inspect* is untested ("Reading ≠ using"). For a generation skill the "tests" are **input→output pairs**: a real SPEC section in, a known-good player page expected out; the skill is done when generation reliably hits the target. This is *technique-skill* testing (application scenarios), not the heavy discipline pressure-scenarios/rationalization tables.
- **Form = recipe, not prohibitions.** Per `writing-skills` §"Match the Form to the Failure", *wrong-shaped output* is fixed by a **positive recipe** ("a page **IS** …") + a golden exemplar; prohibition lists measurably *backfire* on shaping problems. So the style guide leads with the recipe; hard bans (screenshots, internal terms) are a short tail.
- **`description` = triggers only.** The skill's `description` states *when to use it*, never a workflow summary — a summarized workflow makes agents follow the description instead of reading the skill (`writing-skills` SDO).

**Project-specific-skill decision (deliberate, recorded):** `writing-skills` steers project-specific conventions toward the instructions file, not a skill. Exception taken here because the generator is an invocable *multi-step process* (not a convention), too large for CLAUDE.md, and **repo-scoped** (`.claude/skills/`, not the shared user namespace) so it never enters the generic library.

**Files:**
- Create: `.claude/skills/generate-user-docs/SKILL.md`
- Create: `.claude/skills/generate-user-docs/references/page-manifest.md`
- Create: `.claude/skills/generate-user-docs/references/style-guide.md`
- Scratch (session scratchpad, **not committed**): the two expected-output exemplars + the baseline / with-skill generation outputs used as the tests.

**Interfaces:**
- Produces: an invocable skill `generate-user-docs` whose run steps a later maintainer (or Task 4) follows. No code interface; the "interface" is the manifest's page set (`Home`, `_Sidebar`, `Timer-Overlay`, `Parse-Meter`) and the `.generated-from` marker convention (below), which Task 4 consumes.

- [ ] **Step 1 — RED: define the tests, watch a naive attempt fail.**

  - Pick **two representative SPEC sections** as test inputs, spanning both overlays and different shapes: `§Escalation is driven by ACT's `WarningValue`` (Timer — a behavioral "no config needed" feature) and `§The meter window` (Meter — an interactive, multi-surface feature). Read them at the pinned release: `git show v1.4.0:docs/SPEC.md`.
  - **Hand-write the expected output** for each — the good player page/section it should yield (concise, glanceable; *what it is* → *how you use/set it*; zero internals). These exemplars are the acceptance bar and seed the style guide's golden example. **Sanity-check them with Alex** — "good docs" is the maintainer's call.
  - **Baseline (watch it fail):** in a **fresh subagent**, generate each section as player docs with only a naive instruction ("rewrite this SPEC section as player how-to") — *no skill*. Record the divergences from the exemplar **verbatim**: restating the SPEC, leaked terms (`CombatantData` / engine names), dev voice, over-length, invented features. These exact failures are what the skill must fix.

- [ ] **Step 2 — GREEN: write `SKILL.md` (frontmatter + run process + invariants).**

**`description` — triggers only, no workflow summary:** e.g. *"Use when regenerating eq2auras's player-facing feature wiki — at a stable promotion, or after a SPEC change to a documented feature."* NOT "…reads the SPEC, generates pages, and pushes" — a summarized workflow makes agents follow the description instead of reading the skill (`writing-skills` SDO).

Body — the run process. The body MUST specify, in order:

1. **Resolve the source revision.** The maintainer supplies the promoted version (e.g. `1.4.0`); the skill reads the SPEC from that release's commit, never the working tree:
   - **Fetch tags first** — `git -C <eq2auras-repo> fetch --tags --force origin`. The `vX.Y.Z` tags are minted *remotely* by the promote workflow, so a local clone won't have them until fetched; without this, the `git show`/`git diff` below fail with `invalid object name 'v<VERSION>'`.
   - `git -C <eq2auras-repo> show v<VERSION>:docs/SPEC.md` is the source text. (The `vX.Y.Z` tag exists per SPEC §"A promotion mints a permanent versioned release.")
2. **Determine scope — full vs. incremental.**
   - **Full** (bootstrap, or `--full`): regenerate every page in the manifest.
   - **Incremental** (default at a promotion): read the wiki's `.generated-from` marker (the `vX.Y.Z` the wiki was last generated from — a plain-text file at the wiki root written by every run). Diff the two SPEC revisions — `git -C <repo> diff v<LAST>..v<VERSION> -- docs/SPEC.md` — map each changed section to its page(s) via the manifest, and **regenerate only those pages**. Pages whose source sections are unchanged are left **byte-identical** (this is what kills non-deterministic-LLM churn on unchanged docs and targets only what moved — the plan-watch mechanism).
3. **Generate**, per page, following `references/page-manifest.md` (which SPEC sections feed the page) and `references/style-guide.md` (voice, structure, no screenshots, no internals). **Section resolution — by header prefix:** each manifest entry names the *leading* part of a `###` header; resolve it to the SPEC section whose header **begins with** that string. Prefixes are chosen unambiguous, so a header's descriptive suffix (`— warning-window semantics`, `: session-stable palette assignment`) need not be copied verbatim and won't break resolution if it later changes. **Absent-section rule (load-bearing):** the manifest is version-static but the source is a *pinned* revision, so a mapped section's **`###` header may not exist** at `v<VERSION>` — when a feature's SPEC section was *added after* that version. The rule keys on the **header, not the body**: if no `###` header prefix-matches the manifest name, **skip it and log `skipped <section> — not in v<VERSION>` — never emit an empty page or a stub section.** (A section whose header *is* present but whose body is thinner at the older revision — a feature later fleshed out *within* an existing section, e.g. the segment picker inside `§Segments mirror ACT's encounter list` — still resolves and generates; only a genuinely absent header skips.) A page whose sources are *all* absent is not written at all (that version doesn't have the feature).
4. **Write** the pages into a local clone of `eq2auras.wiki.git` (`git clone https://github.com/amdrake93/eq2auras.wiki.git`), update `.generated-from` to `v<VERSION>`, and regenerate `Home.md` + `_Sidebar.md` from the manifest's page list.
5. **Hand off — do NOT push.** Print the wiki-repo `git status` / `git diff --stat` and stop. The maintainer reviews the diff (coverage, accuracy vs. SPEC, player voice, no screenshots, no internals) and pushes manually. State this explicitly: the skill never pushes; the maintainer is the review gate on AI prose.

Invariants to state in `SKILL.md` (bold, non-negotiable):
- **Source is the promoted `vX.Y.Z` SPEC, never live `main`.**
- **Only manifest pages are written; nothing hand-maintained exists in the wiki to clobber.**
- **A manifest section absent at the pinned revision is skipped + logged, never emitted empty.**
- **No screenshots, no image references, ever.**
- **The skill stops before push; the maintainer reviews and pushes.**

- [ ] **Step 3 — GREEN: write `references/page-manifest.md` — SPEC sections → wiki pages (shipped features only).**

An explicit table. Initial page set is two feature pages plus the generated index/nav:

```
Page: Timer-Overlay.md  ← §The core loop; §Timer groups: N instances of one pipeline;
                          §Escalation is driven by ACT's `WarningValue`;
                          §The timer lifecycle; §The escalated radial pie; §The Overdue visual;
                          §The center escalation zone; §Configuration: the knob model;
                          §Moving the overlay: unlock/move mode; §Element dimensions;
                          §Window growth: per-window grow direction; §Timer colors; §Typography: per-panel font
Page: Parse-Meter.md    ← §The metric registry; §The meter window; §Deaths & the Death Recap;
                          §Class colors; §The hover surface; §Segments mirror ACT's encounter list
Page: Home.md           ← generated index of the above (derived from this manifest's page list)
Page: _Sidebar.md       ← generated nav (derived from this manifest's page list)
```

**Both the manifest sources above and the EXCLUDE list below name header *prefixes*** (a name resolves to the SPEC section whose `###` header begins with it — see the run-process section-resolution rule in Step 2). Sub-features live *inside* their section, not as separate entries: `§The meter window` covers the multiple-windows / right-click-menu / ⚙-settings / row-drill-down surfaces, and `§Segments mirror ACT's encounter list` covers the segment picker.

Then an explicit **EXCLUDE** list naming the SPEC sections the manifest must never map (they are internals/meta, not player features): `§Architecture: shared core + feature modules`, `§Packaging`, `§Platform facts`, `§The theme system`, `§The one hard constraint`, `§The one data rule`, `§The shared rendering substrate`, `§Assembly split & polling`, `§Slice map`, every `§Testing strategy …`, `§Development & test cycle`, `§Release channels & public distribution`, `§Resolved by the Phase-0 spike`, `§Roadmap`, `§Open decisions`. (The "Forward-compatible vocabulary" material is a bolded paragraph *inside* `§The theme system`, already excluded — it is not its own section, so it is not listed here.)

State the rule above the table: **if a SPEC section is not listed as a page source here, it is not player-facing and is not generated** — a new shipped feature is added to this manifest deliberately, not picked up automatically (so unbuilt/spec-first sections never leak into player docs even at the promoted commit).

- [ ] **Step 4 — GREEN: write `references/style-guide.md` as a recipe.**

Lead with the **positive recipe** — the primary form (per `writing-skills` §"Match the Form to the Failure", recipes beat prohibitions for shaping output):
- **A page IS:** a one-line *what this is*, then per feature — a heading + 1–3 short paragraphs (*what it does* → *how you turn it on / set it*). Short sections, one per feature; name the knobs a player actually sets, not every internal default.
- **Audience:** an EQ2 player using the overlay, not a developer.
- **Golden exemplar (include verbatim):** one Step-1 expected output, as the shape to match. Seed it from this transform — SPEC's "Escalation is driven by ACT's `WarningValue`…" → "As an ability gets close to ready, its timer grows and brightens so the urgent ones stand out at a glance — no config needed; it follows the warning time your ACT trigger already sets."

Then a short **never** tail (bans, secondary to the recipe): image markdown / screenshots; "SPEC says…"; internal terms (`CombatantData`, `MasterSwing`, `EncounterData`, engine/file names); exhaustive every-knob tables.

- [ ] **Step 5 — GREEN-verify: re-generate the tests with the skill.**

  - Regenerate the two Step-1 sections **with** the skill (a fresh subagent given the skill). Compare each to its exemplar; **grep the output** for `CombatantData`, `MasterSwing`, `EncounterData`, `SPEC`, `![` → expect **none**; check length + voice against the recipe.
  - Expected: the Step-1 baseline failures are gone and each output matches its exemplar's shape.

- [ ] **Step 6 — REFACTOR: close gaps.**

  - Any remaining divergence (over-length, a leaked term, a missed how-to, an invented feature) → tighten the recipe / `SKILL.md` → regenerate → recompare. Iterate until **both** sections reliably produce exemplar-quality output. Record any recurring failure as an explicit recipe line (the loophole-closing step).

- [ ] **Step 7 — structural + `writing-skills` conformance checks.**

Run these and confirm each:
- The skill **loads**: `ls .claude/skills/generate-user-docs/` shows `SKILL.md` + both references (and it appears in a fresh Skill-tool listing).
- **Coverage:** every feature named in the SPEC amendment's feature list (Timer Overlay: escalation, knobs, timer groups, unlock/move placement; Parse Meter: metrics + scope, multiple windows, right-click menu + ⚙ settings, drill-down, Deaths & Recap, hover cards, class colors, segment picker) maps to a page in `page-manifest.md`.
- **Scoping:** no EXCLUDE-list section is referenced as a page source.
- **`writing-skills` conformance:** `description` is triggers-only (no workflow summary); the style guide is recipe-form with a golden exemplar; `SKILL.md` states the invariants (promoted-commit source, no-screenshots, stop-before-push).

- [ ] **Step 8 — Commit.**

```bash
git add .claude/skills/generate-user-docs/
git commit -m "Add generate-user-docs skill (TDD-authored via writing-skills): SPEC→wiki generator — recipe style-guide + manifest + promoted-commit source"
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
