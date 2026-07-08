# ACT Timer Engine — decompiled reference

The complete data pipeline of ACT's spell timer system, read from the shipped binary. This is
ground truth for the exact `Advanced Combat Tracker.exe` vendored in `ThirdParty/` — re-derive
after any ACT upgrade.

**Sources:** `[decompiled]` = read from the binary (commands below). `[field]` = cross-validated
against the 2026-07-05 raid capture (`spike-data/2026-07-05/`). Where this doc contradicts
`docs/plans/2026-07-01-spike-findings.md`, **this doc wins** — the July 1 findings were correct
observations of single-trigger idle-log tests, but two of their generalizations don't survive
raid scale (see §Supersessions).

## Re-derivation

```sh
dotnet tool install -g ilspycmd   # once
~/.dotnet/tools/ilspycmd -t Advanced_Combat_Tracker.FormSpellTimers "ThirdParty/Advanced Combat Tracker.exe"
# likewise: TimerData, TimerFrame, SpellTimer
```

Everything below comes from four classes: `TimerData` (the configured trigger),
`TimerFrame` (one live timer per `(Name, Combatant)`), `SpellTimer` (one instance inside a
frame), and `FormSpellTimers` (the engine: `NotifySpell`, the update loop, `GetTimerFrames`,
the native render).

## Data model `[decompiled]`

**`TimerData`** — the configuration, keyed `category.ToLower() + "|" + name.ToLower()`:

| Field | Default | Meaning |
|---|---|---|
| `TimerValue` | 30 | countdown seconds |
| `WarningValue` | 10 | warning threshold |
| `RemoveValue` | **−15** | instance purge threshold (see §Purge) |
| `OnlyMasterTicks` | false | checked = every trigger is a master reset (see §Master flag) |
| `AbsoluteTiming` | false | frame property `OneOnly`: while a master is alive (>0), new triggers are dropped entirely |
| `RestrictToMe` | false | only trigger when self is attacker/victim (or whitelisted) |
| `RestrictToCategory` | false | category must match attacker/victim/zone |
| `Panel1Display` / `Panel2Display` | true / false | panel routing flags |
| `FillColor`, `Modable`, `ActiveInList`, `RadialDisplay`, sounds | — | display/sound config |

**`TimerFrame`** — one per `(Name, Combatant)` (`GetKey` = `"{SpellName} - {Combatant}"`),
holds `List<SpellTimer> SpellTimers` plus sound-state flags (`WarningSounded`, `ExpireSounded`).
Key helpers: `MasterExists`, `GetLargestVal(bool IncludeNonMaster)`,
`GetMostRecentTime(bool IncludeNonMaster)`.

**`SpellTimer`** — one instance: `bool MasterTimer`, `DateTime StartTime`,
`TimerFinalDuration` (duration × (1 + timer mods)), and
`TimeLeft = TimerFinalDuration − (int)(LastEstimatedTime − StartTime).TotalSeconds` —
**no clamp, goes negative; derived from ACT's log-driven clock**, so it lurches when the log
is quiet (why the overlay smooths from wall-clock `StartTime` instead).

## The trigger pipeline: `NotifySpell` `[decompiled]`

Every trigger match runs these gates **in order**:

1. **Timer lookup** by spell name; category filter (`RestrictToCategory` → category must equal
   attacker, victim, or current zone) and `ActiveInList` filter. A category-restricted match
   beats an unrestricted one.
2. **`RestrictToMe` gate** — dropped unless self/whitelisted attacker or victim.
3. **Frame get-or-create** for `(Name, Attacker)`. An existing frame gets its `TimerData`
   refreshed (config edits apply live).
4. **`AbsoluteTiming` (OneOnly) gate** — if set and any master instance is still positive, the
   trigger is **dropped** (no reset until expiry).
5. **2-second dedup** — if *any* instance (master or not) started < 2s ago, the trigger is
   **dropped**. `[field]` This is why ~20 per-raid-member hit lines in one second produced
   exactly one instance.
6. **Master flag decision** — if the newest instance (master or not) started < **12s** ago
   *and* `OnlyMasterTicks` is unchecked → the new instance is **`MasterTimer: false`** (a
   bookkeeping "tick" instance). Otherwise → **`MasterTimer: true`**: sound flags re-arm and
   the start sound plays. `[field]` A 6s-cadence DoT (Blanket) therefore accumulates one master
   + N non-master ticks; a 41–46s recast (Soul Paralysis) produces all-masters.
