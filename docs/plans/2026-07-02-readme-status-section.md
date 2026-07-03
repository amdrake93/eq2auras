# README Status Section (CI-Stamped) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task, **inline in the main session** (repo convention). Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A README Status block — live Actions build badge, version/date pills, quick links — whose contents CI rewrites on every release, replacing the stale hand-written Status section.

**Architecture:** Everything between `<!-- status:begin -->` / `<!-- status:end -->` in `README.md` is machine-owned. The main-only publish path in `build.yml` gains a stamp step: rewrite the block with the just-released version + UTC date (shields.io *static* badge URLs — the only shields form that works for a private repo), commit back with `[skip ci]`, push failure-tolerantly. Spec: `docs/SPEC.md` §Development & test cycle, "README status section (CI-stamped)".

**Tech Stack:** GitHub Actions (bash step on `windows-latest`), perl one-liner for the marker-delimited replace, shields.io static badges.

## Global Constraints

- Work stays on branch `readme-status-section`; **merge to `main` = release = first live stamp** (Alex approves the merge).
- The stamp step must **never fail an already-published run** — push failures degrade to a workflow warning (spec: "failure mode is benign — a stale block, corrected by the next release").
- `[skip ci]` in the bot commit message (prevents recursion; skipped pushes don't consume a run number, so `0.1.{run_number}` versioning is unaffected).
- No Core code, no unit tests — verification is the live check in Task 3.

---

### Task 1: README — status block + prose refresh

**Files:**
- Modify: `README.md`

**Interfaces:**
- Produces: the `<!-- status:begin -->` / `<!-- status:end -->` markers Task 2's perl replace targets — exact spelling matters.

- [ ] **Step 1: Replace the stale Status section and refresh stale prose.** Replace the current `## Status` section (lines 22–24: "Pre-implementation…") with:

```markdown
## Status

<!-- status:begin -->
[![build](https://github.com/amdrake93/eq2auras/actions/workflows/build.yml/badge.svg?branch=main)](https://github.com/amdrake93/eq2auras/actions/workflows/build.yml) ![version](https://img.shields.io/badge/version-0.1.58-56B4E9) ![released](https://img.shields.io/badge/released-2026--07--02-E69F00)

`dev-latest` · [release page](https://github.com/amdrake93/eq2auras/releases/tag/dev-latest) · [CI runs](https://github.com/amdrake93/eq2auras/actions) · [SPEC](docs/SPEC.md) · [backlog](docs/backlog.md)
<!-- status:end -->

Everything between the markers is stamped by CI on each release ([SPEC §Development & test cycle](docs/SPEC.md)).
```

(The initial hand-stamped values — `0.1.58`, `2026--07--02` — are the current dev-latest release; the first post-merge build overwrites them. Note shields.io escapes literal dashes as `--`.)

Also refresh the stale human-owned prose in the same file:
- Line 12: `**Timer Overlay** — *Phase 1, in design.*` → `**Timer Overlay** — *live (shipped through slice 4: escalation, knobs, palette colors, dual panels, movable windows).*`

- [ ] **Step 2: Verify markers are exact.** Run: `grep -c 'status:begin\|status:end' README.md` — Expected: `2`.

- [ ] **Step 3: Commit**

```bash
git add README.md
git commit -m "README: CI-stamped status block; refresh stale prose"
```

---

### Task 2: build.yml — stamp step on the release path

**Files:**
- Modify: `.github/workflows/build.yml` (after the "Publish rolling prerelease" step)

**Interfaces:**
- Consumes: Task 1's markers; the existing `steps.ver.outputs.version` output; the workflow's existing `permissions: contents: write` and `actions/checkout@v4` (persists credentials by default — no new secrets).

- [ ] **Step 1: Append the stamp step** at the end of the job, after "Publish rolling prerelease":

```yaml
      - name: Stamp README status block
        if: github.ref == 'refs/heads/main'
        shell: bash
        env:
          VERSION: ${{ steps.ver.outputs.version }}
        run: |
          today="$(date -u +%Y-%m-%d)"
          export BLOCK="<!-- status:begin -->
          [![build](https://github.com/amdrake93/eq2auras/actions/workflows/build.yml/badge.svg?branch=main)](https://github.com/amdrake93/eq2auras/actions/workflows/build.yml) ![version](https://img.shields.io/badge/version-${VERSION}-56B4E9) ![released](https://img.shields.io/badge/released-${today//-/--}-E69F00)

          \`dev-latest\` · [release page](https://github.com/amdrake93/eq2auras/releases/tag/dev-latest) · [CI runs](https://github.com/amdrake93/eq2auras/actions) · [SPEC](docs/SPEC.md) · [backlog](docs/backlog.md)
          <!-- status:end -->"
          perl -0777 -pi -e 's/<!-- status:begin -->.*?<!-- status:end -->/$ENV{BLOCK}/s' README.md
          git config user.name "github-actions[bot]"
          git config user.email "41898282+github-actions[bot]@users.noreply.github.com"
          git add README.md
          if git commit -m "README: stamp status v${VERSION} [skip ci]"; then
            git pull --rebase origin main || true
            git push origin HEAD:main \
              || echo "::warning::status stamp push failed — block stays stale until next release"
          fi
```

Notes for the executor:
- The heredoc-style `BLOCK` is two YAML-indented lines inside the string — perl inserts them verbatim; leading spaces inside the block are stripped by neither bash nor perl, so **left-align the block's continuation lines to the string's opening column** exactly as shown (YAML `run: |` strips only the common indent of the scalar).
- `git commit` is wrapped in `if` — an unchanged README (theoretically impossible since VERSION always changes, but cheap) skips the push cleanly.
- Push failure = `::warning::`, never a step failure (reviewer requirement 1: the release already succeeded).

- [ ] **Step 2: Sanity-check the YAML** — Run: `python3 -c "import yaml,sys; yaml.safe_load(open('.github/workflows/build.yml'))" && echo OK` — Expected: `OK`.

- [ ] **Step 3: Local dry-run of the replace** (proves the perl/marker mechanics without CI):

```bash
export BLOCK="<!-- status:begin -->
DRYRUN
<!-- status:end -->"
cp README.md /tmp/readme-test.md
perl -0777 -pi -e 's/<!-- status:begin -->.*?<!-- status:end -->/$ENV{BLOCK}/s' /tmp/readme-test.md
grep -A1 'status:begin' /tmp/readme-test.md
```
Expected: `DRYRUN` between the markers; rest of the file untouched.

- [ ] **Step 4: Commit**

```bash
git add .github/workflows/build.yml
git commit -m "CI: stamp README status block on each release (failure-tolerant push, skip-ci)"
```

---

### Task 3: Ship + live verification

- [ ] **Step 1: Push the branch; confirm verify-only CI is green** (stamp step must show *skipped*):

```bash
git push -u origin readme-status-section
gh run watch $(gh run list --branch readme-status-section --limit 1 --json databaseId --jq '.[0].databaseId') --exit-status
```

- [ ] **Step 2: Alex reviews** `git diff main..readme-status-section`; **merge on his approval** (merge = release = first live stamp):

```bash
git checkout main && git pull --rebase && git merge readme-status-section && git push
```

- [ ] **Step 3: Live check** (after the main run finishes):
1. `gh run watch` the main run — all steps green, **"Stamp README status block" ran** (not skipped).
2. `git pull --rebase` — expect the bot commit `README: stamp status v0.1.NN [skip ci]`; README shows the new version + today's date; **no second CI run was triggered** (`gh run list --limit 3`).
3. **View the README on github.com** (the empirical check the spec review flagged): the **Actions build badge renders** — not a broken image. If broken: fallback per review — add a static `build-passing` pill to the stamped block and drop the native badge (same rewrite step, no design change).
4. Delete the branch: `git branch -d readme-status-section && git push origin --delete readme-status-section`.

- [ ] **Step 4: Backlog** — note the feature shipped (one line under slice-4's shipped entry; doc-only, straight to main is allowed).
