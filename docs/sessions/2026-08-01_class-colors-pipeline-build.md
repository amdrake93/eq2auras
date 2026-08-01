# Session chronicle — class colors: brainstorm → dev-latest, one session

*2026-07-31 → 08-01. The payoff session for the class-colors arc: turning two locked spike inputs (the 12-subclass palette, the census-validated name→subclass catalog) into a working, shipped feature. One continuous run — brainstorm → spec → plan → implement → three review loops → merge → `dev-latest 1.2.35` — most of it autonomous while Alex was at lunch. The stories worth keeping are the reversals the reviews forced: a transcription that ignored its own source's corrections, a spec whose persona mechanism was **logically impossible**, and a betrayal bug the code carried from the plan all the way to the last review. Also the process experiment: the third-party reviewer run on Fable 5.*

## Part I — the design fell out of one visual companion session

The architecture spine came fast because grounding it visually made the choices obvious. Three forks, all settled at the browser:

- **Threading.** The worry was "inference on every action clogging the display." The reframe that dissolved it: the spike's catalog pruning already did the classification, so inference is a **dictionary lookup**, not a classifier — ~20µs of hash lookups, not computation. Alex pushed for a third option beyond my two (single-thread-throttled vs. dedicated-thread): **one poll thread owns the ACT lock, folds the ability read into the read it already does.** He called my throttle "over-engineering a 20µs action" — and he was right; his C was *simpler* than my A, not fancier. The pillar he named — never soft-lock the ACT lock with competing threads — is what the whole read discipline serves.
- **Poll timing.** His verifying question ("what if processing exceeds the 100ms window?") had a clean answer once we read the code: it's a `System.Windows.Forms.Timer` on ACT's UI thread — **non-reentrant by construction** (WM_TIMER coalesces; a poll can't start before the prior returns). Overrun drops a tick, never races. The reentrancy problem couldn't happen.
- **The recap two-tone bar.** Alex's idea, and a mockup nailed the polarity in one shot: the victim's class color is the row *background*, a dark bar for *current* HP shrinks left as they die, so at the killing blow the row is entirely their color — they "bleed out into their identity."

**The realization that mattered most wasn't in the browser.** Designing the persistence, I first modeled "committed = colored = skip-the-read." But that makes personas undetectable: a warm-started player marked committed is never re-read, so a class swap can never be seen. The fix — **split *color* (warm-started, shown immediately) from *confirmed-this-encounter* (the read-skip), and reset confirmation each fight** — is the load-bearing idea of the whole feature. Personas happen between fights (log out/in), never mid-combat, so a per-encounter re-read catches them while staying bounded within a fight. This surfaced during planning, exactly where design should surface.

## Part II — the reviews as a forcing function (and the Fable 5 experiment)

Alex tried something new: run the third-party reviewer on **the most capable model available (Fable 5)** — not in the workflow docs, an experiment "depending on how this goes." It went well enough to keep. Each artifact got a fresh, context-isolated Fable 5 reviewer, blocks written verbatim to an out-of-tree audit dir, processed with `receiving-code-review`.

**The plan review earned the whole experiment.** Two findings that a lighter pass might have missed:

1. **The transcription ignored the census corrections in its own source of truth.** `signatures.md` ends with a census cross-reference that MOVES 15 names to subclass-SHARED (Backstab/Gouge → Rogue, not Brigand) and CUTs others (Ambush, Blaze) — and my plan's transcription rule, sample arrays, and a test all used the *pre-census* classification. The census section is the final authority; the tier lines above it are superseded. Also caught: the thin-class firm-ups (Ranger/Warlock/Guardian/Berserker) were claimed but never operationalized — they live truncated in `signatures.md`, full lists in `census_index.tsv`. The fix taught the real lesson: recover them **union-by-base-name** (a name qualifies as single-class only if its union across all rank-variants is one class), which correctly excludes names like "Bash" that are single-class at one spell-rank but Warrior-SHARED by union.

2. **The spec's persona mechanism was logically unsatisfiable.** The spec said committed combatants are "never re-scanned" *and* that personas self-correct via re-read — which can't both be true (a never-re-read combatant can't be re-read). The plan had already solved it (the per-encounter confirmation reset from Part I), but the spec still described the impossible version. The reviewer flagged that spec and plan must agree — so the *plan review corrected the spec*. A good demonstration that the artifacts are one system: a plan can expose a spec bug.

Both spec and plan reached closure in two rounds each.

## Part III — the build: strict TDD, and honoring the hard constraints

Fourteen tasks, inline strict-TDD, Core-first. The engine constraints from CLAUDE.md all showed up as real forces:

- **Single shipped DLL** → the catalog ships as compiled C# (no sidecar file — Alex: "drop a DLL in a folder is the entire install contract").
- **DCJS only, enum-0 defaults** → `Subclass.Unknown = 0`, the cache round-trips.
- **The call-time-statics trap** → the code review's sibling concern, but the plan review's Finding 3 already caught it: ACT's bucket alias-statics are reassigned by the EQ2 parser at *its* init, so a type-init static array would freeze stale values and silently read nothing. Read them at call time, as the existing `BucketName` does.

The catalog transcription was the biggest single task — all 24 finals + 12 SHARED lists, census-corrected, pets/parentheticals stripped, thin-class firm-ups recovered from the tsv. The collision-guard test (no name resolves to two subclasses) passed on the real transcribed whole, and the code review later verified it **name-by-name against the source** and found the firm-ups *more* rigorous than the source doc's own truncated previews.

Core landed at **370 green**; the WPF plugin (untestable on the Mac) was verified by branch CI compile.

## Part IV — the bug that survived to the last review

The code review (Fable 5, type-5, on Alex's request) was mostly a long "what checks out" — but it found one real defect the plan *and* the implementation both carried: **a betrayed player's stored `final` never corrected.** In `Commit`, the same-subclass branch only upgraded the final *from* `Unknown`; a within-subclass betrayal (Swashbuckler → Brigand — same Rogue color, different final) hit that branch, found a non-Unknown final, and returned without updating. Stale forever. The spec explicitly promises the override corrects that drift (it's the enrichment a future 24-level feature reads). Color was unaffected in v1, but the persisted record would be durably wrong.

Notable that it survived three reviews: the plan sketch had the logic, the plan review didn't hand-trace that path, and only the code review — reading `Commit` fresh against the spec's pillar — caught it. The fix was one predicate (`final != Unknown && final != existing.Final`), plus two tests pinning both directions (betrayal corrects; a SHARED re-read never downgrades a known final). Round 2 closed.

## Where it stands

**Merged to `main` → `dev-latest 1.2.35`.** All three artifacts — spec, plan, code — Fable-5-reviewed to closure. Rows now color by inferred EQ2 subclass across every combatant-scoped surface (main rows, deaths, drill, by-counterpart hover, the two-tone recap), warm-started from a learned cache, with the per-encounter re-read catching persona swaps. Headers and the popup's family axis stay untouched; the recap-second hover keeps its red/green.

Alex's minimal self-test looked good; the full on-box field script (SPEC §Testing strategy — class colors) and the stable promote are his, later. Field-tune candidates flagged but non-blocking: the dark-HP shade, the min-fill floor, the outline blur, and the exact log-name form of the thin-class firm-ups. Pet-proc → owner attribution is the one deferred piece (a pet is a separate combatant; classes still resolve via their own outgoing casts).

The arc that began "i think we're going to try class colors" three sessions ago — palette, then the signature dig, then this pipeline — is complete.
