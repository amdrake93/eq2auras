# Versioned Releases + Programmatic Revert — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the release pipeline durable per-version releases and a one-command programmatic revert, by rewriting `promote.yml` — no plugin/self-updater changes.

**Architecture:** Each promotion mints an immutable `vX.Y.Z` release (the durable byte store + version history + GitHub "Latest"); the rolling `stable` release becomes a prerelease-flagged pointer the self-updater keeps polling by tag name. One `workflow_dispatch` does two operations by its optional `version` input: **promote** (blank → bless current `dev-latest`, mint `vX.Y.Z`, re-point `stable`) or **revert** (a version → re-point `stable` at an existing `vX.Y.Z` and demote newer versioned releases so "Latest" stays honest). A `dry_run` input logs every action without mutating, since the only live test of a release workflow is running it.

**Tech Stack:** GitHub Actions (`workflow_dispatch`), `gh` CLI (release create/edit/upload/download, `gh api`), `sort -V` for semver comparison. Verification: `actionlint` (+ its embedded `shellcheck`) statically; `gh workflow run … -f dry_run=true` live once on `main`.

## Global Constraints

- **CI-only — no plugin changes.** The self-updater reads a fixed tag (`/releases/tags/{stable|dev-latest}`, `src/eq2auras.Plugin/SelfUpdate/SelfUpdater.cs:103`) and ignores the prerelease flag; nothing in `src/` changes. (SPEC §`stable` is a rolling pointer.)
- **`stable` and `dev-latest` are prerelease-flagged pointers; `vX.Y.Z` are the only non-prerelease releases** — the channel is chosen by **tag name**, never the flag. (SPEC §`stable` is a rolling pointer.)
- **`vX.Y.Z` release identity:** tag = semver `vX.Y.Z` (e.g. `v1.4.0`) on the **source commit**; name = **bare `X.Y.Z` core** (`1.4.0`); asset = the exact DLL; body records the source commit SHA. (SPEC §A promotion mints a permanent versioned release.)
- **Promotion pins the specific version it read** — blank resolves `dev-latest`'s current version once, so a `main` push mid-promotion can't sneak in. (SPEC §One manual `workflow_dispatch`.)
- **Revert targets must be previously promoted** (their `vX.Y.Z` exists) — else the dispatch errors. Bytes come from that `vX.Y.Z` release; no rebuild. (SPEC §One manual `workflow_dispatch`, §Rebuild-from-source is the break-glass floor.)
- **`build.yml` is unchanged** — it only publishes the `dev-latest` rolling prerelease, which the new scheme keeps.

---

## File Structure

- **Modify:** `.github/workflows/promote.yml` — full rewrite to the two-operation versioned scheme. This is the entire feature.
- **Unchanged:** `.github/workflows/build.yml` (dev-latest publisher), `src/**` (self-updater), `README.md` badges (the stable badge already reads `display_name=release` = latest non-prerelease, which resolves to `vX.Y.Z` once the scheme is live — forward-compatible).
- **Rollout (owner merge gate, not a branch edit):** a one-time seed of `v1.3.0` + flipping `stable` to prerelease (Task 2), then live verification (Task 3).

**Note on the branch-vs-`main` dispatch constraint:** `workflow_dispatch` runs the workflow file *as it exists on the chosen ref*, but the run's **inputs are validated against the default branch's copy** of the workflow. `main`'s current `promote.yml` has no `dry_run` input, so `gh workflow run promote.yml --ref versioned-releases-revert -f dry_run=true` may reject the unknown input **until the rewrite is on `main`.** Therefore live `dry_run` verification (Task 3) is a **post-merge** step at the owner's gate; on-branch verification (Task 1) is static (`actionlint` + `shellcheck` + hand-trace).

---

## Task 1: Rewrite `promote.yml` to the two-operation versioned scheme

**Files:**
- Modify (full rewrite): `.github/workflows/promote.yml`

