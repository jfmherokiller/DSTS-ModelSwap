# Digimon Story: Time Stranger — Player Model Swap

A [Reloaded II](https://reloaded-project.github.io/Reloaded-II/) mod that swaps the **field player
character's model** to any of 582 Digimon, chosen from an in-game config dropdown. Author: **Noah Gooder**.

- **Save-safe** — the swap is applied only at render time and never written to your save, so you can
  enable, change, or remove the mod at any time without corrupting a save.
- **HUD-safe** — the game still sees the normal player at rest, so gender/HUD logic works.
- **Crash-safe** — cutscene crashes from missing rig bones are guarded.
- **Code + data only** — no game model/asset files are replaced.
- **Compatibility-tiered dropdown** — entries are prefixed `A_`…`F_` by how well each Digimon's
  skeleton fits the player's animations (see below).

## Requirements
- A legally-owned copy of **Digimon Story: Time Stranger** (Steam).
- **Reloaded II** with these mods (auto-resolved as dependencies): `Reloaded.Memory.SigScan.ReloadedII`,
  `reloaded.sharedlib.hooks`, `MVGL.FileLoader.Reloaded` (RyoTune).
- To build: **.NET 9 SDK**.

## Install (players)
1. Grab the packaged mod from Releases (or build it — see below) so you have a folder
   `timestranger.noah.playermodelswap` containing `TimeStranger.PlayerModelSwap.dll`, `ModConfig.json`,
   and `mvgl-loader/`.
2. Drop it in your Reloaded II `Mods` folder and enable it (its dependencies pull in automatically).
3. **Configure Mod** → set **Player Digimon** → apply. Load a save and cross a loading zone.

## Build from source (the mod)
```powershell
# RELOADEDIIMODS (set by Reloaded II) makes it build straight into your Mods folder;
# otherwise it outputs to src/PlayerModelSwap/bin/mod.
dotnet build src/PlayerModelSwap/TimeStranger.PlayerModelSwap.csproj -c Release
```
Or run `build.ps1` (builds Release and zips a distributable into `dist/`).

## Repo layout
```
src/PlayerModelSwap/     The mod (C#). Mod.cs = hooks, Config.cs = dropdown, Digimon.g.cs = generated
                         enum (id + compat tier), mvgl-loader/ = the player_change_model MBE rows.
tools/MbeDumper/         Standalone MVGL/MBE extractor used to dump game tables & skeletons. Depends on
                         MVLibraryNET (git submodule, external/). Run `git submodule update --init`.
scripts/                 Python: gen_cs2.py (regenerate the enum), gen_mbe_rows.py (regenerate MBE rows),
                         compat.py / compat2.py (skeleton compatibility analysis).
data/                    PLAYER_ANIM_COMPATIBILITY.csv — 582 Digimon ranked by humanoid-rig compatibility.
docs/RE-NOTES.md         Full reverse-engineering notes (how the swap + guards were found).
external/MVLibraryNET    Git submodule (RyoTune/MVLibraryNET), needed only to build MbeDumper.
```

## How it works (short)
Hooks `FieldPlayer_ResolveModelRef`. Per resolve it transiently sets the game's own change-model fields
(`+480` = key `90000+digimonId`, `+477` = flag), calls the original so the game fills the model string
and ref consistently from the `player_change_model` MBE table, then restores the fields — so nothing
persists to the save. The `player_change_model` rows for all Digimon ship in `mvgl-loader/` and are
loaded by MVGL.FileLoader. A second hook null-guards a cutscene look-at blend that crashed on Digimon
rigs. Full details in [`docs/RE-NOTES.md`](docs/RE-NOTES.md).

## Compatibility tiers
Player animations target a humanoid skeleton. Body animation retargets by bone *role* (so humanoid
Digimon animate well), but look-at/facial use exact bone names no Digimon has (they T-pose). Each
dropdown entry is graded from the game's skeleton data (`data/PLAYER_ANIM_COMPATIBILITY.csv`):

`A_` Excellent (full humanoid rig) · `B_` Good · `C_` Partial · `D_` Poor · `F_` Incompatible
(non-humanoid; T-poses) · `U_` Unrated. Pick **A_/B_** for the best look.

The remaining animation retargeting work lives in a separate follow-up repo (`AnimationFixes`).

## Credits & legal
- **RyoTune** — DSTS.ModLoader, MVGL.FileLoader, MVLibraryNET, RemixToolkit.
- **unluac** (SourceForge) — Lua 5.2 decompilation used during RE.
- Digimon names/ids/model data © Bandai Namco; this repository contains only mod code and derived
  configuration data — **no game assets are included**. You must own the game to use this mod.
- The mod code, tooling, and scripts here are licensed **MIT** — see [`LICENSE`](LICENSE). MIT covers
  this repository's own code; it does not extend to the Bandai Namco names/ids/model data that the
  generated tables derive from, nor to MVLibraryNET (see its own license in `external/`).
