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

## Next steps
1. **Tomorrow: full archive sweep.** Extract Wuoshi volumes (7zz installed) to scratchpad,
   skip the Galdenya file, `sweep.py` everything + the three July day-logs with the Mayong
   cohort. Deliverable: pulls × {bounce-overlap, burst} contingency table + any new bursts.
   "Vampire Lord" prefix = the reflect version of the Mayong fight (cohort filter is correct).
2. **In-game discriminator** (raid, whenever): during a reflect phase, deliberately bounce
   Death Mark + Mark of Divinity together. Burst follows → on-demand reproduction (and free
   burst window to exploit/report). Nothing → tracer reading wins; amp is a hidden boss state.
3. After the spike: untrack the big archives + history-scrub (filter-repo/BFG).