**Interfaces:**
- Consumes: the `dev-latest` release (name = current dev version, asset `eq2auras.dll`) published by `build.yml`; `gh api .../git/ref/tags/<tag>` for source SHAs.
- Produces: on **promote** — a new `v<version>` release (non-prerelease, tag on source commit, asset `eq2auras.dll`) + an updated `stable` release (prerelease, name `<version>`, asset `eq2auras.dll`). On **revert** — an updated `stable` release pointing at an existing `v<version>`'s bytes, with all `vX.Y.Z` newer than `<version>` demoted to prerelease and `v<version>` marked latest.

- [ ] **Step 1: Verify the current `promote.yml` baseline (so the rewrite replaces exactly what's there)**

Run: `sed -n '1,60p' .github/workflows/promote.yml`
Expected: the current single-operation workflow — `workflow_dispatch` with one `version` input, a "Resolve the dev-latest build" step, a `gh release download dev-latest` step, and a `softprops/action-gh-release@v2` "Publish stable" step with `tag_name: stable`, `prerelease: false`.

- [ ] **Step 2: Replace the whole file with the two-operation workflow**

Write `.github/workflows/promote.yml` with exactly this content:

```yaml
name: promote
on:
  workflow_dispatch:
    inputs:
      version:
        description: "Blank = promote current dev-latest. A version (e.g. 1.3.0) = revert stable to that already-promoted version."
        required: false
        default: ""
      dry_run:
        description: "Log the actions without creating/moving/editing any release."
        type: boolean
        required: false
        default: false

permissions:
  contents: write

jobs:
  promote:
    runs-on: ubuntu-latest
    env:
      GH_TOKEN: ${{ github.token }}
      REPO: ${{ github.repository }}
      DRY: ${{ inputs.dry_run }}
    steps:
      - name: Resolve operation, version, byte source, and source commit
        id: resolve
        run: |
          set -euo pipefail
          want="${{ inputs.version }}"
          if [ -z "$want" ]; then
            op="promote"
            version="$(gh release view dev-latest --repo "$REPO" --json name -q .name)"
            sha="$(gh api "repos/$REPO/git/ref/tags/dev-latest" -q .object.sha)"
            src_tag="dev-latest"
          else
            op="revert"
            version="$want"
            vtag="v$version"
            if ! gh release view "$vtag" --repo "$REPO" >/dev/null 2>&1; then
              echo "::error::Version $version was never promoted (no $vtag release). Revert targets must be previously-promoted versions."
              exit 1
            fi
            sha="$(gh api "repos/$REPO/git/ref/tags/$vtag" -q .object.sha)"
            src_tag="$vtag"
          fi
          {
            echo "op=$op"
            echo "version=$version"
            echo "sha=$sha"
            echo "src_tag=$src_tag"
            echo "vtag=v$version"
          } >> "$GITHUB_OUTPUT"
          echo "-> $op $version (bytes from $src_tag, source commit $sha)"

      - name: Download the release DLL (exact bytes -- no recompile)
        run: |
          set -euo pipefail
          mkdir -p dist
          gh release download "${{ steps.resolve.outputs.src_tag }}" --repo "$REPO" --pattern eq2auras.dll --dir dist --clobber

      - name: Mint the immutable versioned release (promote only)
        if: steps.resolve.outputs.op == 'promote'
        run: |
          set -euo pipefail
          vtag="${{ steps.resolve.outputs.vtag }}"
          version="${{ steps.resolve.outputs.version }}"
          sha="${{ steps.resolve.outputs.sha }}"
          if gh release view "$vtag" --repo "$REPO" >/dev/null 2>&1; then
            if [ "$DRY" = "true" ]; then
              echo "[dry-run] would ensure existing $vtag is non-prerelease + latest (idempotent re-promote of a possibly-demoted version)"
            else
              echo "$vtag already exists -- ensuring non-prerelease + latest (idempotent re-promote; clears any prior revert demotion)."
              gh release edit "$vtag" --repo "$REPO" --prerelease=false --latest
            fi
          elif [ "$DRY" = "true" ]; then
            echo "[dry-run] would create release $vtag on $sha (name=$version, non-prerelease, --latest) with dist/eq2auras.dll"
          else
            gh release create "$vtag" --repo "$REPO" --target "$sha" --title "$version" --latest \
              --notes "eq2auras $version. Source commit $sha." dist/eq2auras.dll
          fi

      - name: Demote versioned releases newer than the target, keep target latest (revert only)
        if: steps.resolve.outputs.op == 'revert'
        run: |
          set -euo pipefail
          target="${{ steps.resolve.outputs.version }}"
          gh release list --repo "$REPO" --limit 200 --json tagName,isPrerelease \
            -q '.[] | select(.isPrerelease==false) | .tagName' | while read -r tag; do
            case "$tag" in
              v[0-9]*) core="${tag#v}" ;;
              *) continue ;;
            esac
            [ "$core" = "$target" ] && continue
            higher="$(printf '%s\n%s\n' "$target" "$core" | sort -V | tail -1)"
            if [ "$higher" = "$core" ]; then
              if [ "$DRY" = "true" ]; then
                echo "[dry-run] would demote $tag to prerelease (newer than reverted-to $target)"
              else
                echo "demoting $tag to prerelease (newer than reverted-to $target)"
                gh release edit "$tag" --repo "$REPO" --prerelease
              fi
            fi
          done
          if [ "$DRY" = "true" ]; then
            echo "[dry-run] would mark v$target as latest"
          else
            gh release edit "v$target" --repo "$REPO" --latest --prerelease=false
          fi

      - name: Re-point stable at the chosen build (prerelease pointer)
        run: |
          set -euo pipefail
          version="${{ steps.resolve.outputs.version }}"
          sha="${{ steps.resolve.outputs.sha }}"
          op="${{ steps.resolve.outputs.op }}"
          body="stable = $version ($op). Bytes from ${{ steps.resolve.outputs.src_tag }}; source commit $sha."
          if [ "$DRY" = "true" ]; then
            echo "[dry-run] would set stable -> name=$version, prerelease=true, asset=dist/eq2auras.dll"
            exit 0
          fi
          if gh release view stable --repo "$REPO" >/dev/null 2>&1; then
            gh release edit stable --repo "$REPO" --prerelease --title "$version" --notes "$body"
            gh release upload stable --repo "$REPO" dist/eq2auras.dll --clobber
          else
            gh release create stable --repo "$REPO" --prerelease --title "$version" --notes "$body" dist/eq2auras.dll
          fi
```

- [ ] **Step 3: Install `actionlint` if absent (static validator for Actions YAML + embedded shell via shellcheck)**

Run: `command -v actionlint || brew install actionlint`
Expected: a path to `actionlint`, or a successful brew install. (If Homebrew is unavailable, fall back to Step 4b.)

- [ ] **Step 4: Run `actionlint` on the workflow**

Run: `actionlint .github/workflows/promote.yml`
Expected: **no output** (exit 0). actionlint validates the `workflow_dispatch`/`inputs` schema, the `${{ }}` expressions, and — via shellcheck — the `run:` shell (quoting, `set -euo pipefail`, the `while read` loop, `sort -V` pipeline).

- [ ] **Step 4b: (only if actionlint could not be installed) YAML-parse + manual shell review**

Run: `python3 -c "import yaml,sys; yaml.safe_load(open('.github/workflows/promote.yml')); print('yaml ok')"`
Expected: `yaml ok`. Then hand-read each `run:` block against Step 5's trace.

- [ ] **Step 5: Hand-trace both operations against the spec (no live run — see the dispatch-constraint note)**

Confirm by reading the file:
- **Promote (blank):** resolves `version`/`sha` from `dev-latest`; downloads `dev-latest`'s DLL; mints `v<version>` on `sha` as non-prerelease `--latest` — and if `v<version>` already exists (including left **prerelease** by a prior revert's demotion), re-asserts it non-prerelease + latest so "Latest" tracks the version `stable` now serves; re-points `stable` as **prerelease** with that name+bytes. Matches SPEC §One manual `workflow_dispatch` (Promote) + §A promotion mints + §Keeping "Latest" honest (a re-promote of a demoted version reclaims Latest).
- **Revert (`1.3.0`):** errors if `v1.3.0` absent; else downloads `v1.3.0`'s DLL; demotes every non-prerelease `vX.Y.Z` with `core > 1.3.0` to prerelease and marks `v1.3.0` latest; re-points `stable` as prerelease with `1.3.0`'s name+bytes. Matches SPEC §One manual `workflow_dispatch` (Revert) + §Keeping "Latest" honest across a revert.
- **`dry_run=true`:** every create/edit/upload is replaced by an `echo "[dry-run] would …"`; only read-only `gh release view/list`/`gh api`/`gh release download` run. Confirm no mutating `gh` command is reachable when `DRY=true`.

