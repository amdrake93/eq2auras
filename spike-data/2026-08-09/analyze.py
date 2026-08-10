#!/usr/bin/env python3
"""Structured parse of the Mayong fight log + spike analysis. No mechanic assumptions."""
import re, sys, statistics
from collections import defaultdict

LOG = "tntmayongweirddmg"
OWNER = "Biffels"  # log owner = YOU/YOUR/YOURSELF

def expand(amt: str) -> int:
    amt = amt.replace(",", "")
    m = re.match(r"^([0-9]+(?:\.[0-9]+)?)([KMBTQ]?)$", amt)
    if not m:
        return None
    val = float(m.group(1))
    mult = {"": 1, "K": 1e3, "M": 1e6, "B": 1e9, "T": 1e12, "Q": 1e15}[m.group(2)]
    return int(round(val * mult))

# (epoch)[Www Mmm DD HH:MM:SS YYYY] BODY
LINE = re.compile(r"^\((\d+)\)\[[A-Za-z]{3} [A-Za-z]{3}\s+\d+ (\d\d:\d\d:\d\d) \d{4}\] (.*)$")

# hit forms:
#   ATTACKER's ABILITY hits TARGET for [a critical of ]AMOUNT TYPE damage.
#   ATTACKER hits/multi attacks TARGET for [a critical of ]AMOUNT TYPE damage.
HIT = re.compile(
    r"^(.*?) (?:hits|multi attacks|flurries) (.*?) for (a critical of )?([0-9][0-9,\.]*[KMBTQ]?) ([a-z]+) damage\.$"
)

events = []  # dict rows
with open(LOG, encoding="utf-8", errors="replace") as f:
    for raw in f:
        lm = LINE.match(raw.rstrip("\n"))
        if not lm:
            continue
        epoch, tstr, body = int(lm.group(1)), lm.group(2), lm.group(3)
        # normalize owner tokens
        body = body.replace("YOUR ", f"{OWNER}'s ").replace("YOU hit", f"{OWNER} hit")
        hm = HIT.match(body)
        if not hm:
            continue
        left, target, crit, amt_s, dtype = hm.groups()
        amt = expand(amt_s)
        if amt is None:
            continue
        # attacker / ability split on possessive
        if "'s " in left:
            attacker, ability = left.split("'s ", 1)
        elif left.endswith("' "):  # names ending in s -> "Vicious' "
            attacker, ability = left[:-2], "(auto)"
        else:
            attacker, ability = left, "(auto)"
        # strip pet chains: keep top-level owner as attacker
        top = attacker.split("'s ")[0]
        events.append(dict(epoch=epoch, t=tstr, attacker=attacker, top=top,
                           ability=ability, target=target, amt=amt, crit=bool(crit),
                           dtype=dtype, abbrev=bool(re.search(r"[KMBTQ]$", amt_s.replace(",", "")))))

print(f"parsed {len(events)} damage events")
mayong = [e for e in events if "Mayong" in e["target"]]
print(f"  {len(mayong)} to Mayong")

# ---- 1. per-second raid damage TO Mayong (players only: exclude Mayong-as-attacker) ----
players_dmg = [e for e in mayong if "Mayong" not in e["attacker"]]
per_sec = defaultdict(int)
for e in players_dmg:
    per_sec[e["t"]] += e["amt"]
top_sec = sorted(per_sec.items(), key=lambda kv: kv[1], reverse=True)[:15]
print("\n=== TOP 15 seconds by raid damage TO Mayong ===")
for t, v in top_sec:
    print(f"  {t}  {v:>12,}")

# ---- 2. breakdown of the single peak second ----
peak_t = top_sec[0][0]
print(f"\n=== breakdown of peak second {peak_t} (hits >= 3,000) ===")
rows = [e for e in players_dmg if e["t"] == peak_t and e["amt"] >= 3000]
for e in sorted(rows, key=lambda e: e["amt"], reverse=True):
    flag = " *ABBREV*" if e["abbrev"] else ""
    c = "crit" if e["crit"] else "    "
    print(f"  {e['amt']:>10,} {c} {e['top']:<12} {e['ability']:<22} {e['dtype']}{flag}")

# ---- 3. named abilities: full distribution + outlier factor ----
def dist(top, ability):
    xs = [(e["t"], e["amt"], e["crit"]) for e in players_dmg
          if e["top"] == top and e["ability"] == ability]
    if not xs:
        return
    amts = [a for _, a, _ in xs]
    med = statistics.median(amts)
    mx = max(xs, key=lambda z: z[1])
    print(f"\n  {top} / {ability}: n={len(xs)} median={med:,.0f} "
          f"max={mx[1]:,} at {mx[0]} (x{mx[1]/med:.1f})")
    for t, a, c in sorted(xs, key=lambda z: z[1], reverse=True)[:5]:
        print(f"      {a:>10,} {'crit' if c else '    '} {t}")

print("\n=== named-ability distributions ===")
for top, ab in [("Biffels", "Head Shot"), ("Drizzlen", "Lung Puncture"),
                ("Flepead", "Ceremonial Blade"), ("Biffels", "Assassinate")]:
    dist(top, ab)

# ---- 4. each player's single biggest hit -> do they cluster in time? ----
print("\n=== each player's SINGLE biggest hit to Mayong (sorted by time) ===")
best = {}
for e in players_dmg:
    if e["top"] not in best or e["amt"] > best[e["top"]]["amt"]:
        best[e["top"]] = e
for e in sorted(best.values(), key=lambda e: e["t"]):
    flag = " *ABBREV*" if e["abbrev"] else ""
    print(f"  {e['t']}  {e['amt']:>10,}  {e['top']:<12} {e['ability']}{flag}")
