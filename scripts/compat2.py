import os, re, csv, glob

NLST = r"E:\ReverseEngineProjects\TimeStranger\dump\nlst_all"
STATUS = r"E:\ReverseEngineProjects\TimeStranger\dump\digimon_status.digimon_status_data.csv"
OUT = r"E:\ReverseEngineProjects\TimeStranger\dump\humanoid_compatibility.csv"

def bones(path):
    out = []
    with open(path, "rb") as f:
        for line in f.read().decode("latin1").splitlines():
            n = line.split(",")[0].strip()
            if n: out.append(n.lower())
    return out

# Humanoid roles the player animations drive; each maps to regexes tolerant of naming
# conventions (J_ prefix, l_/r_ vs _l/_r, arm/upperarm, etc.).
ROLES = {
    "head":   [r"head"],
    "neck":   [r"neck"],
    "spine":  [r"spine|chest|waist|hip|pelvis|center|cog|body"],
    "arm.l":  [r"(^|_)l.*arm|arm.*l($|_)|larm|arm_l|shoulder.*l|l.*shoulder"],
    "arm.r":  [r"(^|_)r.*arm|arm.*r($|_)|rarm|arm_r|shoulder.*r|r.*shoulder"],
    "elbow.l":[r"(l.*elbow|elbow.*l|lforearm|forearm.*l)"],
    "elbow.r":[r"(r.*elbow|elbow.*r|rforearm|forearm.*r)"],
    "hand.l": [r"(l.*hand|hand.*l|lwrist|wrist.*l)"],
    "hand.r": [r"(r.*hand|hand.*r|rwrist|wrist.*r)"],
    "leg.l":  [r"(l.*leg|leg.*l|l.*thigh|thigh.*l|l.*hip|hip.*l)"],
    "leg.r":  [r"(r.*leg|leg.*r|r.*thigh|thigh.*r|r.*hip|hip.*r)"],
    "knee.l": [r"(l.*knee|knee.*l|l.*shin|shin.*l|l.*calf)"],
    "knee.r": [r"(r.*knee|knee.*r|r.*shin|shin.*r|r.*calf)"],
    "foot.l": [r"(l.*foot|foot.*l|l.*ankle|ankle.*l|l.*toe)"],
    "foot.r": [r"(r.*foot|foot.*r|r.*ankle|ankle.*r|r.*toe)"],
}
def has_role(bset_str, pats): return any(re.search(p, bset_str) for p in pats)

def score(names):
    joined = "\n".join(names)
    return {role: has_role(joined, pats) for role, pats in ROLES.items()}

names = {}
with open(STATUS, encoding="utf-8", newline="") as f:
    r = csv.reader(f); next(r)
    for rec in r:
        if len(rec) >= 3 and rec[0].strip().isdigit():
            names[int(rec[0])] = rec[2].strip().replace("char_", "")

rows = []
for p in glob.glob(os.path.join(NLST, "chr*.nlst")):
    m = re.match(r"chr(\d+)\.nlst$", os.path.basename(p))
    if not m: continue
    cid = int(m.group(1))
    b = bones(p)
    sc = score(b)
    present = sum(sc.values())
    rows.append({"id": cid, "name": names.get(cid, f"chr{cid:03d}"), "bones": len(b),
                 "roles": present, "pct": round(100*present/len(ROLES),0),
                 "detail": "".join("1" if sc[r] else "." for r in ROLES)})

rows.sort(key=lambda r: (-r["roles"], r["id"]))
with open(OUT, "w", encoding="utf-8", newline="") as f:
    w = csv.DictWriter(f, fieldnames=["id","name","bones","roles","pct","detail"]); w.writeheader(); w.writerows(rows)

def band(lo,hi): return [r for r in rows if lo<=r['roles']<=hi]
print(f"roles tracked: {len(ROLES)} (head neck spine + L/R arm elbow hand leg knee foot)")
print(f"humanoid-completeness bands (of {len(ROLES)} roles):")
print(f"  15/15: {len(band(15,15))}  12-14: {len(band(12,14))}  9-11: {len(band(9,11))}  5-8: {len(band(5,8))}  <5: {len(band(0,4))}")
print("\nMOST humanoid (best player-animation candidates), top 40:")
for r in rows[:40]:
    print(f"  {r['name']:<24} id={r['id']:<4} roles={r['roles']:>2}/15 ({r['pct']:>3.0f}%)")
print(f"\nfull list -> {OUT}")
