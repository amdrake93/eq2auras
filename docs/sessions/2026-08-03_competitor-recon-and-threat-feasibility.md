# Session chronicle — competitor recon, and a threat meter ruled out the hard way

*2026-08-03. A sprawling investigative session with no shipped code. It started as idle curiosity — reading hex codes off a screenshot of someone else's EQ2 meter — and turned into two things worth keeping: a clear-eyed read on the competing parser that appeared, and an exhaustive, ultimately-negative feasibility dig on a threat meter. The threat thread is the real story: a why-chain that dead-ends, several of my own errors that Alex caught, and a methodology (the controlled on-box probe) that finally turned "we can't build it" from a guess into a proof. Ends with the buildable ideas banked and the next direction named.*

## Part I — the competitor, and why it wasn't a fire

Alex pasted a screenshot of another player's DPS meter and asked for the hex codes. Sampling the pixels showed a 4-color **archetype** palette (Mage blue, Scout gold, Fighter rose, Priest green) — not per-class. That thread unspooled into "someone's building a competing EQ2 parser," teased on a Russian guild's stream as "coming soon."

The reflexive worry — a lookalike appearing right after he went public — didn't survive scrutiny, and the debunking is a reusable pattern:

- **The repo's own graph beat any web search.** 0 forks / 0 stars / 0 watchers at first check — no fork-button trail; the clone traffic was anonymous. A global GitHub code-search for Alex's distinctive identifiers (`AfterActionQueueThread`, `ApostropheNameFix`, `ClassSignatures`) surfaced only ACT-origin hits, no copy. (Correction that fell out: two identifiers Alex might treat as "his" are actually ACT's — so they're not fingerprints.)
- **The decisive fact came from asking, not sleuthing.** Alex asked the creator directly. The answer: class inference via **Census + fuzzy matching** (uncached), not Alex's compiled catalog. Independent solution, different data source — the whole "did he copy me" question dissolved. And the grey-until-a-few-seconds warmup we'd both flagged turned out to be *convergence* (any log-inference approach has it), never derivation.

The engine truth reaffirmed: **ACT emits zero class data.** Alex's class engine exists *because* of that; the competitor cleared the same bar independently via Census. Their tool is a fixed 5-window parser (DPS / HPS / Tanking / Timers / Alerts, drag-then-lock) — architecturally a *subset* of Alex's configurable-groups north star. Not a rival on the same bet; a simpler, different bet that looks similar. Also observed live: they shipped naive per-victim timer rows, then fixed it to caster-keyed by switching to `GetTimerFrames()` — speed-running Alex's own design history on stream. (A random Russian starred the repo mid-session; noted, shrugged at.)

## Part II — three feature ideas, and "the doc is about the program"

Competitor-watching surfaced real ideas, all banked to the backlog:

- **Class-name text label on meter rows** (per-window knob, default off) — we render class as *color* only; a text label complements the 24-subclass palette, which is genuinely hard to distinguish across a raid. Cheap: the class is already resolved for the fill.
- **Segment / history picker** — captured as a first-class item (referenced across SPEC/backlog, never had its own entry). Lean on ACT's `ZoneList`→`ZoneData.Items` (grouped by zone); never hold an `EncounterData` reference across polls.
- **Zonewide "All" segment pin** — bind a window to `ActiveZone.Items[0]` (the "Zone All listing" merge), read live each poll so it **auto-follows zone changes for free** — the never-cache-`EncounterData` rule forces exactly the re-resolve that makes it follow.

Two corrections from Alex worth internalizing for backlog writing: **no competitor mentions** (the repo is public; the backlog is about the *program*, not his motivations) and **no examples lifted from the competitor's screenshots** (character names). I'd drafted a "priority shift / direction" entry framed around the competitor — wrong doc entirely. Direction is memory, not the public backlog.

## Part III — the threat meter: a why-chain to a wall

Alex really wanted a threat meter — specifically the *macro* view: **2nd-place threat on enemies you're not targeting** (raid pull-awareness). The current-target 2nd-place already exists in DarqUI; the ask was the whole list. This became the session's long saga — a chain of "where could the data possibly come from," each link closed:

