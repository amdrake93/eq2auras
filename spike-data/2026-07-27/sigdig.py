import re, sys, collections
VERB = r"(?:hits|heals|multi attacks|absorbs|reduces|reflects|cures|afflicts|taunts|marks)"
LINE = re.compile(r"\]\s+(.+?)'s?\s+(.+?)\s+" + VERB + r"\b")
SELF = re.compile(r"\]\s+YOUR\s+(.+?)\s+" + VERB + r"\b")

FINAL, SUB = {}, {}
for ln in open('labels.tsv'):
    parts = ln.rstrip('\n').split('\t')
    if len(parts) == 3:
        FINAL[parts[0]] = parts[1]; SUB[parts[1]] = parts[2]

count = collections.defaultdict(collections.Counter)
for p in sys.argv[1:]:
    for line in open(p, encoding='utf-8', errors='replace'):
        m = LINE.search(line)
        if m:
            a, ab = m.group(1).strip(), m.group(2).strip()
            if a in ('YOU','YOUR','You'): a='Biffels'
        else:
            s = SELF.search(line)
            if not s: continue
            a, ab = 'Biffels', s.group(1).strip()
        if a in FINAL and len(ab)<=60: count[a][ab]+=1

# ability -> {final class -> (total, set of players)}
ab_cls = collections.defaultdict(lambda: collections.defaultdict(lambda: [0,set()]))
for actor, ctr in count.items():
    fc = FINAL[actor]
    for ab, n in ctr.items():
        ab_cls[ab][fc][0]+=n; ab_cls[ab][fc][1].add(actor)

nplayers = collections.Counter(FINAL.values())
sig = collections.defaultdict(list)   # class -> [(ab,total,nusers)]
shared = collections.defaultdict(list) # subclass -> [(ab,total,classes)]
for ab, per in ab_cls.items():
    classes = set(per); subs = {SUB[c] for c in classes}
    tot = sum(v[0] for v in per.values())
    if len(classes)==1:
        c = next(iter(classes)); sig[c].append((ab, per[c][0], len(per[c][1])))
    elif len(subs)==1:
        shared[next(iter(subs))].append((ab, tot, sorted(classes)))

ORDER=['Guardian','Berserker','Monk','Bruiser','Paladin','Shadowknight','Templar','Inquisitor',
 'Warden','Fury','Mystic','Defiler','Swashbuckler','Brigand','Troubador','Dirge','Ranger','Assassin',
 'Wizard','Warlock','Conjuror','Necromancer','Illusionist','Coercer']
for c in ORDER:
    np = nplayers.get(c,0)
    tag = '' if c!='Monk' and c!='Bruiser' else ' [Vicious=both, betrayed]'
    print(f"\n{c} ({SUB.get(c,'?')}) — {np} sample(s){tag}")
    for ab,t,u in sorted(sig.get(c,[]), key=lambda x:-x[1])[:5]:
        star = '  <-- on %d players'%u if u>1 else ''
        print(f"    {t:>6,}  {ab}{star}")
    if not sig.get(c): print("    (no class-specific — all shared/noise)")

print("\n=== SUBCLASS-SHARED (both finals cast — covers whole subclass) ===")
for sub in ['Warrior','Crusader','Brawler','Cleric','Druid','Shaman','Rogue','Bard','Predator','Sorcerer','Summoner','Enchanter']:
    top = sorted(shared.get(sub,[]), key=lambda x:-x[1])[:2]
    for ab,t,cl in top:
        print(f"    {t:>6,}  {ab:<26} {sub}: {','.join(cl)}")
