# 2026-08-09 — the damage-amp burst investigation

## Context
Started as "weird damage" from the **Vampire Lord Mayong Mistmoore** kill (2026-08-09, ~9m29s,
24 players, log owner Biffels). Mid-analysis Alex recalled a second instance from the start of
the expansion; that log (**Clockwork Menace**, 2026-06-23, ~12m20s) confirmed the phenomenon is
encounter-independent and recurring. Investigation status: **open — archive sweep pending**.

## Ferried files
- `tntmayongweirddmg` — raw EQ2 log, VL Mayong fight only (2026-08-09 20:57–21:07)
- `menaceweirddmg` — raw EQ2 log, Clockwork Menace fight only (2026-06-23 19:37–19:50)
- `Wuoshi.7z.001–005` — **unswept**: ~1.7GB raw log archive (Biffels history; one Galdenya
  file inside to be ignored). Extract to scratchpad, do NOT commit extracted contents.
  These archives get history-scrubbed (filter-repo/BFG) after the spike.
- (POC input reused from `../2026-07-27/eq2log_Biffels.2026.07.22.7z` — 3 raid-day logs.)

## The phenomenon
Once per fight, for ~9–10 seconds, a handful of **direct-hit damage packets against the boss**
land at 2×–50× their normal value.

**Mayong burst — 21:03:16–25 (12 hits, 6 players):**
| hit | value | vs matched ceiling |
|---|---|---|
| Biffels Head Shot | 125,300 crit | ×5.6 |
| Drizzlen Lung Puncture | 109,700 crit | ×11.9 |
| Sprok Wisp Blade | 40,973 crit | ×3.3 |
| Flepead Ceremonial Blade | 36,407 crit | ×6.6 |
| Biffels Masked Strike | 34,580 crit | ×3.2 |
| Bardomir Ceremonial Blade | 27,943 NONcrit | ×6.8 |
| Sprok Storm Surge | 23,507 NONcrit | ×3.9 |
| Kludian Poisoned Spike | 21,566 crit | ×5.0 |
| Drizzlen Hamstring | 19,377 crit | ×2.7 |
| Biffels Quick Strike | 15,700 NONcrit | ×3.1 |
| Drizzlen autos ×2 | 14,913 / 13,287 crit | ×2.7 / ×2.4 |

**Menace burst — 19:48:30–38 (14 hits, 5 players):** Biffels Gushing Wound 101,800 NONcrit
(×50.5!), Paralyzing Strike 87,718 (×6.4), Masked Strike 34,987 (×3.7); Drizzlen Arctic Blast
51,022 (×9.6), Walk the Plank 33,792; Sprok Wisp Blade 41,284 (×3.4), Aery Whip, Storm Surge;
Majoras Darksong Blade 28,071 (×15.4); Goose Decimate 39,276. Plus Biffels Assassinate
260.0K crit at 19:48:34 (~×2 his 111–131K normals).

## Established properties (each with evidence)
1. **Per-packet, not per-cast**: one packet of a multi-packet cast amps, siblings stay normal
   (Ceremonial Blade p1-only — both bards; Arctic Blast part-1 only; Gushing Wound).
2. **Ability-keyed magnitude, applied pre-crit**: CB portion-pair reconstruction gives ×6.19
   (Flepead, crit) and ×6.79 (Bardomir, NONcrit) — same factor within roll variance; as a flat
   pre-crit add both come out ~23.5–23.8k (1% apart). Small-base abilities show huge ratios
   (GW ×50), big-base small ones (Assassinate ×2).
3. **Fight- and gear-independent per ability**: Sprok Wisp Blade amped to 41.0k vs 41.3k across
   two fights 6 weeks apart (normals ~12.1k vs ~12.4k → ×3.3–3.4 both times); Biffels Masked
   Strike 34,580 vs 34,987.
4. **Boss-target only**: 47 window hits on adds — 0 amped. **Damage only**: 312 window heals —
   0 amped. **Player-side only**: 0 anomalies in boss outgoing damage, either fight.
5. **No player selection**: ~2–4% of direct packets amp; binomial-consistent with per-hit
   chance once DoT ticks are excluded (ticks never amp). Fast swingers (Biffels, Drizzlen,
   Sprok) simply sample more; they amped in BOTH fights.
6. **Once per fight, ~9–10s** (both fights). Menace also had 3–4 weak scattered flags
   (×2–4) that fit no pattern — unresolved.

## The mark-displacement correlation (tracer vs trigger — UNRESOLVED)
Amp bursts coincide with **both Death Mark (Biffels, assassin) and Mark of Divinity (Cheggers,
templar) being reflected onto players nearly simultaneously**:
- 2/2: both marks bounce within ~1s → burst (Mayong 21:03:15+16; Menace 19:48:28 same second).
- 0/6: single mark bounces (or 13s-apart non-overlapping pair, Mayong E1) → no burst.
- **All four burst endpoints track the DM-displacement residence window ±1–2s** (bounce→cure:
  Mayong :16–:20 vs amps :16–:21; Menace :28–:37 vs amps :30–:38).
