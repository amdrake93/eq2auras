#!/usr/bin/env python3
"""Archive-scale amp-burst sweep. Streams arbitrary raw EQ2 logs (GB-safe),
finds amp bursts with DAY-LOCAL population baselines (gear-drift safe),
and reports mark-reflect context. Optional cohort mode builds a per-pull
table for a named boss (e.g. 'Vampire Lord Mayong Mistmoore').

Usage:
  sweep.py <logfile> [<logfile> ...] [--owner Biffels] [--cohort "Boss Name"]

Population = (day, attacker, ability, dtype, crit, target); a hit is FLAGGED if
>= 2.2x the max of all OTHER same-population hits, >= 4000 dmg, pop n >= 5.
Flags cluster (<=20s gaps); clusters with >=3 flags from >=2 attackers = BURST.
"""
import re, sys
from collections import defaultdict

args = [a for a in sys.argv[1:]]
OWNER, COHORT = "Biffels", None
files = []
i = 0
while i < len(args):
    if args[i] == "--owner": OWNER = args[i+1]; i += 2
    elif args[i] == "--cohort": COHORT = args[i+1]; i += 2
    else: files.append(args[i]); i += 1

LINE = re.compile(r"^\((\d+)\)\[[A-Za-z]{3} ([A-Za-z]{3}\s+\d+) (\d\d:\d\d:\d\d) (\d{4})\] (.*)$")
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