- [ ] **Step 6: Commit**

```bash
git add .github/workflows/promote.yml
git commit -m "Release: promote.yml -> versioned vX.Y.Z + programmatic revert (mint/re-point/demote, dry_run); stable becomes prerelease pointer [skip ci]"
```

`[skip ci]` is intentional: this changes only `promote.yml` — a `workflow_dispatch` workflow not triggered by push — so the push-triggered `build.yml` verify run would exercise nothing about this change. Its verification is `actionlint` (Steps 3–4), not branch CI.

---

## Task 2: (Rollout, owner merge gate) Seed `v1.3.0` and flip `stable` to prerelease

Runs **once, after the branch merges to `main`**, to establish the scheme's invariant (a durable first revert target + the pointer-flag end-state). It mutates live releases, so it is an owner-gate step, not a branch edit. `1.3.0`'s source commit is `c1e7f3e` (the `version.txt` `1.2 -> 1.3` bump; `PATCH=0` ⇒ that exact commit).

**Files:** none (one-time `gh` commands run against the live repo).

- [ ] **Step 1: Dry-inspect current state**

Run: `gh release view stable --repo amdrake93/eq2auras --json tagName,name,isPrerelease && gh release view v1.3.0 --repo amdrake93/eq2auras 2>&1 | head -1`
Expected: `stable` → name `1.3.0`, `isPrerelease: false`; `v1.3.0` → `release not found` (nothing to seed yet).

