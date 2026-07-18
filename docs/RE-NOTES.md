# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Goal

Replace the **player character's model** in *Digimon Story: Time Stranger* with a chosen Digimon, **via code only — no model/asset file replacement**. Delivery is a C# [Reloaded II](https://reloaded-project.github.io/Reloaded-II/CreatingMods/) mod that swaps the model at runtime.

- All work products / assets belong in **`E:\ReverseEngineProjects\TimeStranger`** (this folder). It starts empty.
- The game exe: `E:\SteamLibrary\steamapps\common\Digimon Story Time Stranger\Digimon Story Time Stranger.exe` (x64, PE, imagebase `0x140000000`).

## Reverse Engineering: IDA Pro (primary tool)

The exe is already loaded in IDA Pro and reachable through the **`ida-pro-mcp`** MCP tools (idb: `...\Digimon Story Time Stranger.exe.i64`). Hex-Rays decompiler is available.

- Check `mcp__ida-pro-mcp__server_health` first — if it times out, IDA's MCP server plugin needs to be (re)started in the IDA GUI (Edit → Plugins → MCP / Ctrl+Alt+M). Do not assume the DB is unavailable without checking.
- Long full-binary sweeps (`survey_binary`) can time out; prefer targeted queries (`search_text`, `find_bytes`, `decompile`, `xrefs_to`, `list_funcs`).
- When you locate a function to hook, record a **stable AOB signature** (see sigscan pattern below) rather than a raw address — addresses shift between game patches; signatures survive.

## Chosen approach: pure-Lua player model swap (CONFIRMED FEASIBLE)

IDA reconnaissance confirmed the swap is doable **entirely in Lua** — no native hook, no asset replacement. Use this path.

### The mechanism (traced end-to-end)

The field player system object holds two model ids plus a flag:
- `+464` (dword) — default **avatar** model id, resolved via MBE table `player_avatar_model`
- `+480` (dword) — **change** model id, resolved via MBE table `player_change_model`
- `+477` (byte) — "model changed" flag. When `1`, the renderer draws `player_change_model[+480]` instead of the avatar.

