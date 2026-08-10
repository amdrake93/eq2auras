#!/usr/bin/env python3
"""Prove or disprove the bimodal-artifact claim for the abilities I retracted.
For each: full sorted value profile + how many 'big' hits + top-vs-2nd ratio."""
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
    ev.append(dict(t=t,top=top,ab=ab,amt=amt))

by=defaultdict(list)
for e in ev: by[(e["top"],e["ab"])].append(e["amt"])

# suspects = recurring "amplified" abilities from the median detector
suspects=[("Biffels","Quick Strike"),("Sprok","Fiery Annihilation"),("Zenji","Shadow Coil"),
          ("Surrillian","Backstab"),("Biffels","Impale"),("Kludian","Lifeburn"),
          # and the 2 TRUE anomalies for contrast:
          ("Biffels","Head Shot"),("Drizzlen","Lung Puncture")]

for key in suspects:
    v=sorted(by.get(key,[]),reverse=True)
    if not v: continue
    top1=v[0]; top2=v[1] if len(v)>1 else 0
    n_big=sum(1 for x in v if x>=0.5*top1)   # how many hits are within 2x of the max
    print(f"\n{key[0]} / {key[1]}  (n={len(v)})")
    print(f"   top10: {', '.join(f'{x:,}' for x in v[:10])}")
    print(f"   #hits within 2x of max: {n_big}/{len(v)}   |   top / 2nd-best = {top1/top2 if top2 else 0:.1f}x")
