#!/usr/bin/env python3
"""Next-round probes:
1. Add (Libant) death/spawn timeline vs reflect episodes + amp window.
2. Ceremonial Blade portion-pair analysis -> exact per-cast amp multiplier for both bards.
3. Same trick for Biffels' Impale double-hit at 21:03:16.
4. Were hits on ADDS amped in the window too? (out-of-window baselines, add targets)
5. 'frightening alacrity' proc timeline vs Biffels' amps.
"""
import re
from collections import defaultdict

PLAYERS = {"Arcadia","Bardomir","Buffsy","Butchy","Cheeseot","Cheggers","Cheppy","Drizzlen",
           "Flepead","Keys","Kludian","Korps","Onlyfans","Passionate","Pharout","Shuzuk",
           "Sprok","Stylix","Sugah","Surrillian","Vicious","Zenji","Zill","Biffels"}
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
    b = re.sub(r"^YOU have killed", "Biffels has killed", b)
    return b.replace("YOUR ", "Biffels's ").replace("YOURSELF", "Biffels")

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
    lines.append((t, body))
    hm = HIT.match(own(body))
    if hm:
        left, tgt, crit, amt_s, dt = hm.groups()
        amt = expand(amt_s)
        if amt is not None:
            atk, ab = split_attacker(left)
            events.append(dict(t=t, atk=atk, ab=ab, tgt=tgt, amt=amt, crit=bool(crit), dt=dt))

# ---------- 1. add lifecycle ----------
print("=" * 78)
print("1. LIBANT ADD deaths / mechanic-says, whole fight (episodes: E1 20:57:11-28,")
print("   E2 20:58:48-59:07, E3 21:03:15-29; AMP 21:03:16-25)")
print("=" * 78)
for t, b in lines:
    bb = own(b)
    if re.search(r"has killed a Libant|Libant.*says|a Libant (infiltrator|interloper) (dies|has been slain)", bb):
        ep = ""
        if "20:57:11" <= t <= "20:57:28": ep = "  [E1]"
        elif "20:58:48" <= t <= "20:59:07": ep = "  [E2]"
        elif "21:03:15" <= t <= "21:03:29": ep = "  [E3+AMP]"
        print(f"  {t}  {bb[:95]}{ep}")

# ---------- 2/3. portion-pair analysis ----------
def pairs_for(player, ability):
    """same-second value pairs for a two-portion ability"""
    per_sec = defaultdict(list)
    for e in events:
        if e["atk"] == player and e["ab"] == ability and BOSS in e["tgt"]:
            per_sec[e["t"]].append((e["amt"], e["crit"]))
    return {t: sorted(v, reverse=True) for t, v in per_sec.items() if len(v) == 2}

print()
print("=" * 78)
print("2. CEREMONIAL BLADE portion pairs (p1/p2 same second) -> exact amp multiplier")
print("=" * 78)
for player in ("Flepead", "Bardomir"):
    pp = pairs_for(player, "Ceremonial Blade")
    ratios = []
    print(f"\n  {player}:")
    for t in sorted(pp):
        (a1, c1), (a2, c2) = pp[t]
        r = a1 / a2 if a2 else 0
        amp = "  <== AMPED CAST" if a1 > 15000 else ""
        print(f"    {t}  p1={a1:>7,}{'c' if c1 else ' '}  p2={a2:>6,}{'c' if c2 else ' '}  ratio={r:5.2f}{amp}")
        if a1 <= 15000: ratios.append(r)
    if ratios:
        mean_r = sum(ratios) / len(ratios)
        print(f"    normal ratio: mean={mean_r:.3f} (n={len(ratios)}, "
              f"min={min(ratios):.2f}, max={max(ratios):.2f})")
        for t in sorted(pp):
            (a1, c1), (a2, c2) = pp[t]
            if a1 > 15000:
                expected_p1 = mean_r * a2
                print(f"    -> amped cast {t}: expected p1 = {expected_p1:,.0f}, actual = {a1:,} "
                      f"=> TRUE multiplier = {a1/expected_p1:.2f}x")

print()
print("=" * 78)
print("3. BIFFELS IMPALE pairs (was the 4,510 at 21:03:16 amped?)")
print("=" * 78)
pp = pairs_for("Biffels", "Impale")
ratios = []
for t in sorted(pp):
    (a1, c1), (a2, c2) = pp[t]
    r = a1 / a2 if a2 else 0
    mark = "  <== window" if "21:03:10" <= t <= "21:03:35" else ""
    print(f"    {t}  p1={a1:>7,}{'c' if c1 else ' '}  p2={a2:>6,}{'c' if c2 else ' '}  ratio={r:5.2f}{mark}")

# ---------- 4. hits on adds: amped? ----------
print()
print("=" * 78)
print("4. hits on LIBANT ADDS — window hits vs outside-baseline ceilings")
print("=" * 78)
out = defaultdict(list); ins = defaultdict(list)
for e in events:
    if e["atk"] in PLAYERS and "Libant" in e["tgt"]:
        key = (e["atk"], e["ab"], e["dt"], e["crit"])
        ("21:03:10" <= e["t"] <= "21:03:35" and ins or out)[key].append(e)
flagged = 0
for key, evs in ins.items():
    base = sorted((x["amt"] for x in out.get(key, [])), reverse=True)
    if len(base) < 2: continue
    for e in evs:
        r = e["amt"] / base[0]
        if r >= 2.0 and e["amt"] >= 2000:
            flagged += 1
            print(f"  {e['t']}  {e['amt']:>8,}  x{r:.1f}  {e['atk']} / {e['ab']}")
print(f"  -> {flagged} amped hits on adds" + (" (NONE — amp was boss-target only?)" if not flagged else ""))
n_add_hits = sum(len(v) for v in ins.values())
print(f"  (window hits on adds with usable baselines: {n_add_hits})")

# ---------- 5. frightening alacrity ----------
print()
print("=" * 78)
print("5. 'frightening alacrity' timeline (Biffels proc?) vs his amps 21:03:17/19/21")
print("=" * 78)
for t, b in lines:
    if "frightening alacrity" in b:
        print(f"  {t}  {b[:80]}")
