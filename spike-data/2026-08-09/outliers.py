#!/usr/bin/env python3
"""Genuine ceiling-breakers: hits that far exceed that ability's OWN 2nd-best (kills the
bimodal-median artifact). These are the only truly anomalous hits in the fight."""
import re
from collections import defaultdict

OWNER="Biffels"
def expand(a):
    a=a.replace(",","")
    m=re.match(r"^([0-9]+(?:\.[0-9]+)?)([KMBTQ]?)$",a)
    return int(round(float(m.group(1))*{"":1,"K":1e3,"M":1e6,"B":1e9,"T":1e12,"Q":1e15}[m.group(2)])) if m else None
LINE=re.compile(r"^\((\d+)\)\[[A-Za-z]{3} [A-Za-z]{3}\s+\d+ (\d\d:\d\d:\d\d) \d{4}\] (.*)$")
HIT=re.compile(r"^(.*?) (?:hits|multi attacks|flurries) (.*?) for (a critical of )?([0-9][0-9,\.]*[KMBTQ]?) ([a-z]+) damage\.$")

ev=[]
for raw in open("tntmayongweirddmg",encoding="utf-8",errors="replace"):
    lm=LINE.match(raw.rstrip("\n"))
    if not lm: continue
    t,body=lm.group(2),lm.group(3).replace("YOUR ",f"{OWNER}'s ").replace("YOU hit",f"{OWNER} hit")
    hm=HIT.match(body)
    if not hm: continue
    left,target,crit,amt_s,dtype=hm.groups()
    amt=expand(amt_s)
    if amt is None or "Mayong" not in target or "Mayong" in left: continue
    top=left.split("'s ")[0].rstrip("'").strip()
    ab=left.split("'s ",1)[1] if "'s " in left else "(auto)"
    ev.append(dict(t=t,top=top,ab=ab,amt=amt,crit=bool(crit)))

by=defaultdict(list)
for e in ev: by[(e["top"],e["ab"])].append(e)

outliers=[]
for (top,ab),lst in by.items():
    if len(lst)<3: continue
    s=sorted(lst,key=lambda e:-e["amt"])
    top_hit=s[0]; ceiling=s[1]["amt"]  # 2nd-best = the ability's normal max
    if ceiling>0 and top_hit["amt"]>=2.5*ceiling and top_hit["amt"]>=15000:
        outliers.append((top_hit,ceiling))

print("=== genuine ceiling-breakers (top hit >= 2.5x the ability's 2nd-best, >=15k) ===\n")
for e,ceil in sorted(outliers,key=lambda z:-z[0]["amt"]):
    print(f"  {e['t']}  {e['amt']:>9,}  ({e['amt']/ceil:>4.1f}x its own 2nd-best {ceil:,})  "
          f"{e['top']} / {e['ab']}")