7. **Append, always** — the instance is added to `frame.SpellTimers`. Nothing is ever reset or
   replaced by a trigger; frames only shrink in the purge loop.
8. **`OnSpellTimerNotify(frame)`** fires (after append).

## What ACT itself displays and sounds `[decompiled]`

All native display/sound semantics key off **`GetLargestVal(IncludeNonMaster: false)`** — the
largest master-only `TimeLeft`, which for cooldown-style timers is effectively **the most
recent master reset**:

- **Native window render**: shows largest-master. **Non-master tick instances are never
  displayed by ACT.** This is the "extra logic" that makes ACT's window look sane while the
  raw frame data stacks instances.
- **Warning**: fires (sound + `OnSpellTimerWarning`) when largest-master ≤ `WarningValue`,
  once per master re-arm (`WarningSounded`).
- **Expire**: fires (sound + `OnSpellTimerExpire`) when largest-master ≤ 0, once per re-arm
  (`ExpireSounded`).

## Purge rules `[decompiled]` `[field]`

The update loop, each pass:

1. **Instance purge** — every instance with `TimeLeft < RemoveValue` is removed from its
   frame. Silent: **no event fires for an instance purge.**
2. **Frame kill** — any frame with zero instances **or no master remaining** is removed and
   `OnSpellTimerRemoved(frame)` fires. The no-master rule means a frame dies **even while live
   non-master ticks remain**. `[field]` 20:24:31 raid capture: Blanket's single master purged
   at `RemoveValue` and the frame kill destroyed 10 live tick instances, one with 67s left.
   Conversely 20:17:34 (Soul Paralysis): the old master purged but a newer master (recast)
   existed → only the instance vanished, frame survived.

Consequences:

- A frame's maximum overdue linger is `|RemoveValue|` seconds past the last master's zero.
- Both "purge modes" observed in the field are one deterministic rule: **does another master
  exist when an instance purges?**

## Event handler caveats

- Handlers receive the **frame**, not the instance that caused the event. Any per-instance
  value read in a handler (e.g. `SpellTimers[0].TimeLeft`) is arbitrary — at raid scale it
  produced `warning` records tagged with a *different instance's* negative time.
  (Our `TimerProbe.LogFrameEvent` has exactly this flaw — diagnostics only, display unaffected.)
- `OnSpellTimerRemoved` fires after the frame is already emptied or de-mastered; `SpellTimers`
  may be empty (July 1's `timeLeft=null` removals).

## What `GetTimerFrames()` returns

`GetTimerFrames(0)` (the no-arg overload the overlay polls) returns **every frame, unfiltered,
with every instance — masters and non-master ticks alike**, and no panel filtering. Panel
filtering only happens for `PanelNum` 1/2. **The `MasterTimer` flag is public on each
instance**; any consumer that ignores it (as our probe did through v0.1.85) sees tick noise
ACT's own window never shows.

## Supersessions of 2026-07-01 spike findings

- **"Soonest instance governs, same as ACT's native window" — wrong.** ACT's window shows the
  **largest master-only** value (≈ newest master). The July 1 test used a single re-fired
  trigger where the re-fire landed inside the 12s window → non-master → ACT's window kept
  showing the original master (the soonest *and only* master), which *looked like*
  soonest-governs. The "phantom reset" bug that killed newest-wins was real, but its cause was
  treating a **non-master tick** as a reset — newest-*master*-wins does not have that problem.
- **"RemoveValue does not govern frame lifetime" — wrong.** It governs exactly (instance purge
  at `TimeLeft < RemoveValue`, frame kill when no master remains). The July 1 timer vanished
  ~1s past zero because *that timer's* configured `RemoveValue` was ~0/−1 (the field's Holy
  Shield behaves identically), not because the default −15 is ignored. `[field]` The July 5
  boss timers with linger config purged at −16, on schedule.

## Implications for eq2auras (pointers, not spec)

- The governing rule the overlay needs is **newest master instance per frame; non-masters
  invisible** — this matches ACT's native semantics, Alex's cooldown-tracking model, and
  eliminates DoT tick noise with **no trigger reconfiguration** (leave `OnlyMasterTicks`
  unchecked; checked would make every DoT tick a full reset with sounds).
- `TimerProbe` must capture `instance.MasterTimer` (and already has `StartTime`); the spike
  JSONL schema should log it so future field data is self-explanatory.
- Spec amendment for the governing rule is queued in `docs/backlog.md` (2026-07-05 raid
  analysis).