- Joint chance of the fights' rare mark-bounces landing in both amp windows randomly ≈ 1e-3.

**Alex's objection (standing, and census-supported):** these are just high-value, frequently
cast spells that any reflect will catch — possibly pure coincidence of the fight's nature.
Census (spell data, `s:eq2i`) confirms both are **closed single-target trigger systems**
incapable by design of amping others: DM = 5%-on-melee-hit → "Marked" → Agonizing Pain ×5
triggers (payload measured in-log: ~5–8k — far below every amp tier); MoD = −arcane-mit +
20% chance to HoT the attacker (the fight-long "X is marked" spam = that proc). Displacement
verified real: Agonizing Pain fires ~2.4×/10s all fight but **0 times in all 6 DM-displaced
windows** across both fights.

Both readings survive: marks as **tracers** of an unlogged boss state (which reflects whatever
is mid-flight at onset), or as an **ingredient** somehow. No reflect-density anomaly exists at
either onset (max 4 reflects/s fight-wide; onsets sit at 2–3). Mayong reflects episodically
(3 episodes; only ep3 amped); Menace reflects continuously (460 lines, flat) — so reflect
uptime alone provably does not produce amps.

## Ruled out (with the evidence that killed each)
- Boss emote trigger (all E3-window emotes recur fight-wide; episode template-diff empty)
- Raid-wide buff / potency / crit-bonus (per-packet selectivity; NONcrit amps; ~1.0× siblings)
- Group temp (amped set spans groups — Alex confirmed)
- Stealth/positional openers (Alex: not stealth abilities; Assassinate normal at 122K)
- Reflect-uptime, reflect-density pulse (Menace control; onset density normal)
- Add deaths ("My death is only temporary" — deaths every ~30–40s all fight)
- "Frightening alacrity" proc (constant), "is marked" adjacency (coincidence-level)
- Marks as *mechanical* amp source (census structure + AP payload size)
- Gear/stat scaling (cross-fight value constancy)

## Analysis pitfalls (worth remembering for parse work generally)
- **EQ2 logs abbreviate ≥100k as `125.3K`** — comma-format regexes silently drop the biggest
  hits (this hid the headline 125k for the first hour). ACT expands via `ExpandDamageAmount`
  (docs/act-parse-engine.md line ~307).
- **Match populations on (ability, damage type, crit)** — mixing crits with non-crits hid the
  Quick Strike amp; Biffels' weapon infusion converts phys→disease so dtype matters.
- **Twin amps mask each other** in ceiling tests — compute baselines EXCLUDING the suspect
  window (or vs max-of-others).
- **Bimodal abilities** (small+big hit forms) make ×median tests scream falsely — Quick
  Strike/Fiery Annihilation/Shadow Coil "amps" at ×10 median were the ability's normal big form
  (top ÷ 2nd-best = 1.0 across dozens of hits).
- Day-local baselines for archive sweeps (gear drift), else every upgrade looks like an amp.

## Tooling (all in this dir, py3, no deps)
- `sweep.py` — **the archive tool**: streams GB logs, day-local populations, tightened burst
  criteria (≥3 flags in 12s, ≥2 attackers, max ≥20k, ratio ≥4; weak clusters listed one-line),
  `--cohort "Vampire Lord Mayong Mistmoore"` emits per-pull table (kill, DM/MoD bounces,
  ≤8s overlap, burst y/n). Validated: finds exactly the 2 known bursts; 0 false bursts on a
  75MB raw day-log (3.5s runtime → full archive ≈ 2 min).
- `burst_detect.py` — single-fight version of the same.
- `fight_scan.py <log> <boss> [owner]` — per-fight amp scan + reflect density buckets.
- `analyze.py`/`window.py`/`boundary.py`/`outliers.py`/`deep.py` — the Mayong exploratory
  chain (per-second curve, window ratios, ceiling-breakers, enrichment, adjacency).
- `reflect_amp.py` — reflected-player vs amped-player disproof; `probes.py` — CB portion
  pairs, add-death timeline; `xfight.py` — cross-fight window template intersection (found
  the marks); `verify_retraction.py` — bimodal-population proof.

## ARCHIVE SWEEP RESULTS (2026-08-10 — 17 day-logs, Jun 15–Aug 9, 1.7GB, 81s runtime)
**Five real bursts** (≤9s wall, 10–15 amped hits, 5–9 attackers, ability-keyed ×3–×50), on
**four different bosses**: Jun 21 D'Lizta Cheroon, Jun 23 Clockwork Menace, Jun 23 Tender of
the Seedlings ×2, Aug 9 VL Mayong. Six other detector "bursts" = confirmed noise (sparse,
30–56s spread, auto-crit variance / boss-owned abilities).

