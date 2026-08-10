#!/usr/bin/env python3
"""Who is amplified in the tight window vs who isn't — spatial(group) vs temporal(window) test."""
import re, statistics
from collections import defaultdict

OWNER = "Biffels"
def expand(a):
    a=a.replace(",","")
    m=re.match(r"^([0-9]+(?:\.[0-9]+)?)([KMBTQ]?)$",a)
    return int(round(float(m.group(1))*{"":1,"K":1e3,"M":1e6,"B":1e9,"T":1e12,"Q":1e15}[m.group(2)])) if m else None
LINE=re.compile(r"^\((\d+)\)\[[A-Za-z]{3} [A-Za-z]{3}\s+\d+ (\d\d:\d\d:\d\d) \d{4}\] (.*)$")
HIT=re.compile(r"^(.*?) (?:hits|multi attacks|flurries) (.*?) for (a critical of )?([0-9][0-9,\.]*[KMBTQ]?) ([a-z]+) damage\.$")

events=[]
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
    ability=left.split("'s ",1)[1] if "'s " in left else "(auto)"
    events.append(dict(t=t,top=top,ability=ability,amt=amt,crit=bool(crit)))

med=defaultdict(list)
for e in events: med[(e["top"],e["ability"])].append(e["amt"])
median={k:statistics.median(v) for k,v in med.items()}

WIN={f"21:03:{s:02d}" for s in range(17,21)}   # 21:03:17-20
per_player=defaultdict(lambda:{"maxr":0,"n":0,"hit":None})
for e in events:
    if e["t"] not in WIN: continue
    m=median[(e["top"],e["ability"])]
    r=e["amt"]/m if m else 0
    p=per_player[e["top"]]
    p["n"]+=1
    if r>p["maxr"]:
        p["maxr"]=r; p["hit"]=f'{e["amt"]:,} {e["ability"]} (med {m:,.0f})'

print("=== every player active in 21:03:17-20: peak amplification vs their own median ===")
print("    (>=3.0 = clearly amplified)\n")
for p,d in sorted(per_player.items(),key=lambda kv:-kv[1]["maxr"]):
    mark=" <== AMPLIFIED" if d["maxr"]>=3 else ""
    print(f"  {d['maxr']:>5.1f}x  {p:<12} ({d['n']:>2} hits)  best: {d['hit']}{mark}")