- [ ] **Step 2: Download the current stable (`1.3.0`) bytes**

```bash
mkdir -p seed && gh release download stable --repo amdrake93/eq2auras --pattern eq2auras.dll --dir seed --clobber
```
Expected: `seed/eq2auras.dll` present.

- [ ] **Step 3: Mint `v1.3.0` from those bytes on the source commit**

```bash
gh release create v1.3.0 --repo amdrake93/eq2auras --target c1e7f3e --title "1.3.0" --latest \
  --notes "eq2auras 1.3.0. Source commit c1e7f3e. Backfilled at versioned-release rollout." seed/eq2auras.dll
```
Expected: creates the `v1.3.0` tag at `c1e7f3e` and a non-prerelease release named `1.3.0`.

- [ ] **Step 4: Flip `stable` to a prerelease pointer**

```bash
gh release edit stable --repo amdrake93/eq2auras --prerelease
```
Expected: `stable` now `isPrerelease: true`.

- [ ] **Step 5: Verify the invariant holds**

Run: `gh release view --repo amdrake93/eq2auras --json tagName,name && gh api repos/amdrake93/eq2auras/releases/latest -q '.tag_name+" "+.name'`
Expected: the repo's **Latest** resolves to `v1.3.0` / `1.3.0` (the only non-prerelease); `stable` and `dev-latest` are prereleases. The self-updater still reads `/releases/tags/stable` → name `1.3.0`, unchanged for riders.

---

## Task 3: (Rollout, owner merge gate) Live verification — dry-run, then a real cycle