for path in files:
    # pass 1: population amounts (ints only) + candidates + reflects + cohort activity
    pop_amts = defaultdict(list)
    candidates = []          # full metadata only for hits >= 4000
    reflects = []            # (epoch, daykey, t, caster, spell)
    cohort_hits = []         # epochs of damage involving COHORT
    kills = []               # (epoch, line) killing blows on COHORT
    nlines = 0
    for raw in open(path, encoding="utf-8", errors="replace"):
        m = LINE.match(raw.rstrip("\n"))
        if not m: continue
        nlines += 1
        ep, day, t, yr, body = int(m.group(1)), m.group(2), m.group(3), m.group(4), m.group(5)
        daykey = f"{yr}-{day}"
        b = own(body)
        if "reflects." in b:
            rm = re.match(r"^(.*?) tries to \w+ .* with (.*?), but", b)
            if rm: reflects.append((ep, daykey, t, rm.group(1), rm.group(2)))
            continue
        if "ark of ivinity"[0:0] or re.search(r"[Mm]ark of [Dd]ivinity", b):
            with open(path + ".mod.tsv", "a") as mf:
                mf.write(f"{ep}\t{daykey}\t{t}\t{b}\n")
        if COHORT and COHORT in b:
            if " hits " in b or " multi attacks " in b or " flurries " in b:
                cohort_hits.append(ep)
            if re.search(rf"has killed {re.escape(COHORT)}", b):
                kills.append((ep, t))
        hm = HIT.match(b)
        if not hm: continue
        left, tgt, crit, amt_s, dt = hm.groups()
        amt = expand(amt_s)
        if amt is None: continue
        atk, ab = split_attacker(left)
        key = (daykey, atk, ab, dt, bool(crit), tgt)
        pop_amts[key].append(amt)
        if amt >= 4000:
            candidates.append((ep, daykey, t, key, amt))

    # flag
    flags = []
    for ep, daykey, t, key, amt in candidates:
        amts = pop_amts[key]
        if len(amts) < 5: continue
        mx = max(amts)
        others_max = sorted(amts)[-2] if amt == mx else mx
        if others_max > 0 and amt >= 2.2 * others_max and amt >= 4000:
            flags.append((ep, daykey, t, key, amt, amt / others_max))
    flags.sort()
    with open(path + ".flags.tsv", "w") as ff:
        for ep, daykey, t, key, amt, r in flags:
            ff.write(f"{ep}\t{daykey}\t{t}\t{key[1]}\t{key[2]}\t{amt}\t{r:.1f}\t{key[5]}\n")

    clusters = []
    for f in flags:
        if clusters and f[0] - clusters[-1][-1][0] <= 20: clusters[-1].append(f)
        else: clusters.append([f])

    def is_burst(c):
        # strong core: >=3 flags within any 12s span, >=2 attackers, big + amplified
        if len(c) < 3: return False
        if max(a for *_, a, r in c) < 20000: return False
        if max(r for *_, r in c) < 4.0: return False
        eps = [f[0] for f in c]
        for i in range(len(eps)):
            j = i
            while j + 1 < len(eps) and eps[j + 1] - eps[i] <= 12: j += 1
            if j - i + 1 >= 3 and len({c[k][3][1] for k in range(i, j + 1)}) >= 2:
                return True
        return False

    bursts = [c for c in clusters if is_burst(c)]

    weak = [c for c in clusters if c not in bursts and len(c) >= 3]
    print(f"\n{'#'*76}\n# {path}: {nlines} lines, {len(flags)} flags, "
          f"{len(bursts)} BURSTS, {len(weak)} weak clusters, {len(reflects)} reflects\n{'#'*76}")
    for c in weak:
        mx = max(c, key=lambda f: f[4])
        print(f"  weak: {c[0][1]} {c[0][2]}..{c[-1][2]}  {len(c)} flags  "
              f"max {mx[4]:,} x{mx[5]:.1f}  tgt {mx[3][5][:40]}")
    for c in bursts:
        lo, hi = c[0], c[-1]
        print(f"\nBURST {lo[1]} {lo[2]}..{hi[2]}  ({len(c)} hits, "
              f"{len({f[3][1] for f in c})} attackers, tgt {lo[3][5]})")
        for ep, daykey, t, key, amt, r in c:
            print(f"  {t}  {amt:>9,}  x{r:>5.1f}  {key[1]:<12} {key[2]:<24} "
                  f"{'crit' if key[4] else 'NONcrit'}")
        print("  reflects +-45s:")
        for ep, dk, t, caster, spell in reflects:
            if lo[0] - 45 <= ep <= hi[0] + 45:
                mk = "  <== MARK" if re.search(r"Death Mark|Mark of \w+", spell) else ""
                print(f"    {t}  {caster}: {spell}{mk}")

    # cohort pull table
    if COHORT and cohort_hits:
        cohort_hits.sort()
        pulls = []
        for ep in cohort_hits:
            if pulls and ep - pulls[-1][-1] <= 180: pulls[-1].append(ep)
            else: pulls.append([ep])
        print(f"\n--- COHORT '{COHORT}': {len(pulls)} pulls ---")
        burst_spans = [(c[0][0], c[-1][0]) for c in bursts]
        for p in pulls:
            lo, hi = p[0], p[-1]
            dur = hi - lo
            killed = any(lo <= ke <= hi + 5 for ke, _ in kills)
            dm  = [t for ep, dk, t, cst, sp in reflects if lo <= ep <= hi and sp == "Death Mark"]
            mod = [t for ep, dk, t, cst, sp in reflects if lo <= ep <= hi and "Mark of" in sp]
            close = any(abs(e1 - e2) <= 8
                        for e1 in [ep for ep, dk, t, c_, sp in reflects if lo <= ep <= hi and sp == "Death Mark"]
                        for e2 in [ep for ep, dk, t, c_, sp in reflects if lo <= ep <= hi and "Mark of" in sp])
            burst = any(lo <= b0 and b1 <= hi for b0, b1 in burst_spans)
            import time
            stamp = time.strftime("%Y-%m-%d %H:%M", time.localtime(lo))
            print(f"  {stamp}  dur {dur//60}m{dur%60:02d}s  kill={'Y' if killed else 'n'}  "
                  f"DMrefl={len(dm)} MoDrefl={len(mod)} overlap<=8s={'YES' if close else 'no'}  "
                  f"BURST={'YES' if burst else 'no'}")