**Mark of Divinity reflection is THE trigger — Death Mark exonerated** (Alex called it):
- 12 MoD-bounce events in the archive (all Cheggers). **All 5 bursts start 1–2s after one.**
- Tender#2 burst had NO Death Mark bounce at all; D'Lizta/Tender#1 DM bounces came 20–30s
  AFTER their bursts. The earlier "both marks" conjunction was DM riding along (Biffels
  casts DM into every reflect window; it's just the most-reflected spell).
- Amp magnitudes are era-stable per ability (Wisp Blade amped 41.0k Jun / 41.3k Aug;
  Masked Strike 35.0k/34.6k) — the engine is constant wherever it fires.

**THE OPEN MYSTERY — the era gate.** MoD-bounce → burst: **4/4 in June (21–23), 0/5 Jul 5–28,
1/2 Aug 9** (the Aug 9 dud = fight-open bounce, 4s residence, low swing volume — plausible
zero). Eliminated as the gate: cure speed (bursts cured +3/4s; longest dud residence +9s),
boss identity (Tender & Mayong appear on BOTH sides), raid roster (no player separates burst
from dud nights, either direction), Cheggers' spell (Mark of Nobility proc payload flat
~120–210 median across all eras), reflect behavior (no density/pattern change). Public patch
notes at both boundaries (Jun 23, Jul 28 updates) mention nothing relevant — but the June
notes DO fix a same-family bug elsewhere ("Do'Guen's triggered effect… down from 570,000%"),
so silently-patched trigger-scaling bugs are demonstrably this era's churn. **Best remaining
explanation: silent server-side change (fixed ~Jun 24–Jul 4, regressed by Aug 9). Not
provable from logs.**

## MARK OF DIVINITY RESEARCH (2026-08-10) — exonerated as the engine
Exhaustive check of the spell across every version (live census all 10 quality tiers;
historical ranks I–VI L18→83; item "Mark of the Celestial" → MoD IV; AA "Enhance: Mark of
Divinity", Kunark Ascending 2016): **every version is arcane/divine-resist + combat-mit debuff
plus a 20%-on-MELEE proc (Mark of Nobility) that HEALS the attacker 6–140. No version, era,
AA, item, or focus adds/deals/amplifies damage.** Mark of Nobility isn't standalone (only the
embedded heal). Two independent disproofs it causes the amp: (1) MoD payload is 6–140; amps are
+23k–125k. (2) MoD triggers only on melee-weapon damage, but amps hit SPELLS too (Storm Surge
cold, Ceremonial Blade mental, Darksong Blade disease) — a melee-gated proc can't fire on those.
=> MoD-reflect is a **tracer**, not the trigger. Confirms Alex's instinct + the earlier
tracer branch.

**Best model = server-side (emulator) issue, not live EQ2.** No official MoD can do this;
official patch notes at both era boundaries (Jun 23, Jul 28 updates) contain nothing relevant
(nearest: a June fix to a *different* triggered effect "down from 570,000%" — trigger-scaling
bugs were live-side churn that expansion). The amp's on/off/on cadence (Jun on → Jul off →
Aug on) fits **server data re-imports** (the "live data leaks into our version" phenomenon),
not a client ability. Amp fingerprint (per-hit chance → fixed, ability-keyed, pre-crit add, on
melee AND spell hits) = a damage-proc / scaling-error applied to the triggering ability,
sourced in the server's reflect handling or a mismatched spell-effect row. Unreachable from
public data — lives in the emu's build/changelog.
Sources: census s:eq2i eq2/spell (Mark of Divinity ×10 tiers, Mark of Nobility 0 standalone);
ZAM Mark of the Celestial; tentonhammer templar spell list; eq2wire KA 2016 update notes;
everquest2.com update notes 6-23 / 7-28 / 6-9-2026.

## Next steps
1. **In-game reproduction on current game version**: during a reflect phase, deliberately
   bounce a reflected ability (NOT MoD-specific — MoD is exonerated; bounce whatever the raid
   normally reflects). If bursts follow, mechanism is live: on-demand ~7s free-damage window
   (time big CAs into it), or a clean bug report. If never reproduces, Aug 9 was the tail of
   another silent server change.
2. **Ask the server's dev team / changelog** about spell-data or reflect-handler changes
   around late June and early August 2026 — that's where the real toggle lives.
3. **If a server-side spell dump is available**, diff the amped abilities' effect rows against
   live census to find the mismatched/bugged row directly.
2. Optionally re-sweep future ferried logs with `sweep.py` (validated: 5/5 known bursts,
   0 false positives at burst tier) to track whether the mechanism stays live.
3. After the spike: untrack the big archives + history-scrub (filter-repo/BFG).