Model resolution: `ResourceTable_FindByName("player_change_model")` → `MbeTable_FindById(table, id)` (binary search on row's first dword) → row holds model name (`+8`) and model resource ref (`+16`). Resolver: `FieldPlayer_ResolveModelRef` @ `0x1409ADEE0` (branches on `+477`).

The **only** writer of `+480`/`+477` is `FieldPlayer_SetChangeModelId(sys, id)` @ `0x1409AD4E0` (`*(sys+480)=id; *(sys+477)=1;`), whose **only** caller is the Lua-bound setter below (verified by xrefs).

### Exact Lua calls (registered in `RegisterLua_Common` @ `0x140BCE1D0`, table `_G.Common`)

⚠️ **The first recon pass INVERTED the setter/getter — corrected here from the game's own `function_common.lua` (authoritative).** `GetPlayerModelName` is a no-arg **getter**; the actual swap setter is **`PlayerModelChange(id)`**. Trust the game's Lua usage over the IDA impl labels (which were mis-assigned).

| Lua call | Args | Effect |
|---|---|---|
| **`Common.PlayerModelChange(id)`** | 1 int | **THE SWAP** — sets change-model id + flag=1. Game wrapper: `function PlayerModelChange(id) Common.PlayerModelChange(id) end` |
| `Common.GetPlayerModelName()` | **0** →string | GETTER — current model name string (game uses it in `GetGender`, parses trailing `pc001`/`pc002`) |
| `Common.CancelPlayerModelChange()` | 0 | Revert (game wrapper guards with `if Common.IsPlayerModelChanged()`) |
| `Common.IsPlayerModelChanged()` | 0→bool/int | Is a change active |

The native writer of `+480`/`+477` (`0x1409AD4E0`, sets id+flag) is real; it's just bound to the Lua name **`PlayerModelChange`**, not `GetPlayerModelName`. ⚠️ `GetGender()` parses the model name suffix — swapping the model may make it return nil; watch for downstream gender-dependent breakage.

Delivery = the CustomStarters idiom: ReMIX enum picks the Digimon → generated Lua global → a decompiled game script (`field_map_change.lua`) calls `Common.PlayerModelChange(<key>)`.

### RESOLVED — the `player_change_model` id space (dumped from `app_0.dx11.mvgl`)

`+480` indexes the **`player_change_model`** table by its own `Int` key — **NOT** the global Digimon id. That table is a sheet inside `data/player_model.mbe` (base archive `gamedata/app_0.dx11.mvgl`) and ships with only **3 rows**, all protagonist-partner (Aegiomon) forms:

```
Int(key), String2(name),        String2(modelRef),   Int, ...bools..., String2(attach)
11831,    char_AEGIOMON,        *Aegiomon,           1,   ...,          (none)
11832,    char_AEGIOMON,        chr183ba010101,      1,   ...,          (none)
11841,    char_AEGIOCHUSMON,    chr184aa010101,      2,   ...,          fg07_u01 ... e200
```
(Sibling sheet `player_avatar_model` in the same MBE: `0=char_PLAYER_M`, `1=char_PLAYER_F`.)

**Digimon-id → model-code mapping is trivial and direct.** In `data/digimon_status.mbe` sheet `digimon_status_data`: col1 = Digimon id, col3 = `char_<NAME>`, col4 = model code = **`chr` + zero-padded id**:

| Digimon | id | name (col3) | model code (col4) |
|---|---|---|---|
| Agumon | 50 | `char_AGUMON` | `chr050` |
| Patamon | 96 | `char_PATAMON` | `chr096` |
| Gomamon | 343 | `char_GOMAMON` | `chr343` |

So `Common.GetPlayerModelName(343)` does **not** work directly (343 isn't a `player_change_model` key). To swap to an arbitrary Digimon, **add one row** to `player_change_model` via MVGL.FileLoader's MBE-CSV merge (see below) mapping a chosen key → that Digimon's `char_<NAME>` / model ref, then call `Common.GetPlayerModelName(<thatKey>)`. Still 100% data + Lua, no native code. Example new row for Gomamon:
```
50343, char_GOMAMON, chr343aa010101, , 1, false,false,false,true,false,false,false,false,false,true,false,
```
(Confirm the exact `union_model` variant key for `chr343` — union keys look like `chr183aa010101`; base+skeleton live in `data/union_model.mbe`.)

**Runtime caveat to test in-game (not a code blocker):** the player slot drives a humanoid agent skeleton (`char_MALE_AGENT` / `char_FEMALE_AGENT`); the stock change-models (Aegiomon forms) were authored for it. An arbitrary Digimon with a different skeleton may mis-animate. Verify per target Digimon.

### Tooling: MbeDumper

`MbeDumper/` (this repo) is a standalone .NET 9 console tool built against the `MVLibraryNET` submodule to extract/inspect MBE tables offline:
```
dotnet MbeDumper.dll list <mvgl> [substr]              # list files in an MVGL
dotnet MbeDumper.dll dump <mvgl> <fileSubstr> <outDir> # dump matching .mbe sheets to CSV
```
Base game data is `gamedata/app_0.dx11.mvgl` (+ `patch.dx11.mvgl`); `addcont_*` are DLC. Dumped CSVs live in `dump/`. Note MBE **sheet** names ≠ file names (e.g. both `player_avatar_model` and `player_change_model` are sheets in `player_model.mbe`).

### Fallback (only if a runtime row-injection is ever needed)

A native `Reloaded.Hooks` hook is **not** required for the swap itself. It would only matter if you needed to inject rows into `player_change_model` at runtime instead of via the MBE/CSV editor — which MVGL.FileLoader already handles without custom hooks.

### AOB signatures for the key functions (imagebase 0x140000000)

```
FieldPlayer_SetChangeModelId (0x1409AD4E0) — the swap setter (+480/+477):
89 91 E0 01 00 00 C6 81 DD 01 00 00 01 C3

LuaCommon_GetPlayerModelName_impl_SETTER (0x140B97A30) — Lua entry:
40 53 48 83 EC 20 8B D9 E8 ?? ?? ?? ?? 48 8B C8 E8 ?? ?? ?? ?? 48 8B C8 8B D3 48 83 C4 20 5B E9

FieldPlayer_ResolveModelRef (0x1409ADEE0) — model resolver (branches on +477):
48 85 D2 0F 84 09 01 00 00 48 89 5C 24 08 48 89 6C 24 10 48 89 74 24 18

lua_thunk_1int (0x140BF1180) — 1-int marshalling thunk:
48 89 5C 24 08 57 48 83 EC 20 BA D7 B9 F0 FF 48
```

Digimon numeric ids are used throughout (`Agumon (50)`, `Patamon (96)`, `Gomamon (343)`, …); see the CustomStarters enum for the full name→id list.

## Modding Stack (Reloaded II)

Reloaded II lives at `C:\Reloaded-II`; installed mods are in `C:\Reloaded-II\Mods`. Mods are C# .NET, each with a `ModConfig.json` (declares `ModDll`, `ModDependencies`, `SupportedAppId: ["digimon story time stranger.exe"]`). Ask the user for the C# **source** of any installed mod when you need it — only compiled DLLs are present locally.

Installed infrastructure to build on (do not reinvent):

| Mod | Purpose |
|---|---|
| **DSTS.ModLoader** (RyoTune) | Replaces game files with no unpack/repack. Hooks `PackFileResource_ReadFile` / `PackFileResource_GetFileSize`. |
| **MVGL.FileLoader.Reloaded** | Replace files inside **MVGL** archives at load time; edit **MBE** data tables via CSV. `IsUniversalMod`, `HasExports`. |
| **RemixToolkit.Reloaded** | ReMIX config engine: a mod's `ReMIX/Config/config.yaml` declares user `settings` (enums etc.) and `actions` that generate files at load (see below). |
| **Reloaded.Memory.SigScan.ReloadedII** | AOB signature scanning (resolve runtime addresses from byte patterns). |
| **reloaded.sharedlib.hooks** | Shared Reloaded.Hooks runtime. |
| **DSTS.QoL.CustomStarters** (SydMontague) | **Closest working example** — changes starter Digimon. Study this first. |

### How CustomStarters works (reference pattern)

`C:\Reloaded-II\Mods\DSTS.QoL.CustomStarters`:
- `ReMIX/Config/config.yaml` — enum settings (each choice like `"Gomamon (343)"`), plus an `actions:` block that `WriteFile`s a generated Lua file (`custom_starters.lua`) extracting the numeric id with `string.match(..., "%((%d+)%)")` and assigning globals (`starterIdVaccine`, etc.).
- `dsts-loader/patch/lua/m010.lua` (+ `m010_090.lua`) — a **patched copy of the game's own event script**; it reads those globals and calls `Common.SetDigimonGraspState(starterId…, DIGIMON_GRASP_FLAG_JOIN)` to grant the chosen Digimon.
- The ModLoader swaps the game's `m010.lua` for this patched one at read time — no archive repacking.

This is the template for a Lua-based player-model swap: expose the target Digimon as a ReMIX enum, generate a small Lua file with its id, and either patch an event script or call the relevant engine API.

### Sigscan pattern format

`DSTS.ModLoader/Project/MVGL.FileLoader.Reloaded/scans.ini` shows the AOB style (space-separated hex, `??` wildcards):
```
[Scans]
PackFileResource_ReadFile=41 54 41 56 41 57 48 81 EC B0 01 00 00
```
Generate new signatures for hook targets in the same format.

## Mod delivery conventions (verified from FileLoader source)

Our mod: `C:\Reloaded-II\Mods\timestranger.noah.mods` (data/Lua only, `ModDll: ""`, depends on `DSTS.ModLoader`, `RemixToolkit.Reloaded`, `MVGL.FileLoader.Reloaded`).

**MBE data edits (MVGL.FileLoader)** — probe folder is **`mvgl-loader/`** in the mod dir (`Mod.cs`: `AddProbingPath("mvgl-loader")`). Layout mirrors `<mvglName>/<pathInArchive>/<sheet>.csv`, where `mvglName` = archive name before the first dot (`app_0.dx11.mvgl` → `app_0`). So a `player_change_model` edit goes at:
```
mvgl-loader/app_0/data/player_model.mbe/player_change_model.csv       # full-sheet diff-merge
mvgl-loader/app_0/data/player_model.mbe/player_change_model.ap.csv    # APPEND new rows
```
- `.ap.csv` → `Sheet.AppendCsv` (adds rows). CSV reader has **header=true**, so include a header line (same as a MbeDumper dump) followed by data rows; each row needs all columns in schema order/type.
- plain `.csv` → diff vs original sheet, then merge (edits existing cells).
- Sheet name comes from the CSV filename before the first `.`; the containing folder must be named exactly `<mbeFile>.mbe`.

**Lua (DSTS.ModLoader)** — RESOLVED. DSTS.ModLoader is a 12-line shim that just does `mvgl.AddProbingPath("dsts-loader")`, so `dsts-loader/` is a second FileLoader probe path. A file at `dsts-loader/patch/lua/X.lua` binds to path `patch\lua\X.lua` and the read hook (`TryReadFile`, matches the game's requested path string) serves it whenever the game requests that script. The game requests scripts **prefixed by the source archive**, and `patch.dx11.mvgl` overlays `app_0.dx11.mvgl` (both hold `lua\*.lua`; patch wins) — so game scripts are requested as `patch\lua\<name>.lua`.

⚠️ **Stock game Lua is compiled Lua 5.2 bytecode** (`\x1bLuaR` = `\x1bLua`+ver `0x52`, standard 5.2 header); MbeDumper `extract` pulls raw bytecode. The game's `lua_load` compiles **plaintext source** too, so replacements can be source. To edit a stock script: extract the **patch-archive** copy, decompile with **unluac** (`tools/unluac.jar`, fetched from SourceForge; `java -jar unluac.jar <file> > out.lua`), edit, ship as source. Validate edits with `luaparser` (pip) before shipping — a syntax error breaks that script.

**`require` injection trick:** `require("modname")` makes the game request `patch\lua\modname.lua`, which the FileLoader serves from the mod — that's how CustomStarters runs its generated `custom_starters.lua`. Our mod uses `pcall(require, "player_model_swap")` in a decompiled `field_map_change.lua`, then calls `ApplyPlayerModelSwap()` (in `player_model_swap.lua`) on map trigger/arrival. `pcall` + `if ApplyPlayerModelSwap then` guards ensure a missing/broken script never breaks map transitions.

## Follow-ups
- **Analyze-disabled-while-Digimon — worked around in v1.1.0.** The field `Q Analyze` (and a few other
  actions) are gated natively on the loaded model; the exact gate was never pinned (Lua and data are not
  levers either). Instead of patching it, v1.1.0 ships a **Temporary-Human hotkey** that briefly reverts to
  the human model in place (via the game's own field refresh) so those actions work, then swaps back. Full
  investigation, the disproven `sub_1409E7130` patch, and the shipped hotkey internals are in
  [`ANALYZE-GATE.md`](ANALYZE-GATE.md).
- **Animation retargeting** for non-humanoid rigs lives in the separate `AnimationFixes` repo.

## Conventions

- Target app id everywhere is lowercase `digimon story time stranger.exe`.
- New mods depend on `DSTS.ModLoader` (file replacement) and/or `RemixToolkit.Reloaded` (config UI), which transitively pull in SigScan + sharedlib.hooks.
- Keep new mod source and generated assets under `E:\ReverseEngineProjects\TimeStranger`; deploy built mods into `C:\Reloaded-II\Mods\<ModId>\`.
