#!/usr/bin/env python3
"""Pull all 24 classes from EQ2 census, build a base-name -> {classes} index,
then cross-reference our curated STRONG (class-specific) signatures in signatures.md.
Flags anything census says is shared/other/absent."""
import json, re, urllib.request, urllib.parse, collections

SID = "s:eq2i"
IDS = {"guardian":3,"berserker":4,"monk":6,"bruiser":7,"shadowknight":9,"paladin":10,
 "templar":13,"inquisitor":14,"warden":16,"fury":17,"mystic":19,"defiler":20,"wizard":23,
 "warlock":24,"illusionist":26,"coercer":27,"conjuror":29,"necromancer":30,"swashbuckler":33,
 "brigand":34,"troubador":36,"dirge":37,"ranger":39,"assassin":40}

def strip_tier(n):  # drop trailing roman-numeral upgrade tier
    return re.sub(r"\s+(?:I{1,3}|IV|VI{0,3}|IX|XI?)$", "", n).strip()

index = collections.defaultdict(set)   # base name (lower) -> set of class names
for cls, cid in IDS.items():
    url = f"https://census.daybreakgames.com/{SID}/get/eq2/spell?classes.{cls}.id={cid}&given_by=class&c:limit=2000&c:show=name,classes"
    try:
        data = json.load(urllib.request.urlopen(url, timeout=40))
    except Exception as e:
        print(f"# WARN {cls}: {e}"); continue
    for s in data.get("spell_list", []):
        nm = s.get("name","")
        if not nm or nm != nm.strip() or "_" in nm or nm[:1].islower():
            continue
        index[strip_tier(nm).lower()] |= set((s.get("classes") or {}).keys())
print(f"# census index: {len(index)} base ability names\n")

# write the index for reuse
with open("census_index.tsv","w") as f:
    for n in sorted(index): f.write(f"{n}\t{','.join(sorted(index[n]))}\n")

# parse signatures.md STRONG lines -> (final_class, [abilities])
CENSUS_ALIAS = {}  # our final-class label -> census key (all match lowercased here)
def norm(ab):
    ab = re.sub(r"\[.*?\]|\(.*?\)", "", ab)      # drop [notes]/(notes)
    ab = ab.split(";")[0]
    return strip_tier(ab.strip()).lower()

flags = collections.defaultdict(list)
confirmed = collections.Counter()
for line in open("signatures.md"):
    m = re.match(r"^(\w+) STRONG", line)
    if not m: continue
    fc = m.group(1).lower()
    if fc not in IDS: continue
    body = line.split(":",1)[1] if ":" in line else ""
    for chunk in re.split(r"[,;]", body):
        ab = norm(chunk)
        if not ab or ab in ("pets","scout-pet","strong","pet") or len(ab)<3: continue
        cls = index.get(ab)
        if cls is None:
            flags[fc].append((ab, "not-in-census (proc/pet/AA/pluralized?)"))
        elif cls == {fc}:
            confirmed[fc]+=1
        elif fc in cls:
            flags[fc].append((ab, "SHARED per census: "+",".join(sorted(cls))))
        else:
            flags[fc].append((ab, "census says: "+",".join(sorted(cls))+" (mislabel/collision?)"))

print("=== CROSS-REFERENCE FLAGS (STRONG entries census disputes) ===")
for fc in sorted(flags):
    print(f"\n{fc.upper()}  ({confirmed[fc]} confirmed unique)")
    for ab, why in flags[fc]:
        print(f"   • {ab:<28} {why}")