1. **Logs / ACT — no.** Threat isn't logged except explicit hate-mod ability lines (parse-engine case 12) — partial, useless as a meter.
2. **The clever inversion.** Alex's idea: don't *read* the client (the fork he rejected for class data) — inject EQ2 UI-XML that *emits* a bound value to a local log ACT reads. Stays in-contract (the game emits its own data; we consume via ACT) and it's the buff-tracker's emit-to-log pattern generalized. Refined to a *custom* log channel, not chat, to dodge the ToS/spam problem.
3. **The emit half never proved out.** Deep research claimed DrumsUI demonstrated a UI-handler→chat-command emission with a live value. **It didn't** — I pulled the actual DrumsUI threat source and it's a pure display reskin; every handler does only color/height logic. That was a subagent confabulation I'd relayed at "medium-high confidence." Across all of DrumsUI (threat, casting-macros, heroic) there is *no* autonomous emission. So the emit-to-log path is unproven and, on the evidence, likely dead.
4. **The data half — the ferry, and my errors.** We chased whether per-enemy threat is even exposed to the UI, over the two-machine ferry (Alex uploads real files → `spike-data/` → I `git show` them). I made three mistakes, each caught:
   - Treated an **eq2interface patch-*diff* snapshot** of `eq2ui_gamedata.xml` as the full registry → "definitively no per-enemy source" was wrong. Alex: *"this just is incorrect — I actively use a window with every enemy's threat."*
   - A **tag-stripping regex** in my own analysis hid attribute-borne definitions → I reported 0 threat entries when there were 6.
   - Overstated that **SOE's default template proves the field set** — Alex correctly noted it's the *same* argument as name-guessing (an element not *using* a value ≠ the value not existing).
5. **What the real files showed.** The enemy list is a data-bound `Listbox` over an **engine-provided** `ThreatListDS`; its rows are rendered by a template exposing only `$dispname$` / `$health$` / `$target$`. Threat is encoded purely as **sort order** — no per-entry value. `ThreatListDS` isn't declared in `gamedata` at all (engine-populated), so its fields can't be *enumerated*, only discovered.

**The methodology that finally settled it.** With no enumeration possible, the honest test is a **controlled on-box probe**: put a known-fake field (`$zzznotreal$`) in the row as a *control*, alongside candidate names, and read the candidates against how the control renders. Alex sharpened it twice — first calling out that trying names is guessing (true; the control is what makes it rigorous, not blind), then catching that SOE's `health_color` proves **snake_case** for multi-word fields, so my `secondarythreat`/`threatpct` were malformed. The corrected round tested proper `secondary_threat` / `threat_pct` / `threat_position` / etc. **All blank against the control.** ~13 names across both conventions, two independent templates, one live control — a *real* negative, not an inference.

**Verdict: not feasible.** EQ2 exposes threat only as current-target scalars (already visible in-game) and list sort-order — no per-enemy value reaches the UI. Recorded as a `❌ NOT FEASIBLE` backlog entry with the full why, so it isn't re-opened. "Well that sucks," but a *known* no beats a lingering maybe.

## Part IV — housekeeping and lessons

- **Public-repo hygiene.** The ferried SOE/DarqUI UI-XML files were committed to `spike-data/` to move them Mac-ward; once done, Alex flagged we shouldn't redistribute third-party files from a public repo. Removed from HEAD (no history rewrite — these ship to every EQ2 client / DarqUI is a free public mod, so low stakes).
- **Verify subagent claims against source.** The DrumsUI confabulation is the cautionary tale — a load-bearing "it's precedented" that dissolved the moment I read the actual file. Fetching the real artifact caught it; relaying the claim didn't.
- **Diff-snapshots aren't full files.** eq2interface's `/patches/` pages are per-update diffs; my own earlier note said so, and I ignored it once.
- **A control turns guessing into a probe.** The single most useful methodological move of the session — and it came from Alex pushing back on blind name-guessing.

## Where we are

Threat is closed — investigated, ruled out, documented, borrowed files out of the tree. The buildable ideas are banked: the class-name row label, and the named next direction — **the segment picker + the persistent zonewide option**. Next session picks up there, Alex-owned brainstorm first.
