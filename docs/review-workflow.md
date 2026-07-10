# Third-party review workflow

How development artifacts in this repo get written, reviewed, and merged. Two Claude sessions
with separated roles — a **writer** (specs, plans, code, research docs) and a **reviewer** —
plus the **human owner**, who ferries feedback between them and owns every gate. This doc is
a process contract: hand it to a fresh session and it can play either role (§Bootstrap lists
what each needs).

## The shape

- **Writer and reviewer never see each other's conversation.** The separation is the point:
  the reviewer re-derives from the working tree, binaries, and vendored sources — never from
  the writer's narrative. Feedback travels as one self-contained block relayed verbatim —
  by the human between two sessions, or mechanically by the writer session itself
  (§Automated orchestration; both are instantiations of the same contract).
- **The human owns every gate**: phase transitions (brainstorm → spec → plan → implement),
  accept/reject of each finding, and the merge decision. The reviewer never pronounces on
  merge-readiness; the writer presents work as ready-for-review, never ready-to-merge.
- Work happens on descriptively-named branches; the reviewer reviews
  `git diff main..<branch>`. Merge to `main` only on the owner's explicit call
  (see CLAUDE.md §Working style for branch/release mechanics).

## Review types and their triggers

1. **Spec review** ("spec review \<branch\>") — verify every code-level claim the spec makes
   against the working tree: signatures, line numbers, constants, existing behavior, naming.
   A spec is present-tense ("how the system works NOW") — claims about records/features that
   don't exist in code are findings even when inherited from older text, if the section under
   edit interacts with them.
2. **Plan review** ("review the plan") — verify: cited line ranges and insertion points are
   exact; interfaces match between producing and consuming tasks; test code matches the
   suite's actual conventions (framework, file style); new files are picked up by the build
   (e.g. csproj include globs) or the plan adds the packaging step; every test expectation
   traced by hand through the plan's implementation; every plan-watch item from the spec
   review demonstrably landed.
3. **Fix-flow review** (spec + code on one branch, no plan doc — for field-driven reversals
   and small reworks) — same verification depth on both halves. The review block must carry a
   **merge-gate live script** (concrete "do X, expect Y" steps) since there is no plan doc to
   hold one.
4. **Re-review / closure** ("rereview", "verify edits") — strictly two parts: (a) verify each
   prior finding's edit against the fix diff, AND (b) a fresh holistic pass over the whole
   artifact as it now stands. Edits-only verification is not a re-review. Closures use the
   header variant "review closure — no action required" and tick each finding ✓ with
   evidence.
5. **Code review of implemented plans (on request only)** — implementation verification
   belongs to the owner's merge gate: diff review, verify-only branch CI, and the live
   script. A reviewer code round runs only when the owner requests one for a risky branch.
   (Fix-flow branches carry their code review inherently — type 3.) Backlog/plan phrases like "the
   plan/code review verifies each" resolve to this split: the plan review verifies
   plan-watch items landed in the *plan*; their landing in *code* is checked at the owner's
   gate (or the requested code round).
6. **Research / ground-truth doc review** — verified by **re-derivation**, not reading: the
   reviewer independently reproduces the artifact (decompile the vendored binary, clone the
   referenced upstream, read the vendored source) and samples claims and `file:line`
   citations against it. Coverage is stated honestly — which claims were sampled, which were
   not. Verbatim agent research reports on *external* artifacts (decompiles, upstream clones)
   are archived in `docs/research/`; errors found there are flagged in a review-status header
   at the top, never silently patched — the reference doc is the maintained distillation, the
   archive is provenance. Research read from source vendored in-repo with inline citations
   may skip the archive, with the skip recorded in the reference doc.

## The verification standard (reviewer)

- **Verify, don't trust.** Every code-level claim is checked against the tree/binary before
  being asserted, and cited as `file:line`. A claim that verifies goes in "What checks out";
  only unverifiable or false claims become findings.
- Hand-trace test expectations through implementations; run exhaustive greps for
  "only"/"never"/"zero callers" claims rather than sampling them.
- When the human disputes a finding's mechanics, verify their claim against the code before
  conceding — then update the still-pending feedback block.

## The feedback block (the reviewer's deliverable)

One self-contained markdown block **addressed to the writing agent in second person**, pasted
verbatim by the human. Self-contained means: SPEC section refs, `file:line` refs, and all
context inline — the writer has none of the review conversation. Structure, in order:

1. Opening receiver directive, verbatim:
   > **For the receiving agent:** REQUIRED SUB-SKILL: process this feedback with
   > `superpowers:receiving-code-review` — verify each claim against the spec/code before
   > acting on it; push back on anything that doesn't hold.
2. Header: `# Review feedback: <thing> (<commit>, branch)`.
3. **Bold verdict sentence up top** — exactly one of **approved** or **request changes**,
   with a one-phrase scope of what must change.
4. `## What checks out` — verified strengths, with citations. Load-bearing: it tells the
   writer which claims survived independent verification.
5. `## Findings` — numbered, severity-tagged: **Important** = behavior or ambiguity that must
   close before the artifact is right; **Minor** = one-to-two-line fixes. Verdict is
   "request changes" iff any Important finding exists (Minor-only → approved, findings listed
   for follow-up).
6. Optional take-or-leave nits — explicitly the writer's call; a re-review does not count
   them unaddressed.

Separately from the block, the reviewer gives the human a chat summary — lead with the
verdict. The block is for the writer; the chat is for the human.

## Decision routing

- **Questions for the owner** — design decisions that are the owner's to make are flagged as
  "Question for \<owner\>" findings, never as change requests to the writer. A re-review does
  not count them unaddressed if the writer correctly left them alone. Whichever way they
  resolve, the decision must be **recorded** (spec phrase, backlog line) — the reviewer can't
  see conversations, so unrecorded "no change" decisions get re-raised.
