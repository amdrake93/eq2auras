# Session chronicle — the segment picker survives the field, and a hashcode that stringifies your whole night

*2026-08-04 (spanning a late-night 08-03 start through 08-06 wrap-up). The session that took the segment picker from "merged" to "field-hardened" — and, in doing so, tripped over a latent O(all-swings) performance trap that had been silently taxing every fight since the meter first shipped. Two of the three big threads are reversals of first impressions: a "major memory problem" that turned out to be ACT, not us; and a "new" hard lockup that turned out to be an ancient hashcode trap the segment picker merely held still long enough to expose. Ships five dev releases (1.3.37 → 1.3.46), ends with a candidate we deliberately did **not** promote, and leaves two durable artifacts — a design-time SPEC rule and a runtime observability item — as bookends around the failure class we learned the hard way.*

## Part I — field-hardening the picker

The picker shipped functional; the field found the rough edges. A dozen polish items in the first pass (Σ spacing, hover states, respect the window font, right-align durations, precompute flyout width, chip-won't-open-when-locked, popups opening upward, Lock→Unlock label, bold/italic not respected, a too-faint name outline) — then a run of behavioural bugs that each taught something:

- **The chip opened on mouse-*down*** → a `StaysOpen=false` Popup read the button-*up* as an outside-click and closed itself. Hold-to-open/release-to-close. Fix: `Down` handles (and blocks the drag), `Up` opens.
- **The first zone in the flyout was always expanded**, selecting a segment sometimes **silently reverted to Current**, and opening the picker on a big import **froze** for seconds. The revert was a fresh-pick-vs-culled-handle race; the freeze was the picker forcing ACT to compute an ally-cache (`GetEncounterSuccessLevel()`) on *every imported encounter*. Branch-3 fixed all three: remember the expanded zone, scope the ally-cache to current+expanded zones only, and distinguish "just picked, not resolved yet" from "gone."

A knob-model wrinkle resolved by fiat: *"just have the zonewide pick uncheck the box"* — picking Zonewide auto-pins (clears Return-to-Current in one gesture). 1.3.37 → 1.3.39.

## Part II — the memory scare that wasn't ours

Then: *"major memory problem, all of ACT is laggy... reached 1GB even after clearing all encounters."* The reflex was to hunt a plugin leak. The discipline that settled it was refusing to: Alex reproduced it on **stable 1.3.0 — which has no segment picker at all**, and then ran the deciding A/B. Fresh ACT + our plugin, import a 145MB raid log → **1253MB**. Same import with the **plugin disabled → 1159MB**. The plugin's entire contribution was ~94MB; the gigabyte was ACT holding a parsed raid log with a .NET heap that won't shrink after "clear." A read-only leak audit over the plugin independently confirmed no unbounded retention.

The lesson, banked: **the "disable the plugin" A/B settles "is it us" faster and more honestly than any code audit.** Don't chase the plugin first.

## Part III — readability, a toggle, and the outline that took three tries

Two field asks landed together: names were unreadable on light class fills, and users wanted class colours off entirely. The toggle was clean — a per-window `disableClassColors` (DCJS-inverted so the 0-value is "colours on"), gated through one DRY `OverlayHost.ColorResolverFor` so *every* coloured surface greys uniformly.

The outline was not clean — it took three swings, each field-rejected before the last:

1. A single soft `DropShadowEffect` — a Gaussian glow whose alpha spreads across the blur radius, so the letter edge stays thin.
2. My "fix": a **compounded** nested pair of glows. Darker, still soft, still washed out on white. Alex: *"still not enough."*
3. The right answer, finally: a **true layered stroke** — the name rendered in black, offset 1px in all eight directions behind the white text. Opaque by construction. (The escalation I'd flagged as "next" the first time and should probably have reached for sooner.)

A separate font bug rode along: once Bold took, you couldn't un-set it. Hosted from WPF, the native `FontDialog` wouldn't reliably round-trip a *de*-selected style. Fix: family/size stay on the dialog; **Bold/Italic became dedicated checkboxes** — deterministic on/off. 1.3.41 → 1.3.44.

## Part IV — the lockup, and a hashcode that walks your whole night

Then the real story. Point a window at a zone's **"All" segment** — the 103-minute Emerald Halls aggregate — and **ACT hard-locks**, memory flat. Flat memory + hard lock = CPU/contention, not a leak.

Alex fed three field data points that, together, were almost a diagnosis: a **5-second** delay before a mouseover painted (the hover read waits on the same lock the poll holds → the poll is *inside the lock for seconds*); **instant** again on a bounded 7-minute fight; and — the clincher — that same 7-minute fight, only ~35 combatants, was **still noticeably laggy**. A small roster that's still slow means the cost scales with **swings, not combatants**.

Rather than guess, we ran a **holistic Fable-5 performance review** — three fresh reviewers, three slices (ACT read-path / Core compute / WPF render), each hand-tracing its layer's cost, none seeing the others or my analysis. All three **independently converged on one line**:

```csharp
var allySet = new HashSet<CombatantData>(encounter.GetAllies(true));
```

A `HashSet<CombatantData>` with no comparer calls `CombatantData.GetHashCode()` — and per our own decompile doc, ACT computes that hash by walking **every swing the combatant owns and calling `MasterSwing.ToString()` on each**. So membership isn't O(combatants); it's **O(all swings in the segment)** — paid twice per poll (the main snapshot plus the deaths sweep), inside the lock, on ACT's UI thread. On a 30-second pull, nothing. On a 103-minute aggregate, millions of transient strings per poll → a saturated UI thread and flat memory (gen-0 garbage). Every symptom explained, exactly.

My own hypothesis had been coarser — O(combatants) loops plus a one-time deaths blast — and had *missed* the multiplier the reviewers caught: it's the swing-deep hash that turns "thousands of combatants" into "seconds." Three independent agents landing on the same line is as close to proof as this gets.

The kicker: that line runs on the **Current** segment too. This tax has been on **every fight since the meter shipped** — invisible only because each fight's cost died when the segment rolled to the next pull. A *pinned* long segment was the first thing to hold still long enough to feel it.

The fix is one-ish line: key ally membership by **name** (`HashSet<string>`, OrdinalIgnoreCase — ACT's own identity model), which never touches the swing-deep hash. On-box: **the 103-minute "All" now loads and mouseovers as fast as a 30-second fight.** 1.3.46. *"ACT really took the uniqueness of a hash to a new level."*

## Part V — the bookends, and a YAGNI call

Two durable things came out of the lockup, aimed at the same failure class from opposite ends:

- **Design-time:** a new SPEC **§Runtime-scaling discipline** — an emphatic, always-consider warning that ACT's model is compositional and swing-deep, that the trap hides in innocuous operations (any `HashSet<CombatantData>`/`.Contains`/`.Distinct`), that flat memory *hides* it, and the rule: **budget every per-poll/under-lock read against a two-hour raid "All", counted in swings not combatants.**
- **Runtime (Alex's ask, now likely-next):** a **poll-health observability** item — a real-time latency / lock-hold readout plus a per-session high-water-mark, so a regression like this shows up during his own testing and gives a *warm fuzzy before promotion*, without bolting an SLA onto every action. Instrument the poll boundary once, not every feature.

And a clean **YAGNI call**: the reviewers had also mapped a whole "a complete segment should never be on the poll" architecture pass (frozen-segment load-once cache, deaths gating, open-card frozen-skip). But the hotfix worked so completely — 103 minutes performs like 30 seconds — that the remaining findings became *efficiency, not correctness*: reclaiming invisible cycles at the cost of a cache-invalidation layer's risk. Parked, not queued; kept as documented levers to pull only if a concrete symptom ever appears.

## Where we are

`dev-latest` is **1.3.46** — the full stack (segment picker + readability/class-colour/font fixes + the perf hotfix) and the real promotion candidate. Initial on-box testing was good, but we **deliberately did not promote**: Alex is away from the Windows box and won't ship a stable he can't field-test. Fix-forward if anything surfaces when he tests the candidate later. Next session: **poll-health observability** (fleshed and ready to brainstorm), and the **1.4 promotion** once the candidate has had a real field pass.
