#!/usr/bin/env python3
"""Test: did getting a spell reflected cause the amp?
A. Recompute amp list with baselines from OUTSIDE-window hits only (fixes
   two-amped-hits-masking-each-other), get full amp set + true window extent.
B. Interleaved timeline: every ep3 reflect + boss-owned-copy action + amp hit,
   in raw line order.
C. Targets of the boss's reflected Aqueous Swarm.
D. Per-player verdict: own-reflect -> amp mapping.
"""
import re
from collections import defaultdict

PLAYERS = ["Arcadia","Bardomir","Buffsy","Butchy","Cheeseot","Cheggers","Cheppy","Drizzlen",
           "Flepead","Keys","Kludian","Korps","Onlyfans","Passionate","Pharout","Shuzuk",
           "Sprok","Stylix","Sugah","Surrillian","Vicious","Zenji","Zill","Biffels"]
PSET = set(PLAYERS)
BOSS = "Vampire Lord Mayong Mistmoore"
LINE = re.compile(r"^\((\d+)\)\[[A-Za-z]{3} [A-Za-z]{3}\s+\d+ (\d\d:\d\d:\d\d) \d{4}\] (.*)$")
HIT  = re.compile(r"^(.*?) (?:hits|multi attacks|flurries) (.+?) for (a critical of )?([0-9][0-9,\.]*[KMBTQ]?) ([a-z]+) damage\.$")

def expand(a):
    a = a.replace(",", "")
    m = re.match(r"^([0-9]+(?:\.[0-9]+)?)([KMBTQ]?)$", a)
    if not m: return None
    return int(round(float(m.group(1)) * {"":1,"K":1e3,"M":1e6,"B":1e9,"T":1e12,"Q":1e15}[m.group(2)]))

def own(b):
    b = re.sub(r"^YOU hit ", "Biffels hits ", b)
    b = re.sub(r"^YOU multi attack ", "Biffels multi attacks ", b)
    b = re.sub(r"^YOU flurry ", "Biffels flurries ", b)
    b = b.replace("YOUR ", "Biffels's ").replace("YOURSELF", "Biffels")
    b = re.sub(r"^YOU try", "Biffels tries", b)
    return re.sub(r"\bYOU\b", "Biffels", b)

def split_attacker(left):
    i1 = left.find("'s "); i2 = left.find("' ")
    if i1 >= 0 and (i2 < 0 or i1 < i2): return left[:i1], left[i1+3:]
    if i2 >= 0: return left[:i2], left[i2+2:]
    return left, "(melee)"

lines = []; events = []
for raw in open("tntmayongweirddmg", encoding="utf-8", errors="replace"):
    m = LINE.match(raw.rstrip("\n"))
    if not m: continue
    t, body = m.group(2), m.group(3)
    no = len(lines); lines.append((no, t, body))
    hm = HIT.match(own(body))
    if hm:
        left, tgt, crit, amt_s, dt = hm.groups()
        amt = expand(amt_s)
        if amt is not None:
            atk, ab = split_attacker(left)
            events.append(dict(no=no, t=t, atk=atk, ab=ab, tgt=tgt, amt=amt, crit=bool(crit), dt=dt))

WLO, WHI = "21:03:10", "21:03:35"   # generous exclusion window

# ---- A. amp scan with outside-window baselines ----
outside = defaultdict(list); inside = defaultdict(list)
for e in events:
    if e["atk"] in PSET and BOSS in e["tgt"]:
        key = (e["atk"], e["ab"], e["dt"], e["crit"])
        (inside if WLO <= e["t"] <= WHI else outside)[key].append(e)

amps = []
for key, evs in inside.items():
    base = sorted((x["amt"] for x in outside.get(key, [])), reverse=True)
    if len(base) < 3: continue
    ceil = base[0]
    for e in evs:
        if e["amt"] >= 2.2 * ceil and e["amt"] >= 4000:
            amps.append((e, ceil))

amps.sort(key=lambda z: z[0]["no"])
print("=" * 80)
print("A. FULL amp list — baseline = max of that ability's hits OUTSIDE 21:03:10-35")
print("=" * 80)
byplayer = defaultdict(list)
for e, ceil in amps:
    c = "crit" if e["crit"] else "NONcrit"
    print(f"  {e['t']}  {e['amt']:>9,}  x{e['amt']/ceil:>4.1f} (out-ceil {ceil:>7,})  "
          f"{e['atk']:<10} {e['ab']:<22} {c}")
    byplayer[e["atk"]].append(e)
print(f"\n  amplified players: {sorted(byplayer)}")
print(f"  window extent: {amps[0][0]['t']} .. {amps[-1][0]['t']}" if amps else "")

# ---- C. boss swarm targets ----
swarm_targets = defaultdict(int)
for e in events:
    if e["atk"] == BOSS and "queous swarm" in e["ab"]:
        swarm_targets[e["tgt"].split()[0]] += 1
# also fails
for no, t, body in lines:
    m = re.match(r"^Vampire Lord Mayong Mistmoore's aqueous swarm(?:'s Frozen Waters)? hits (\w+) but fails", own(body))
    if m: swarm_targets[m.group(1)] += 1

print()
print("=" * 80)
print("C. targets of BOSS's reflected Aqueous Swarm (hits + fails)")
print("=" * 80)
for tgt, n in sorted(swarm_targets.items(), key=lambda kv: -kv[1]):
    mark = "  <== amplified player" if tgt in byplayer else ""
    print(f"  {n:>2}x {tgt}{mark}")

# ---- B. interleaved timeline ----
print()
print("=" * 80)
print("B. INTERLEAVED: reflects (R), boss-copy actions (B), amp hits (A) — raw order")
print("=" * 80)
ampnos = {e["no"] for e, _ in amps}
for no, t, body in lines:
    if not ("21:03:13" <= t <= "21:03:30"): continue
    b = own(body)
    tag = None
    if "reflects." in b: tag = "R"
    elif no in ampnos: tag = "A"
    elif b.startswith(BOSS + "'s") and any(s in b for s in ("aqueous swarm", "Pestilence", "Absorb Magic", "Lifetap")): tag = "B"
    if tag:
        print(f"  {tag}  {t}  {b[:100]}")

# ---- D. per-player reflect->amp verdict ----
print()
print("=" * 80)
print("D. per-player: own spell reflected in ep3 (21:03:13-30) vs amplified")
print("=" * 80)
reflected = defaultdict(list)
for no, t, body in lines:
    if not ("21:03:13" <= t <= "21:03:30"): continue
    m = re.match(r"^(\w+) tries to \w+ .* but .* reflects\.$", own(body))
    if m: reflected[m.group(1)].append(t)
allp = sorted(set(list(reflected) + list(byplayer)))
for p in allp:
    r = ",".join(reflected.get(p, [])) or "-"
    a = ",".join(sorted({e['t'] for e in byplayer.get(p, [])})) or "-"
    verdict = ("BOTH" if p in reflected and p in byplayer
               else "reflected only" if p in reflected else "AMPED, never reflected")
    print(f"  {p:<11} reflected: {r:<28} amped: {a:<28} {verdict}")