Runs after Task 2, on `main` (so the `dry_run` input validates — see the dispatch-constraint note).

**Files:** none (live `workflow_dispatch` runs).

- [ ] **Step 1: Dry-run a promote**

```bash
gh workflow run promote.yml --repo amdrake93/eq2auras -f dry_run=true
```
Then watch: `gh run watch "$(gh run list --repo amdrake93/eq2auras --workflow promote.yml -L1 --json databaseId -q '.[0].databaseId')" --exit-status`
Expected: logs `-> promote <dev-version> …`, `[dry-run] would create release v<dev-version> …`, `[dry-run] would set stable -> …`; **no** release is created/edited (re-run `gh release list` to confirm unchanged).

- [ ] **Step 2: Dry-run a revert to `1.3.0`**

```bash
gh workflow run promote.yml --repo amdrake93/eq2auras -f version=1.3.0 -f dry_run=true
```
Watch the run. Expected: logs `-> revert 1.3.0 …`, `[dry-run] would demote v<newer> …` for any newer versioned release, `[dry-run] would set stable -> name=1.3.0 …`; nothing mutated.

- [ ] **Step 3: (Owner's call) A real promote of the current dev build**

```bash
gh workflow run promote.yml --repo amdrake93/eq2auras
```
Watch the run. Expected: a new `v<dev-version>` non-prerelease release appears as Latest; `stable` name = `<dev-version>`, prerelease. A rider's "check for updates" then installs `<dev-version>` by identity.

- [ ] **Step 4: (Owner's call) Prove revert works — roll `stable` back to `1.3.0`**

```bash
gh workflow run promote.yml --repo amdrake93/eq2auras -f version=1.3.0
```
Watch the run. Expected: `stable` name = `1.3.0` again; the just-promoted `v<dev-version>` is demoted to prerelease; Latest = `v1.3.0`. A rider's next check sees `1.3.0 != <dev-version>` and rolls back automatically (SPEC §Updates target by channel identity).

---

## Self-Review

**Spec coverage:**
- §A promotion mints a permanent versioned release → Task 1 Step 2 "Mint" step (tag `vX.Y.Z` on source commit, bare-core name, `--latest`, asset, SHA in body). ✓
- §`stable` is a rolling pointer, prerelease-flagged → Task 1 "Re-point stable" step (`--prerelease`) + Task 2 Step 4 (flip existing stable). ✓
- §One manual `workflow_dispatch`, two operations → Task 1 "Resolve" step (blank→promote, version→revert) + the revert-target existence error. ✓
- §Keeping "Latest" honest across a revert → Task 1 "Demote … keep target latest" step (`sort -V`, demote `core > target`, `--latest` on target). ✓
- §Rollout seed (`v1.3.0`) → Task 2. ✓
- §Rebuild-from-source is the break-glass floor → not implemented (by design — the break-glass path is manual, documented in the spec; no workflow needed). Noted, not a gap.
- §badges → no change needed (README stable badge `display_name=release` resolves to `vX.Y.Z` once live; forward-compatible). Noted.
- Identity updater unchanged → Global Constraints; no `src/` task. ✓

**Placeholder scan:** no TBD/TODO; every `run:` block and `gh` command is complete. Verification steps state exact commands + expected output.

**Type/name consistency:** `steps.resolve.outputs.{op,version,sha,src_tag,vtag}` are defined in Task 1 Step 2's "Resolve" step and consumed by later steps with those exact names; `v$version` / `vtag` usage is consistent; `1.3.0` / `c1e7f3e` match the spec's rollout seed and the verified `version.txt` bump commit.

**Verification-model note (not classic TDD):** CI-workflow YAML has no unit-test harness, so each task's "test cycle" is static validation (`actionlint`+`shellcheck`, hand-trace) on the branch and a **`dry_run` live run** on `main` — the honest analog of red/green here, since the only real exercise of a release workflow is dispatching it. The `dry_run` input exists specifically to make that first live exercise non-destructive.
