import re, sys, collections
VERB = r"(?:hits|heals|multi attacks|absorbs|reduces|reflects|cures|afflicts|taunts|marks)"
LINE = re.compile(r"\]\s+(.+?)'s?\s+(.+?)\s+" + VERB + r"\b")
SELF = re.compile(r"\]\s+YOUR\s+(.+?)\s+" + VERB + r"\b")
target = sys.argv[1]
FINAL, SUB = {}, {}
for ln in open('labels.tsv'):
    p = ln.rstrip('\n').split('\t')
    if len(p)==3: FINAL[p[0]]=p[1]; SUB[p[1]]=p[2]
finals = [c for c in SUB if SUB[c]==target]
count = collections.defaultdict(collections.Counter)
for path in sys.argv[2:]:
    for line in open(path, encoding='utf-8', errors='replace'):
        m = LINE.search(line)
        if m:
            a, ab = m.group(1).strip(), m.group(2).strip()
            if a in ('YOU','YOUR','You'): a='Biffels'
        else:
            s=SELF.search(line)
            if not s: continue
            a, ab = 'Biffels', s.group(1).strip()
        if a in FINAL and len(ab)<=60: count[a][ab]+=1
ab_cls = collections.defaultdict(lambda: collections.defaultdict(lambda:[0,set()]))
for a,ctr in count.items():
    for ab,n in ctr.items():
        ab_cls[ab][FINAL[a]][0]+=n; ab_cls[ab][FINAL[a]][1].add(a)
def klass(ab):
    cs=set(ab_cls[ab]); subs={SUB[c] for c in cs}
    if len(cs)==1 and next(iter(cs)) in finals: return ('spec', next(iter(cs)))
    if len(subs)==1 and next(iter(subs))==target: return ('shared', None)
    if subs & {target}: return ('noise', sorted(subs))
    return None
for fc in finals:
    players=[a for a in FINAL if FINAL[a]==fc]
    print(f"\n=== {fc}  (samples: {', '.join(players)}) — class-specific candidates ===")
    rows=[(ab, ab_cls[ab][fc][0], len(ab_cls[ab][fc][1])) for ab in ab_cls if klass(ab)==('spec',fc)]
    for ab,t,u in sorted(rows,key=lambda x:-x[1])[:18]:
        print(f"   {t:>6,}  {ab:<30} {'on %d/%d'%(u,len(players)) if len(players)>1 else ''}")
print(f"\n=== {target} SUBCLASS-SHARED (both finals) ===")
rows=[(ab,sum(v[0] for v in ab_cls[ab].values()),sorted(ab_cls[ab])) for ab in ab_cls if klass(ab)==('shared',None)]
for ab,t,cl in sorted(rows,key=lambda x:-x[1])[:15]:
    print(f"   {t:>6,}  {ab:<30} {','.join(cl)}")
