#!/usr/bin/env python3
"""Per-hit amplification in the spike window vs each ability's fight-long median."""
import re, statistics
from collections import defaultdict
import importlib.util

spec = importlib.util.spec_from_file_location("analyze", "analyze.py")
# reuse parser by re-parsing here (analyze.py prints on import-run, so just inline parse)

OWNER = "Biffels"
def expand(amt):
    amt = amt.replace(",", "")
    m = re.match(r"^([0-9]+(?:\.[0-9]+)?)([KMBTQ]?)$", amt)
    if not m: return None
    return int(round(float(m.group(1)) * {"":1,"K":1e3,"M":1e6,"B":1e9,"T":1e12,"Q":1e15}[m.group(2)]))
LINE = re.compile(r"^\((\d+)\)\[[A-Za-z]{3} [A-Za-z]{3}\s+\d+ (\d\d:\d\d:\d\d) \d{4}\] (.*)$")
HIT = re.compile(r"^(.*?) (?:hits|multi attacks|flurries) (.*?) for (a critical of )?([0-9][0-9,\.]*[KMBTQ]?) ([a-z]+) damage\.$")

events = []
for raw in open("tntmayongweirddmg", encoding="utf-8", errors="replace"):
    lm = LINE.match(raw.rstrip("\n"))
    if not lm: continue
    epoch, t, body = int(lm.group(1)), lm.group(2), lm.group(3)
    body = body.replace("YOUR ", f"{OWNER}'s ").replace("YOU hit", f"{OWNER} hit")
    hm = HIT.match(body)
    if not hm: continue
    left, target, crit, amt_s, dtype = hm.groups()
    amt = expand(amt_s)
    if amt is None or "Mayong" not in target or "Mayong" in left: continue
    top = left.split("'s ")[0].rstrip("'").strip()
    ability = left.split("'s ",1)[1] if "'s " in left else "(auto)"
    events.append(dict(t=t, top=top, ability=ability, amt=amt, crit=bool(crit), dtype=dtype))

# fight-long median per (top, ability)
med = defaultdict(list)
for e in events:
    med[(e["top"], e["ability"])].append(e["amt"])
median = {k: statistics.median(v) for k, v in med.items()}

WIN = {f"21:03:{s:02d}" for s in range(15, 23)}
spikers = ["Biffels", "Drizzlen", "Sprok", "Flepead", "Bardomir"]

for p in spikers:
    rows = [e for e in events if e["top"] == p and e["t"] in WIN]
    if not rows: continue
    print(f"\n=== {p} — every hit to Mayong in 21:03:15-22 (ratio vs fight median) ===")
    for e in sorted(rows, key=lambda e: (e["t"], -e["amt"])):
        m = median[(p, e["ability"])]
        ratio = e["amt"]/m if m else 0
        mark = "  <== ELEVATED" if ratio >= 3 else ""
        print(f"  {e['t']} {e['amt']:>9,} {'crit' if e['crit'] else '   '} "
              f"{e['ability']:<26}{e['dtype']:<9} med={m:>8,.0f} x{ratio:>5.1f}{mark}")
