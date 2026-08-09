# Session chronicle — the competitor went open-source, so we built a reviewer and shipped 1.4

*2026-08-09. One long session, three linked threads. It opened by re-examining the "competitor" from 2026-08-03 — now that they'd open-sourced, we could finally read the source and settle whether they'd copied us (they hadn't). That turned into Alex deciding he'd rather **join** them than out-run them, which surfaced a gap in our own toolchain, which we spent the afternoon filling: a spec/plan review skill. Then — pivoting hard — we promoted eq2auras to **1.4** through the versioned-release flow's first live run, and paid for it with a four-round token gauntlet. The through-line, in hindsight: the same bug wearing three costumes.*

## Part I — reading the competitor's actual code, and choosing to join

The EQ2 parser teased on a Russian guild's stream (see `2026-08-03_competitor-recon-and-threat-feasibility.md`) went public. Alex handed over the repo (`VortexUK/EQ2Parser`) and we read it against source instead of inferring:

- **No code lifted from eq2auras.** Zero hits on our distinctive identifiers; different naming throughout; it targets **.NET 10 as a standalone app**, not an ACT plugin. Architecturally a *different bet* — you can't meaningfully copy an ACT-plugin's code into a from-scratch parser. Their class inference is Census-derived (their own `build_spell_classes.py`), pet detection a port of *their* website's pipeline. They even ship a "cleanroom" rule — aimed at not lifting **ACT's** sources, nothing to do with us.
- The read that changed the day: they've built the infrastructure that's hard to bootstrap solo — a modern engine + the EQ2 Lexicon site (the "WarcraftLogs someday" shape). Alex's edge is overlay expertise on a clean engine he can shape. So the question flipped from "are they a threat" to **"should I contribute overlays there instead of running eq2auras solo?"** — parked for a dedicated conversation, captured in memory `eq2parser-and-review-skill.md`.

## Part II — the missing reviewer (a skill, built by dogfooding itself)

Contributing means working in repos that don't share our two-session writer/reviewer contract, so Alex wanted that contract **portable** — a skill. Framing it exposed the real gap it fills: **superpowers reviews *code* (`requesting-`/`receiving-code-review`) but never the *specs and plans* that `brainstorming`/`writing-plans` produce** — exactly where an unexamined assumption is cheapest to catch. So `requesting-design-review` is the design-time sibling: an isolated review of a spec/plan, feeding `receiving-code-review`.

We built it through its own process — brainstorm → spec → plan → implement — and had a Fable-5 reviewer verify each artifact via the very isolation contract the skill formalizes. Two things worth remembering:

- **The process caught a flaw in its own spec, mid-flight.** A reviewer's audit file overwrote a prior effort's `round-1.md` — because the audit-dir naming wasn't unique and round writes weren't append-only. That's a real defect in the contract we were skillifying; we amended spec + plan (unique dir per effort, append-only lowest-unused `round-N`) and the fix's *first use* was preventing a repeat of the exact collision that surfaced it.
- **You can't get a clean RED baseline for a review skill inside the repo that invented reviewing.** Every subagent inherits eq2auras's `CLAUDE.md`, which teaches the discipline — so "without the skill" agents weren't naive. The fix, and Alex's call: build/test it rooted in his **EQ2Parser fork** (a repo with no review doc), under an `amdrake93`-only identity guardrail (the machine's global git identity is the *work* email — a trap for any new personal repo). There the baselines came out clean: self-review collapse 3/3, merge-verdict violation 4/4, feedback-block divergence 5/5→converged. A harness quirk also fell out — mid-session subagents can't discover a skill created after their session started (`verification/DISCOVERY-FINDING.md`); it's a session-lifecycle property, not a bug.

Result: `requesting-design-review` built, verified, deployed to `~/.claude/skills/`, and made box-portable. Spec/plan/verification live at `~/repos/specs/requesting-design-review/`.

## Part III — shipping 1.4, and the four-token gauntlet

With 1.3.46 field-tested and the new versioned-release + revert flow merged (but never live-run), Alex called the promotion to **1.4**. It was not one click.

- **Seed first.** The flow's revert can only target a version that has an immutable `vX.Y.Z` release, and 1.3.0 was promoted under the *old* flow — so there was no `v1.3.0` to roll back to. We backfilled it (byte-identical 1.3.0 DLL re-tagged on its source commit `c1e7f3e`) and flipped `stable` to the prerelease *pointer* the new model expects. Riders unaffected (they read the `stable` tag directly).
- **Bump + build.** `version.txt` `1.3 → 1.4` (patch resets to 0), pushed → `build.yml` cut dev-latest `1.4.0` (behavior-identical to the tested 1.3.46; only the version string differs).
- **Then the wall.** The real promote's *mint* step 403'd four ways before it worked:
  1. Actions `github.token` (a GitHub App) — **"not accessible by integration"**: App tokens can't *create* releases here (they can *update* existing ones, which is why `build.yml` was always fine).
  2. Bumping repo default workflow perms to write — no help (the run already had `Contents: write`; the token *identity* was the issue).
  3. A **fine-grained PAT** with `Contents: write` — also 403'd ("not accessible by personal access token").
  4. A **classic PAT with `repo`** — closer, but **"workflow scope may be required"**, because the release tags a commit carrying `.github/workflows/`.
  - Green only with a classic PAT holding **`repo` + `workflow`**, stored as secret `RELEASE_PAT`. That requirement is the durable lesson — recorded in memory `active-direction.md`; should also land in SPEC/backlog so it's never re-derived.

1.4.0 is now stable, `v1.4.0` is Latest, `v1.3.0` stands ready as a one-command revert anchor, and workflow perms were restored to `read`. Field-verification (ACT → "Check for updates" → 1.4.0) is Alex's.

## Part IV — one bug, three costumes

The recurring shape all afternoon was **shared mutable state across supposedly-isolated runs**: the audit-file collision the review contract had latent; the fixture contamination when concurrent baseline reps stomped a shared spec; the session-snapshot confound (subagents inheriting session-start state — ambient `CLAUDE.md`, then the skills registry). Each looked different; each was the same class. The review skill now *teaches* the audit-file half of that lesson, which is a satisfying place to land.

**Open threads:** field-test 1.4 on-box; use the review skill in a *fresh* session on a real artifact (its one unverified path); record the `RELEASE_PAT` requirement in SPEC/backlog; and the big one — the conversation about whether/how to contribute to EQ2Parser, which turns on whether their maintainer wants a configurable overlay-groups model or is committed to fixed windows.
