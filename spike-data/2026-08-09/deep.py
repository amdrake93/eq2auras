#!/usr/bin/env python3
"""Deep pass, corrected methodology:
1. Fight-wide anomaly scan with populations keyed (attacker, ability, dtype, CRIT) —
   fixes the crit-mixing bug that hid Quick Strike 15.7k.
2. Same scan for boss/add outgoing hits (encounter-wide vs player-side test).
3. Rare-template enrichment: EVERY line normalized (names->P, boss->BOSS, numbers->N),
   ranked by exclusivity to the spike window. Systematic, no cherry-picking.
4. Raw adjacency dump around each anomalous hit (sub-second event order).
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
NAMES_RE = re.compile(r"\b(" + "|".join(PLAYERS) + r")\b")

def expand(a):
    a = a.replace(",", "")
    m = re.match(r"^([0-9]+(?:\.[0-9]+)?)([KMBTQ]?)$", a)
    if not m: return None
    return int(round(float(m.group(1)) * {"":1,"K":1e3,"M":1e6,"B":1e9,"T":1e12,"Q":1e15}[m.group(2)]))

def own(body):
    body = re.sub(r"^YOU hit ", "Biffels hits ", body)
    body = re.sub(r"^YOU multi attack ", "Biffels multi attacks ", body)
    body = re.sub(r"^YOU flurry ", "Biffels flurries ", body)
    body = body.replace("YOUR ", "Biffels's ").replace("YOURSELF", "Biffels")
    body = re.sub(r"\bYOU\b", "Biffels", body)
    return body

def split_attacker(left):
    i1 = left.find("'s "); i2 = left.find("' ")
    if i1 >= 0 and (i2 < 0 or i1 < i2): return left[:i1], left[i1+3:]
    if i2 >= 0: return left[:i2], left[i2+2:]
    return left, "(melee)"

def template(body):
    s = own(body)
    s = s.replace(BOSS, "BOSS")
    s = re.sub(r"\b[aA] Libant (infiltrator|interloper)", "ADD", s)
    s = NAMES_RE.sub("P", s)
    s = re.sub(r"\b(You|Your|you|your)\b", "P", s)
    s = re.sub(r"[0-9][0-9,\.]*[KMBTQ]?", "N", s)
    return s

lines = []; events = []
for raw in open("tntmayongweirddmg", encoding="utf-8", errors="replace"):
    m = LINE.match(raw.rstrip("\n"))
    if not m: continue
    t, body = m.group(2), m.group(3)
    no = len(lines); lines.append((t, body))
    hm = HIT.match(own(body))
    if hm:
        left, tgt, crit, amt_s, dt = hm.groups()
        amt = expand(amt_s)
        if amt is not None:
            atk, ab = split_attacker(left)
            events.append(dict(no=no, t=t, atk=atk, ab=ab, tgt=tgt, amt=amt,
                               crit=bool(crit), dt=dt))

def in_win(t, lo, hi): return lo <= t <= hi
W_LO, W_HI = "21:03:13", "21:03:22"        # enrichment window (precursors + spike)
A_LO, A_HI = "21:03:16", "21:03:21"        # spike window proper

# ---------- 1. player -> boss anomaly scan, crit+dtype matched ----------
pop = defaultdict(list)
for e in events:
    if e["atk"] in PSET and BOSS in e["tgt"]:
        pop[(e["atk"], e["ab"], e["dt"], e["crit"])].append(e)

anoms, lowconf = [], []
for key, evs in pop.items():
    s = sorted(evs, key=lambda e: -e["amt"])
    if len(s) >= 3:
        a1, ceil = s[0], s[1]["amt"]
        if ceil > 0 and a1["amt"] >= 2.5 * ceil and a1["amt"] >= 8000:
            anoms.append((a1, ceil, len(s), [x["amt"] for x in s[1:4]]))
    elif len(s) == 2:
        a1, ceil = s[0], s[1]["amt"]
        if ceil > 0 and a1["amt"] >= 4 * ceil and a1["amt"] >= 15000:
            lowconf.append((a1, ceil, len(s)))

print("=" * 78)
print("1. PLAYER->BOSS ceiling-breakers, populations matched on (ability,dtype,CRIT)")
print("=" * 78)
inside = outside = 0
for a1, ceil, n, rest in sorted(anoms, key=lambda z: z[0]["t"]):
    w = in_win(a1["t"], A_LO, A_HI)
    inside += w; outside += (not w)
    c = "crit" if a1["crit"] else "NONcrit"
    star = "  <== IN WINDOW" if w else "  ***OUTSIDE***"
    print(f"  {a1['t']}  {a1['amt']:>9,}  x{a1['amt']/ceil:>4.1f}  {a1['atk']:<10} {a1['ab']:<20} "
          f"{a1['dt']:<9}{c:<8} pop n={n:<3} next-best={rest}{star}")
print(f"\n  -> {inside} inside {A_LO}-{A_HI}, {outside} elsewhere in the whole fight")
if lowconf:
    print("\n  low-confidence (population of only 2):")
    for a1, ceil, n in sorted(lowconf, key=lambda z: z[0]["t"]):
        w = "IN WINDOW" if in_win(a1["t"], A_LO, A_HI) else "outside"
        print(f"    {a1['t']}  {a1['amt']:>9,}  x{a1['amt']/ceil:.1f}  {a1['atk']} / {a1['ab']} ({w})")

# ---------- 2. boss/add -> player scan ----------
bpop = defaultdict(list)
for e in events:
    if (e["atk"] == BOSS or e["atk"].startswith("a Libant")) :
        tgt = e["tgt"].split()[0].rstrip("'s")
        bpop[(e["atk"], e["ab"], e["dt"], e["crit"])].append(e)

print()
print("=" * 78)
print("2. BOSS/ADD outgoing ceiling-breakers (same test) — encounter-wide check")
print("=" * 78)
found = 0
for key, evs in bpop.items():
    s = sorted(evs, key=lambda e: -e["amt"])
    if len(s) < 3: continue
    a1, ceil = s[0], s[1]["amt"]
    if ceil > 0 and a1["amt"] >= 2.5 * ceil and a1["amt"] >= 8000:
        found += 1
        print(f"  {a1['t']}  {a1['amt']:>9,}  x{a1['amt']/ceil:.1f}  {a1['atk']} / {a1['ab']} -> {a1['tgt']}")
print(f"  -> {found} boss-side anomalies found" + (" (NONE — amplification was player-side only)" if not found else ""))

# ---------- 3. rare-template enrichment ----------
tot = defaultdict(int); win = defaultdict(int)
wl = sum(1 for t, b in lines if in_win(t, W_LO, W_HI))
for t, b in lines:
    tpl = template(b)
    tot[tpl] += 1
    if in_win(t, W_LO, W_HI): win[tpl] += 1

print()
print("=" * 78)
print(f"3. RARE-TEMPLATE ENRICHMENT — window {W_LO}-{W_HI} has {wl}/{len(lines)} lines "
      f"({100*wl/len(lines):.1f}%); templates whose occurrences concentrate there:")
print("=" * 78)
gold = sorted([tpl for tpl in win if tot[tpl] >= 2 and win[tpl] == tot[tpl]],
              key=lambda tpl: -tot[tpl])
print(f"\n  A. templates occurring ONLY in the window (n>=2): {len(gold)}")
for tpl in gold:
    print(f"     [{tot[tpl]}x] {tpl[:100]}")
strong = sorted([tpl for tpl in win if tot[tpl] >= 4 and win[tpl]/tot[tpl] >= 0.5 and win[tpl] != tot[tpl]],
                key=lambda tpl: -(win[tpl]/tot[tpl]))
print(f"\n  B. templates n>=4 with >=50% of ALL occurrences in this 10s window: {len(strong)}")
for tpl in strong:
    print(f"     [{win[tpl]}/{tot[tpl]}] {tpl[:100]}")
singles = sorted(tpl for tpl in win if tot[tpl] == 1)
print(f"\n  C. one-off templates that happen to fall in the window: {len(singles)}")
for tpl in singles[:30]:
    print(f"     {tpl[:100]}")

# ---------- 4. adjacency around each anomaly ----------
print()
print("=" * 78)
print("4. RAW ADJACENCY (sub-second order) around each in-window anomaly")
print("=" * 78)
for a1, ceil, n, rest in sorted(anoms, key=lambda z: z[0]["no"]):
    if not in_win(a1["t"], A_LO, A_HI): continue
    print(f"\n  --- {a1['atk']} / {a1['ab']} {a1['amt']:,} ---")
    for i in range(max(0, a1["no"]-4), min(len(lines), a1["no"]+3)):
        mark = ">>>" if i == a1["no"] else "   "
        print(f"  {mark} {lines[i][0]} {lines[i][1][:105]}")
