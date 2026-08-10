#!/usr/bin/env python3
"""Amp-burst detector for arbitrary raw EQ2 logs (single fight or whole-day files).

- Parses every damage hit (any attacker -> any target).
- Populations keyed (attacker, ability, dtype, crit, target); a hit is FLAGGED if it
  is >= 2.2x the max of all OTHER hits in its population (pop n>=5, amt>=4000).
- Flags are clustered by epoch (<=20s gaps); clusters with >=3 flags from >=2
  attackers are reported as BURSTS. Smaller flag groups listed as strays.
- Around each burst, prints all reflect lines +-45s (mark-reflects highlighted).

Usage: burst_detect.py <logfile> [owner=Biffels]
"""
import re, sys
from collections import defaultdict

LOG = sys.argv[1]
OWNER = sys.argv[2] if len(sys.argv) > 2 else "Biffels"
LINE = re.compile(r"^\((\d+)\)\[[A-Za-z]{3} ([A-Za-z]{3}\s+\d+) (\d\d:\d\d:\d\d) \d{4}\] (.*)$")
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
    b = re.sub(r"^YOU try", f"{OWNER} tries", b)
    return b.replace("YOUR ", f"{OWNER}'s ").replace("YOURSELF", OWNER)

def split_attacker(left):
    i1 = left.find("'s "); i2 = left.find("' ")
    if i1 >= 0 and (i2 < 0 or i1 < i2): return left[:i1], left[i1+3:]
    if i2 >= 0: return left[:i2], left[i2+2:]
    return left, "(melee)"

events = []; reflects = []
for raw in open(LOG, encoding="utf-8", errors="replace"):
    m = LINE.match(raw.rstrip("\n"))
    if not m: continue
    ep, day, t, body = int(m.group(1)), m.group(2), m.group(3), m.group(4)
    b = own(body)
    if "reflects." in b:
        reflects.append((ep, day, t, b))
        continue
    hm = HIT.match(b)
    if not hm: continue
    left, tgt, crit, amt_s, dt = hm.groups()
    amt = expand(amt_s)
    if amt is None: continue
    atk, ab = split_attacker(left)
    events.append(dict(ep=ep, day=day, t=t, atk=atk, ab=ab, tgt=tgt, amt=amt,
                       crit=bool(crit), dt=dt))

pop = defaultdict(list)
for e in events:
    pop[(e["atk"], e["ab"], e["dt"], e["crit"], e["tgt"])].append(e)

flags = []
for key, evs in pop.items():
    if len(evs) < 5: continue
    amts = sorted((x["amt"] for x in evs), reverse=True)
    for e in evs:
        others_max = amts[1] if e["amt"] == amts[0] else amts[0]
        if others_max > 0 and e["amt"] >= 2.2 * others_max and e["amt"] >= 4000:
            flags.append((e, e["amt"] / others_max))

flags.sort(key=lambda z: z[0]["ep"])
clusters = []
for f in flags:
    if clusters and f[0]["ep"] - clusters[-1][-1][0]["ep"] <= 20:
        clusters[-1].append(f)
    else:
        clusters.append([f])

print(f"{LOG}: {len(events)} hits parsed, {len(flags)} flagged, {len(reflects)} reflect lines")
bursts = [c for c in clusters if len(c) >= 3 and len({f[0]['atk'] for f in c}) >= 2]
strays = [c for c in clusters if c not in bursts]

for c in bursts:
    lo, hi = c[0][0], c[-1][0]
    print(f"\n{'='*74}\nBURST  {lo['day']} {lo['t']} .. {hi['t']}  "
          f"({len(c)} amped hits, {len({f[0]['atk'] for f in c})} attackers, target {lo['tgt']})\n{'='*74}")
    for e, r in c:
        print(f"  {e['t']}  {e['amt']:>9,}  x{r:>5.1f}  {e['atk']:<12} {e['ab']:<24} "
              f"{'crit' if e['crit'] else 'NONcrit'}")
    print(f"  --- reflects within +-45s ---")
    for ep, day, t, b in reflects:
        if lo["ep"] - 45 <= ep <= hi["ep"] + 45:
            mark = "  <== MARK" if re.search(r"Death Mark|Mark of \w+", b) else ""
            sp = re.search(r"tries to \w+ .* with (.*?), but", b)
            who = b.split(" tries")[0]
            print(f"    {t}  {who}: {sp.group(1) if sp else '?'}{mark}")

if strays:
    print(f"\n--- stray flags ({sum(len(c) for c in strays)}) ---")
    for c in strays:
        for e, r in c:
            print(f"  {e['day']} {e['t']}  {e['amt']:>9,}  x{r:.1f}  {e['atk']} / {e['ab']} -> {e['tgt']}")
