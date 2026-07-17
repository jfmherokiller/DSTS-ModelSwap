import os, re, csv, glob

NLST = r"E:\ReverseEngineProjects\TimeStranger\dump\nlst_all"
STATUS = r"E:\ReverseEngineProjects\TimeStranger\dump\digimon_status.digimon_status_data.csv"
OUT = r"E:\ReverseEngineProjects\TimeStranger\dump\compatibility.csv"

def bones(path):
    s = set()
    with open(path, "rb") as f:
        for line in f.read().decode("latin1").splitlines():
            name = line.split(",")[0].strip()
            if name:
                s.add(name)
    return s

player = bones(os.path.join(NLST, "pc001a.nlst"))
# key bones the player animations drive (from spot-check): head look-at, arms/hands, spine, legs
KEY = [b for b in player if re.search(r"head|arm|hand|elbow|spine|neck|leg|knee|foot|shoulder|finger", b, re.I)]

# id -> display name
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
    overlap = player & b
    key_hit = sum(1 for k in KEY if k in b)
    rows.append({
        "id": cid,
        "name": names.get(cid, f"chr{cid:03d}"),
        "bones": len(b),
        "overlap": len(overlap),
        "overlap_pct": round(100 * len(overlap) / len(player), 1),
        "key_hit": key_hit,
        "key_pct": round(100 * key_hit / len(KEY), 1),
    })

rows.sort(key=lambda r: (-r["key_pct"], -r["overlap_pct"]))

with open(OUT, "w", encoding="utf-8", newline="") as f:
    w = csv.DictWriter(f, fieldnames=["id","name","bones","overlap","overlap_pct","key_hit","key_pct"])
    w.writeheader(); w.writerows(rows)

print(f"player bones: {len(player)}  | key anim bones: {len(KEY)} -> {sorted(KEY)[:20]}")
print(f"digimon skeletons analyzed: {len(rows)}")
def band(lo, hi): return [r for r in rows if lo <= r['key_pct'] < hi]
print(f"\nkey-bone compatibility bands (share of {len(KEY)} player anim bones present):")
print(f"  100%: {len(band(100,101))}   90-99%: {len(band(90,100))}   70-89%: {len(band(70,90))}   40-69%: {len(band(40,70))}   <40%: {len(band(0,40))}")
print("\nTOP 25 most compatible:")
for r in rows[:25]:
    print(f"  {r['name']:<22} id={r['id']:<4} key={r['key_pct']:>5}%  overlap={r['overlap_pct']:>5}% ({r['overlap']}/{len(player)})")
print("\nBOTTOM 12 (least compatible):")
for r in rows[-12:]:
    print(f"  {r['name']:<22} id={r['id']:<4} key={r['key_pct']:>5}%  overlap={r['overlap_pct']:>5}%")
print(f"\nfull ranked list -> {OUT}")
