---
name: generate-user-docs
description: Use when regenerating eq2auras's player-facing feature wiki — at a stable promotion, or after editing the generator's page manifest or style guide.
---

# generate-user-docs

Regenerates the player-facing feature wiki (`eq2auras.wiki.git`) from `docs/SPEC.md`. The wiki is a generated projection of the SPEC — never hand-edited. You author nothing by hand here: you run this, review the diff, and push.

**REQUIRED — follow both references for every page:** `references/page-manifest.md` (which SPEC sections feed which page) and `references/style-guide.md` (voice, depth, structure — match its golden examples).

## Run steps

1. **Resolve the source revision.** The maintainer gives the promoted version (e.g. `1.4.0`). Read the SPEC from that release's commit, never the working tree:
   - `git -C <eq2auras-repo> fetch --tags --force origin` **first** — the `vX.Y.Z` tags are minted remotely by the promote workflow, so an existing clone from before a promotion won't have the new tag until it force-fetches (a plain `git fetch` won't add a tag that points at an already-local commit), and without this the reads below can fail with `invalid object name 'v<VERSION>'`.
   - `git -C <eq2auras-repo> show v<VERSION>:docs/SPEC.md` is the source text.

2. **Determine scope — full vs. incremental.**
   - **Full** (bootstrap, or `--full`): regenerate every manifest page.
   - **Incremental** (default at a promotion): read the wiki's `.generated-from` marker — it records the `vX.Y.Z` **and a content hash over *both* references (the manifest and the style guide)** last generated from. Incremental is safe **only if both references are unchanged** since then: if the current references hash differs, fall back to `--full` — a manifest edit can add / remove / re-source pages, and a style-guide edit changes the voice of *every* page, and **neither is visible in a SPEC diff** (the same blind spot that motivates hashing the manifest applies verbatim to the style guide). When both references match: `git -C <repo> diff v<LAST>..v<VERSION> -- docs/SPEC.md`, map each changed section to its page(s) via the manifest, and regenerate **only** those pages; unchanged pages stay byte-identical.

3. **Generate** each page from the manifest + style guide.
   - **Section resolution — by header prefix:** each manifest entry names the leading part of a `###` header; resolve it to the section whose header **begins with** that string. A prefix must resolve to **exactly one** `###` header — manifest entries are authored to be unambiguous. If a prefix matches **more than one** header (the SPEC has genuine duplicates, e.g. `### Goal` appears twice), that is a manifest-authoring error: **stop and report it**, never silently take the first match.
   - **Absent-section rule:** the manifest is version-static but the source is *pinned*, so a mapped section's `###` header may be absent at `v<VERSION>` (a feature added later). Key on the **header, not the body**: if no header prefix-matches, **skip it and log** `skipped <section> — not in v<VERSION>`; never emit an empty page or a stub. A section that is present but thinner in body still resolves. A page whose sources are all absent is not written.

4. **Write** the pages into a local clone of `eq2auras.wiki.git` (`git clone git@github.com:amdrake93/eq2auras.wiki.git` — SSH, not HTTPS; the dev machine authenticates to GitHub over SSH), then **prune orphans** — delete any wiki **`.md` page** not in the current manifest's page list (a "page" is a `.md` file; the `.generated-from` marker is not a page and is never pruned). Update `.generated-from` to the `v<VERSION>` + the references hash (manifest + style guide), and regenerate `Home.md` + `_Sidebar.md` from the manifest's page list.

5. **Hand off — do NOT push.** Print the wiki-repo `git status` / `git diff --stat` and stop. The maintainer reviews the diff (coverage, accuracy vs. the SPEC, voice, no screenshots, no internals) and pushes manually to the wiki's default branch, **`master`** (GitHub wikis use `master`, not `main`).

## Invariants (non-negotiable)

- **Source is the promoted `vX.Y.Z` SPEC, never live `main`.**
- **Only manifest pages are written; nothing hand-maintained exists in the wiki to clobber.**
- **The wiki always equals a full projection of the manifest at `v<VERSION>`** — incremental only optimizes *how* it gets there (a change to **either reference** — manifest or style guide — forces a full pass; orphan pages absent from the manifest are pruned every run).
- **A manifest section absent at the pinned revision is skipped + logged, never emitted empty.**
- **No screenshots, no image references, ever.**
- **The skill stops before push; the maintainer reviews and pushes.**
