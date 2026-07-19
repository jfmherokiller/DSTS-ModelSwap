"""Regenerate the player_change_model MBE rows shipped in the mod's mvgl-loader.

One appended row per Digimon: key = 90000 + digimon id -> its model code (chrNNN). Keys stay > the
stock rows (11831/11841) and ascending, which the game's binary-search lookup requires. Reads the
digimon_status dump (extract it first with MbeDumper from gamedata/app_0.dx11.mvgl).
"""
import csv, re

SRC = r"E:\ReverseEngineProjects\TimeStranger\dump\digimon_status.digimon_status_data.csv"
OUT = (r"E:\ReverseEngineProjects\TimeStranger\DSTS-PlayerModelSwap"
       r"\src\PlayerModelSwap\mvgl-loader\app_0\data\player_model.mbe\player_change_model.ap.csv")

# player_change_model per-model capability bools (columns Bool 6..16). The game copies these into a
# 12-byte capability array (runtime byte index = column - 5) that gates field-action KeyHelp prompts.
# Reverse-engineered readers (see docs/FIELD-DISABLE-API.md):
# Only four columns have a consumer in this build (reverse-engineered; see docs/FIELD-DISABLE-API.md):
#   Bool 8  (byte 3) = field Analyze KeyHelp           -> Field_CanShowAnalyzeKeyHelp   (0x1401FB090)
#   Bool 10 (byte 5) = in-world interaction, target type 12 -> Field_CanShowInteractPrompt (0x1401FB600)
#   Bool 11 (byte 6) = in-world interaction, target type 10 -> Field_DispatchInteractAction case 10 (0x1401DFBD0)
#   Bool 12 (byte 7) = Digimon Ride mount prompt       -> Field_CanShowDigimonRideKeyHelp (0x1401FADB0)
# (byte index = column - 5; the array is a contiguous copy at param+432/+440.)
# Bool 6, 7, 9, 13, 14, 15, 16 have NO consumer anywhere in the field-player module (no effect).
# Stock change-model rows leave the four gates false, hiding those actions while a change model is active.
# We enable all four field actions: Analyze (B8), both interaction sub-types (B10, B11), Digimon Ride (B12).
# (B9 and B15 are left true as inherited from earlier rows; they have no known consumer either way.)
#           B6    B7    B8   B9   B10  B11  B12   B13   B14   B15  B16
BOOLS = "false,false,true,true,true,true,true,false,false,true,false"
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
