"""Regenerate the player_change_model MBE rows shipped in the mod's mvgl-loader.

One appended row per Digimon: key = 90000 + digimon id -> its model code (chrNNN). Keys stay > the
stock rows (11831/11841) and ascending, which the game's binary-search lookup requires. Reads the
digimon_status dump (extract it first with MbeDumper from gamedata/app_0.dx11.mvgl).
"""
import csv, re

SRC = r"E:\ReverseEngineProjects\TimeStranger\dump\digimon_status.digimon_status_data.csv"
OUT = (r"E:\ReverseEngineProjects\TimeStranger\DSTS-PlayerModelSwap"
       r"\src\PlayerModelSwap\mvgl-loader\app_0\data\player_model.mbe\player_change_model.ap.csv")

# Bool 8 is the field Analyze capability. Stock change-model rows leave it false,
# which removes the Q Analyze KeyHelp entry whenever a change model is active.
BOOLS = "false,false,true,true,false,false,false,false,false,true,false"
HEADER = ("Int 1,String2 2,String2 3,Empty 4,Int 5,Bool 6,Bool 7,Bool 8,Bool 9,Bool 10,"
          "Bool 11,Bool 12,Bool 13,Bool 14,Bool 15,Bool 16,String2 17")

rows = []
with open(SRC, encoding="utf-8", newline="") as f:
    r = csv.reader(f); next(r)
    for rec in r:
        if len(rec) < 4:
            continue
        rid, name, code = rec[0].strip(), rec[2].strip(), rec[3].strip()
        if rid.isdigit() and name.startswith("char_") and re.fullmatch(r"chr\d{3,}", code):
            rows.append((int(rid), name, code))
rows = sorted({i: (i, n, c) for i, n, c in rows}.values())

lines = [HEADER] + [f"{90000 + i},{n},{c},,1,{BOOLS}," for i, n, c in rows]
with open(OUT, "w", encoding="utf-8", newline="\n") as f:
    f.write("\n".join(lines) + "\n")
print(f"wrote {len(rows)} rows -> {OUT}")
