#!/usr/bin/env python3
"""Generic fight scan: find every amped hit on <boss> in <logfile>.
Each hit compared against the MAX OF ALL OTHER hits in its (attacker, ability,
dtype, crit) population (robust to a single amp; twin amps compared to each other).
Usage: fight_scan.py <logfile> <boss substring> [owner]"""
import re, sys
from collections import defaultdict

LOG, BOSSPAT = sys.argv[1], sys.argv[2]
OWNER = sys.argv[3] if len(sys.argv) > 3 else "Biffels"
LINE = re.compile(r"^\((\d+)\)\[[A-Za-z]{3} [A-Za-z]{3}\s+\d+ (\d\d:\d\d:\d\d) \d{4}\] (.*)$")
HIT  = re.compile(r"^(.*?) (?:hits|multi attacks|flurries) (.+?) for (a critical of )?([0-9][0-9,\.]*[KMBTQ]?) ([a-z]+) damage\.$")

def expand(a):
    a = a.replace(",", "")
    m = re.match(r"^([0-9]+(?:\.[0-9]+)?)([KMBTQ]?)$", a)
    if not m: return None
    return int(round(float(m.group(1)) * {"":1,"K":1e3,"M":1e6,"B":1e9,"T":1e12,"Q":1e15}[m.group(2)]))

def own(b):
    b = re.sub(r"^YOU hit ", f"{OWNER} hits ", b)
    b = re.sub(r"^YOU multi attack ", f"{OWNER} multi attacks ", b)
    b = re.sub(r"^YOU flurry ", f"{OWNER} flurries ", b)
    return b.replace("YOUR ", f"{OWNER}'s ").replace("YOURSELF", OWNER)

def split_attacker(left):
    i1 = left.find("'s "); i2 = left.find("' ")
    if i1 >= 0 and (i2 < 0 or i1 < i2): return left[:i1], left[i1+3:]
    if i2 >= 0: return left[:i2], left[i2+2:]
    return left, "(melee)"

pop = defaultdict(list)
reflects = []
first_t = last_t = None
for raw in open(LOG, encoding="utf-8", errors="replace"):
    m = LINE.match(raw.rstrip("\n"))
    if not m: continue
    t, body = m.group(2), m.group(3)
    first_t = first_t or t; last_t = t
    b = own(body)
    if "reflects." in b: reflects.append(t)
    hm = HIT.match(b)
    if not hm: continue
    left, tgt, crit, amt_s, dt = hm.groups()
    if BOSSPAT not in tgt or BOSSPAT in left: continue
    amt = expand(amt_s)
    if amt is None: continue
    atk, ab = split_attacker(left)
    pop[(atk, ab, dt, bool(crit))].append((t, amt))

print(f"fight {first_t}..{last_t}; {sum(len(v) for v in pop.values())} hits on '{BOSSPAT}'; "
      f"{len(reflects)} reflect lines")

amps = []
for key, evs in pop.items():
    if len(evs) < 4: continue
    amts = [a for _, a in evs]
    mx_all = sorted(amts, reverse=True)
    for t, a in evs:
        others_max = mx_all[1] if a == mx_all[0] else mx_all[0]
        if others_max > 0 and a >= 2.2 * others_max and a >= 3000:
            amps.append((t, a, a / others_max, key))

amps.sort()
print(f"\n=== amped hits ({len(amps)}) ===")
for t, a, r, (atk, ab, dt, crit) in amps:
    print(f"  {t}  {a:>9,}  x{r:>5.1f}  {atk:<11} {ab:<24} {dt:<9} {'crit' if crit else 'NONcrit'}")

# reflect density per 30s vs amp times
print("\n=== reflect density per 30s bucket (amps marked) ===")
from collections import Counter
def bucket(t):
    h, m, s = map(int, t.split(":"))
    sec = h*3600 + m*60 + s
    return sec - sec % 30
rc = Counter(bucket(t) for t in reflects)
ampb = Counter(bucket(t) for t, *_ in amps)
for bkt in sorted(set(list(rc) + list(ampb))):
    hh, rem = divmod(bkt, 3600); mm, ss = divmod(rem, 60)
    bar = "#" * min(rc.get(bkt, 0), 60)
    am = f"   <== {ampb[bkt]} AMP" if bkt in ampb else ""
    print(f"  {hh:02d}:{mm:02d}:{ss:02d}  {rc.get(bkt,0):>3} {bar}{am}")
