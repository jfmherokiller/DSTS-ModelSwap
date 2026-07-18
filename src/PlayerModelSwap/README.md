# Player Model Swap (Native) — timestranger.noah.playermodelswap

Swaps the field player character's model to any of 582 Digimon, chosen in the mod config.
**Save-safe, HUD-safe, crash-safe.** Author: Noah Gooder.

Source: <https://github.com/jfmherokiller/DSTS-ModelSwap>

## How it works
Hooks **`FieldPlayer_ResolveModelRef`** (`0x1409ADEE0`, by AOB signature). For each player-model resolve
it transiently sets the game's own change-model fields (`+480` = key, `+477` = flag), calls the original
so the game fills **both** the model name string and the model ref consistently from
`player_change_model[key]`, then **restores** those fields. Because the flag is never left set:

- **Save-safe** — nothing is written to the save; adding/changing/removing the mod can't brick it
  (unlike the old Lua `Common.PlayerModelChange` approach, which persisted the flag → invisible null
  model on removal).
- **HUD-safe** — at rest the game sees the normal player, so gender/HUD logic works.

The `player_change_model` rows for all 582 Digimon (key = `90000 + id` → `chrNNN`) are shipped in
`mvgl-loader/` and loaded by **MVGL.FileLoader.Reloaded** (a dependency).

A second hook null-guards a cutscene look-at/aim blend (`sub_140222560`) that would otherwise crash
when a Digimon rig lacks the player's `head` aim bone. A vectored-exception crash logger prints any
access-violation address (as `game+0xOFFSET`) via `OutputDebugString` for diagnosis.

**Temporary-Human Hotkey:** a background thread watches the configured key. On each press it flips a
"suppress swap" flag (so `ResolveModelRef` returns the human model) and, on the game thread via a
per-frame field-update hook, calls the game's own field-model refresh (`sub_1409769F0`) so the model
reloads in place. Press again to restore the Digimon. Still save-safe — nothing persists.

Files: `Digimon.g.cs` (generated enum + id→code map), `Mod.cs` (hooks), `Config.cs` (dropdown + hotkey).

## Usage
1. Enable in Reloaded II → **Configure Mod** → set **Player Digimon** → apply.
2. Launch / load. The model refreshes on map load, so cross a loading zone if already in-game.
3. (Optional) Set **Temporary-Human Hotkey** to a key. In the field, tap it to revert to the human model
   in place — for `Q Analyze` and other human-only actions — and tap again to return to your Digimon.
4. Change or set to **None** at any time — no save cleanup ever needed.

## Compatibility tiers (dropdown prefix)
Player animations are authored for a **humanoid skeleton**. A Digimon animates well only if its
skeleton has the same humanoid bone roles. Each dropdown entry is prefixed with a tier derived from
analysing the game's skeleton bone lists (`.nlst`); the ranking is in `data/PLAYER_ANIM_COMPATIBILITY.csv`
in the source repo.

| Prefix | Meaning | Count |
|---|---|---|
| **A_** | Excellent — full humanoid rig, animates well (e.g. `A_Agumon`, `A_Guilmon`) | 132 |
| **B_** | Good — mostly humanoid, minor gaps | 113 |
| **C_** | Partial — missing some limbs/joints | 50 |
| **D_** | Poor — barely humanoid | 44 |
| **F_** | Incompatible — non-humanoid (quadruped/blob/flyer); will T-pose | 157 |
| **U_** | Unrated — no skeleton data found | 86 |

Pick **A_** / **B_** Digimon for the best look. The dropdown lists tiers A→U in order. (The tier is a
heuristic from bone naming; a few humanoid Digimon with unusual rigs may be mis-graded — treat it as a
strong hint, not a guarantee.)

## Known limitations
- **Body animation** retargets by bone role, so it works for humanoid (A/B) Digimon; **look-at /
  head-tracking and facial expressions** need exact-name bones no Digimon has → those specific
  animations T-pose or stay neutral for everyone (the associated crash is guarded).
- **Non-humanoid (F_) Digimon** T-pose broadly — expected, since they lack the humanoid skeleton.
- **Analyze (`Q`) and a few other actions are locked to the human form.** The game reads the *loaded
  model* to decide their availability, so they don't appear while a Digimon is rendered. Use the
  **Temporary-Human Hotkey** (below) to briefly revert. Background:
  [`docs/ANALYZE-GATE.md`](https://github.com/jfmherokiller/DSTS-ModelSwap/blob/master/docs/ANALYZE-GATE.md).
- Changing the enum names (tier prefixes) resets a previously-saved dropdown selection to `None` once;
  just re-pick your Digimon.