- **Plan-watch items** — spec-review findings that belong to the *plan* (not the spec) are
  listed in the spec review/closure, recorded by the writer in the backlog's in-flight entry,
  and verified landed by the plan review. Fix-flow branches get the merge-gate live script
  instead, written into the review block itself.

## The writer's side of the contract

- **Process every feedback block with `superpowers:receiving-code-review`**: verify each
  claim against the working tree before acting on it; push back with evidence when a finding
  doesn't hold; no performative agreement. Corrections found valid get fixed one at a time
  and committed with the finding named.
- **Record everything the reviewer will need next round**: plan-watch items into the
  backlog's in-flight entry; "no change" resolutions into a doc (spec phrase, backlog line,
  or archive header) — a decision that lives only in conversation will be re-raised.
- **Preserve provenance**: research distillations cite `file:line`; verbatim reports on
  external artifacts go to `docs/research/` with review-status headers (vendored-source
  research may skip the archive, recording the skip); errors discovered later are flagged in
  those headers, never silently patched.
- **Present, don't merge**: every branch — including fix-flow — stops at ready-for-review.
  The reviewer's approval and the owner's merge call are separate gates, in that order.

## Bootstrap

- **New reviewer**: start a fresh session in a clone of this repo and give it this doc plus
  the target ("spec review \<branch\>", "review the plan", …). It needs: the repo (including
  `ThirdParty/` vendored binaries/sources for re-derivation), `git diff main..<branch>`, and
  nothing from the writer's conversation. For a **re-review**, the human also relays the
  prior feedback block — findings live in blocks, not in the repo, so a fresh session can't
  verify prior-finding closure without it. Its output is the feedback block above, relayed
  verbatim by the human.
- **New writer**: a fresh session with this repo's CLAUDE.md gets the writer contract from
  its working-style section; this doc supplies the review-cycle detail.
- **New human coordinator**: your jobs are the ones agents can't do — relay blocks verbatim
  in both directions (or delegate the relay per §Automated orchestration), decide each
  Question finding, gate phase transitions, and call the merge.

## Automated orchestration

The writer session may run the review loop itself by spawning the reviewer as an isolated
subagent — replacing the human as ferry while leaving every human gate in place. This is the
same contract with a mechanical relay; nothing else in this doc changes. **The loop is a mode
the owner explicitly invokes per artifact** — the writer never enters it on its own judgment.
Invoking it is the owner's "go" for the whole loop: one authorized turn, ending at closure or
a break condition.

**Isolation requirements** (the non-negotiables):

- The reviewer is a **fresh agent** — never a context-inheriting fork of the writer. It sees
  only: the fixed trigger prompt below, the repo (CLAUDE.md included), and the diff. Nothing
  of the writer's conversation reaches it. Give it its own git worktree when available.
- The trigger prompt is **fixed and minimal** — the writer must not editorialize, summarize
  the artifact, or explain decisions in it. Everything load-bearing reaches the reviewer
  through the repo, same as a human-bootstrapped session:

  > You are the third-party reviewer for this repo. Read `docs/review-workflow.md` first and
  > operate strictly by it. Task: \<trigger phrase, e.g. "spec review branch \<name\>"\>.
  > You have the repo and `git diff main..<branch>`; you have no access to the writer's
  > conversation — re-derive everything from the tree. Write your feedback block verbatim to
  > \<audit file path\>, then return it as your final message. The contract's separate chat
  > summary is waived in this mode — the block's verdict line is the digest.

- **Every feedback block is surfaced verbatim** in the writer's conversation before being
  acted on — and the reviewer writes each block to the audit file named in the trigger, so a
  reviewer-authored copy exists outside the writer's retelling. The owner's audit reads the
  reviewer's files, not the writer's reprints. Audit files live **outside both git trees**
  (session scratch/temp — never repo-relative, which would sweep blocks into fix commits;
  never the reviewer's disposable worktree), one file per round, persisting until the
  owner's audit.
- The reviewer's separate chat summary (§The feedback block) is waived in automated mode —
  no human sits on the reviewer's channel; the writer surfaces each round's verdict line as
  the digest.

**Loop mechanics:**

- The writer processes each block under §The writer's side (verification, pushback with
  evidence, one commit per finding), then continues the **same reviewer agent** for the
  re-review — its own round-to-round context persists, mirroring a standing reviewer
  session. If the agent is lost, fall back to a fresh reviewer plus the prior block
  (§Bootstrap re-review rule).
- **Continuation messages are as constrained as the trigger**: only the re-review request
  ("rereview — fixes are committed") plus contract-sanctioned pushback (evidence with
  citations, per §The writer's side). Never narrative about why changes were made — that is
  the same contamination the fixed trigger exists to exclude.
- The loop runs until a **closure verdict** ("review closure — no action required"). The
  writer then presents the branch at the owner's merge gate, exactly as in the manual flow.

**Three conditions break the loop to the owner** — these are never automated past:

1. **Question findings** — the loop pauses, the owner decides, the decision is recorded
   (§Decision routing), then the loop resumes.
2. **Deadlock** — the writer disputes a finding with evidence and the reviewer re-asserts
   it. A genuine disagreement is escalated with both positions, not ping-ponged.
3. **Round cap: 5.** Hitting it means the artifact or the process is wrong; the owner sees
   the state as-is rather than the loop grinding on.

**What the owner gives up and keeps:** given up — the independent channel (everything
reaches the owner through the writer's conversation, mitigated by verbatim surfacing);
kept — Question decisions, the merge gate, and the option to run a separate-session
reviewer as an extra independent pass on any artifact, at any time.
