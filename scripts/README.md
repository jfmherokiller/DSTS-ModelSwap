# Scripts

Regeneration/analysis helpers. Absolute Windows paths inside assume this repo lives at
`E:\ReverseEngineProjects\TimeStranger\DSTS-PlayerModelSwap` and that game tables were extracted to
`E:\ReverseEngineProjects\TimeStranger\dump\` — edit the constants at the top for your setup.

- **gen_cs2.py** — regenerate `src/PlayerModelSwap/Digimon.g.cs` (enum: Digimon id + compat-tier name
  prefix). Reads the digimon_status dump + `data/PLAYER_ANIM_COMPATIBILITY.csv`.
- **gen_mbe_rows.py** — regenerate `src/PlayerModelSwap/mvgl-loader/.../player_change_model.ap.csv`
  (one row per Digimon, key = 90000 + id → chrNNN).
- **compat.py** / **compat2.py** — skeleton compatibility analysis from extracted `.nlst` bone lists
  (`compat2.py` produces the humanoid-role ranking → `data/PLAYER_ANIM_COMPATIBILITY.csv`).

Get the source data with `tools/MbeDumper` against the game's `gamedata/app_0.dx11.mvgl`:
`dump` (MBE→CSV, e.g. digimon_status), `extractregex` (raw files, e.g. `^(chr[0-9]+|pc001a)\.nlst$`
skeletons), `list`, `testap`.
