#!/usr/bin/env python3
"""Cross-fight intersection: templates present in BOTH amp windows, rare in both fights.
Rosters are built dynamically per fight (attacker/healer names), so P-normalization
works across different raid compositions."""
import re
from collections import defaultdict

LINE = re.compile(r"^\((\d+)\)\[[A-Za-z]{3} [A-Za-z]{3}\s+\d+ (\d\d:\d\d:\d\d) \d{4}\] (.*)$")
HIT  = re.compile(r"^(.*?) (?:hits|multi attacks|flurries) (.+?) for ")
HEAL = re.compile(r"^(.*?) heals ")

FIGHTS = [
    ("tntmayongweirddmg", "Vampire Lord Mayong Mistmoore", ("21:03:12", "21:03:26")),
    ("menaceweirddmg",    "Clockwork Menace",              ("19:48:27", "19:48:40")),
]

def own(b):
    b = re.sub(r"^YOU (hit|multi attack|flurry) ", "Biffels hits ", b)
    b = re.sub(r"^YOU ", "Biffels ", b)
    return b.replace("YOUR ", "Biffels's ").replace("YOURSELF", "Biffels")

def toplevel(left):
    i1 = left.find("'s "); i2 = left.find("' ")
    if i1 >= 0 and (i2 < 0 or i1 < i2): return left[:i1]
    if i2 >= 0: return left[:i2]
    return left

results = []
for fname, boss, (wlo, whi) in FIGHTS:
    rows = []
    names = set(["Biffels"])
    for raw in open(fname, encoding="utf-8", errors="replace"):
        m = LINE.match(raw.rstrip("\n"))
        if not m: continue
        t, b = m.group(2), own(m.group(3))
        rows.append((t, b))
        for rx in (HIT, HEAL):
            hm = rx.match(b)
            if hm:
                n = toplevel(hm.group(1))
                if re.fullmatch(r"[A-Z][a-z]+", n) and boss not in n:
                    names.add(n)
    namere = re.compile(r"\b(" + "|".join(sorted(names, key=len, reverse=True)) + r")\b")
    def tpl(b):
        s = b.replace(boss, "BOSS")
        s = re.sub(r"\b[aA] (Libant (infiltrator|interloper)|clockwork \w+( \w+)?)", "ADD", s)
        s = namere.sub("P", s)
        s = re.sub(r"\b(You|Your)\b", "P", s)
        return re.sub(r"[0-9][0-9,\.]*[KMBTQ]?", "N", s)
    tot = defaultdict(int); win = defaultdict(int)
    for t, b in rows:
        k = tpl(b)
        tot[k] += 1
        if wlo <= t <= whi: win[k] += 1
    results.append((fname, tot, win, len(rows)))

(f1, tot1, win1, n1), (f2, tot2, win2, n2) = results
common = set(win1) & set(win2)
print(f"templates in Mayong window: {len(win1)}; in Menace window: {len(win2)}; common: {len(common)}\n")
print("=== common templates, ranked by combined rarity (window share in both fights) ===")
scored = []
for k in common:
    s1 = win1[k] / tot1[k]; s2 = win2[k] / tot2[k]
    scored.append((min(s1, s2), s1, s2, tot1[k], tot2[k], k))
scored.sort(reverse=True)
for mn, s1, s2, t1_, t2_, k in scored[:35]:
    print(f"  mayong {win1[k]}/{t1_:>4} ({s1:4.0%})  menace {win2[k]}/{t2_:>4} ({s2:4.0%})   {k[:85]}")
