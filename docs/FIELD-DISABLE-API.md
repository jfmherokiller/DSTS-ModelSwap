# Field disable/prohibit Lua API — native mapping

Reference for the `Field.*` / `Common.*` "disable" and "prohibit" Lua functions used by the game's field
scripts (e.g. `SetProhibitPlayerOnlyOperate` in `function_field.lua`). Addresses are IDA static
(image base `0x140000000`). Functions listed here are **named** in the IDB (`…exe.i64`).

The Lua helper that motivated this (from `function_field.lua`):

```lua
function SetProhibitPlayerOnlyOperate(is_prohibit)
  if is_prohibit == true then
    Field.DisableMenu(); Field.DisableAnalyzeAndFieldAttack(); Field.DisableSystemDigimonChat()
    Field.SetProhibitAnywhereDigimonRide(true); Field.SetProhibitDigimonRide(true); Common.ProhibitSave()
  else
    Field.CancelDisableMenu(); Field.CancelDisableAnalyzeAndFieldAttack(); Field.CancelDisableSystemDigimonChat()
    Field.SetProhibitAnywhereDigimonRide(false); Field.SetProhibitDigimonRide(false); Common.CancelProhibitSave()
  end
end
```

## The field disable-flags block

Most `Field.Disable*` / `Field.CancelDisable*` calls set/clear a single byte in one shared block. The
block is fetched by **`FieldSys_GetDisableFlagsBlock` (`0x1401D5030`)**:

```c
block = *(off_141EDE9C0[419]) + 0x33F8;   // 0 if that subsystem is null
```

Key fact: **`off_141EDE9C0[419]` is null in normal field play**, so the getter returns 0 and the
`Disable*` setters no-op (they guard on it). The block only exists in specific scripted states. This is
why `+732` (analyze-disabled) is **not** the reason Analyze is hidden while a Digimon — see
[`ANALYZE-GATE.md`](ANALYZE-GATE.md). The flags below are a genuine field feature, just not the model gate.

### Flag byte layout (offsets relative to the block base)

| Lua binding | Impl (Disable) | Impl (Cancel) | Byte | Set / Clear |
|---|---|---|---|---|
| `Field.DisableMenu` / `CancelDisableMenu` | `Lua_Field_DisableMenu` `0x140B9C5E0` | `Lua_Field_CancelDisableMenu` `0x140B9B370` | **+730, +731** | 1,1 / 0,0 |
| `Field.DisableAnalyze` / `CancelDisableAnalyze` | `Lua_Field_DisableAnalyze` `0x140B9C470` | `Lua_Field_CancelDisableAnalyze` `0x140B9B200` | **+732** | 1 / 0 |
| `Field.DisableSystemFieldAttack` / `Cancel…` | `Lua_Field_DisableSystemFieldAttack` `0x140B9C730` | `…Cancel…` `0x140B9B440` | **+733** | 1 / 0 |
| `Field.DisableAnalyzeAndFieldAttack` / `Cancel…` | `Lua_Field_DisableAnalyzeAndFieldAttack` `0x140B9C4D0` | `…Cancel…` `0x140B9B260` | **+734** | 1 / 0 |
| `Field.DisableSystemLadder` / `Cancel…` | `Lua_Field_DisableSystemLadder` `0x140B9C790` | `…Cancel…` `0x140B9B4A0` | **+735** | 1 / 0 |
| `Field.DisableSystemDigimonChat` / `Cancel…` | `Lua_Field_DisableSystemDigimonChat` `0x140B9C6D0` | `…Cancel…` `0x140B9B3E0` | **+736** | 1 / 0 |
| `Field.DisableAutoRegeneration` / `Cancel…` | `Lua_Field_DisableAutoRegeneration` `0x140B9C530` | `…Cancel…` `0x140B9B2C0` | **+737** | 1 / 0 |

Notes:
- **`DisableAnalyzeAndFieldAttack` sets a distinct byte `+734`**, *not* `+732` and `+733` together.
  So there are three analyze/attack-related flags: `+732` analyze-only, `+733` field-attack-only,
  `+734` the combined one that `SetProhibitPlayerOnlyOperate` actually uses.
- Each `Disable*`/`Cancel*` impl follows the same shape: fetch a lua/field context
  (`sub_140D6FD10` → `sub_140D6FCF0`, expects tag `== 5`), then tail-call a one-line setter
  `*(block + N) = 0/1` via `FieldSys_GetDisableFlagsBlock`.
- Nearby siblings in the same block seen while scanning: `+738` (cleared by `0x140B9BB00`), `+744`
  (`0x140B9BD20`, "PlayerFixedMaxSpeed"), `+760` (`0x140B9BDF0`, "StealthAtStart").

## Two registration systems (why some bindings are elsewhere)

- **Persistent `Field.*`** (the table above) — registered inline in
  `Lua_RegisterFieldNamespace` (`0x140BD38E0`) via `lua_pushcclosure` + set-field.
- **Per-frame `…InNowFrame` / `…OnlyThisFrame`** — a separate `{fn, name}` data table at ~`0x141AB0BE0`
  (e.g. `DisableMenuInNowFrame` `0x140A5CF70`, `DisableAnalyzeOnlyThisFrame` `0x140A5C550` which sets a
  per-object byte `+750`, not the shared block). These do the same disables but only for the current frame.

## Auxiliary prohibits (separate mechanism, not the disable-block)

These do **not** touch the flag block. Both pairs use a **shared dispatcher parameterized by a Lua
upvalue**, so the four Lua names resolve to two dispatchers plus their per-action callbacks/setters (all
named in the IDB):

| Lua binding(s) | Native (named) | Effect |
|---|---|---|
| `Field.SetProhibitDigimonRide(bool)` **and** `Field.SetProhibitAnywhereDigimonRide(bool)` | `Lua_Field_SetProhibitDigimonRide` `0x140A6F9C0` (shared; the "anywhere" flag comes from the closure upvalue) → `FieldPlayerPrep_SetRideProhibit` `0x1401D6900` | sets the digimon-ride prohibit flags on the field-player-prep object |
| `Common.ProhibitSave` / `Common.CancelProhibitSave` | `Lua_Common_ProhibitSave_dispatch` `0x140BF7C30` (shared; invokes the upvalue callback) → `Lua_Common_ProhibitSave_action` `0x140B97A60` / `Lua_Common_CancelProhibitSave_action` `0x140B962D0` → `FieldPlayer_SetSaveProhibited` `0x1409B0300` (arg 1 / 0) | toggles the "save prohibited" state on the FieldPlayerSystem |

## Relevance to the analyze-while-Digimon problem

The disable-block is **not** the baseline gate: it's null in normal play, and even `+732`/`+734` were
ruled out at runtime. The actual baseline path is now pinned:
`FieldKeyHelp_UpdateLayout` (`0x1401EEE10`) calls `Field_CanShowAnalyzeKeyHelp` (`0x1401FB090`), which
consumes the active model's Analyze capability. Runtime byte index 3 maps to `player_change_model`
column `Bool 8`; v1.2.0 enables that column in every generated row. See
[`ANALYZE-GATE.md`](ANALYZE-GATE.md) for the complete trace.

## `player_change_model` per-model capability array (field-action KeyHelp gates)

When a change-model is active, the game builds a **12-byte capability array** from the row's `Bool 6..16`
columns and caches it on the change-model param object at **`obj+0x1B0` (+432, bytes 0–7)** and
**`obj+0x1B8` (+440, bytes 8–11)**. The array is a **contiguous copy** (single 16-byte `_OWORD` store),
so **runtime byte index = column − 5** (anchored by the confirmed analyze mapping byte 3 ↔ `Bool 8`).
Default (no change-model / `obj+448==0`) is all-`0x01` = every action allowed; each field-action gate
reads its byte and hides its prompt when `0`.

- **Builder (writer):** `sub_1401F71B0` — `*(_OWORD *)(param + 432) = <row caps>`.
- **Copy-ctor:** `sub_1401F93D0` — clones `+432` between param objects.
- **Getter:** `(**(off_141EDE9C0[328]) + 0x128)(...)` i.e. vtable `+296` on the change-model manager at
  `off_141EDE9C0 + 2624`.

### Full column ↔ byte map (all 11 bool columns)

The array's 12 bytes were swept across the entire field-player module (`0x1401B0000`–`0x140208000`) for
readers. **Only four columns have any consumer**; the rest are copied but never read in this build.

| Runtime byte | Column | Consumer | What it gates |
|---|---|---|---|
| 0 | — (lead byte, no bool column) | — | not a capability |
| 1 | Bool 6 | none found | no effect |
| 2 | Bool 7 | none found | no effect |
| **3** | **Bool 8** | `Field_CanShowAnalyzeKeyHelp` `0x1401FB090` | field **Analyze** (`Q`) KeyHelp |
| 4 | Bool 9 | none found | no effect (set true by earlier rows; harmless) |
| **5** | **Bool 10** | `Field_CanShowInteractPrompt` `0x1401FB600` (→ `Field_UpdateInteractTarget` `0x1401F0440`, stores at `FieldPlayerSystem+8896+451`); also `Field_DispatchInteractAction` `0x1401DFBD0` **case 12** | **in-world interaction** target **type 12** (the floating world button) |
| **6** | **Bool 11** | `Field_DispatchInteractAction` `0x1401DFBD0` **case 10** (`BYTE6`) | **in-world interaction** target **type 10** |
| **7** | **Bool 12** | `Field_CanShowDigimonRideKeyHelp` `0x1401FADB0` (+ `sub_1401FB2E0`); also iterates the ride-prohibit array (`off_141EDE9C0+3641`, stride 112) | **Digimon Ride** mount prompt (layout id `100200`) |
| 8 | Bool 13 | none found | no effect |
| 9 | Bool 14 | none found | no effect |
| 10 | Bool 15 | none found | no effect (set true by earlier rows; harmless) |
| 11 | Bool 16 | none found | no effect |

**The mod enables the four consumed columns** in every generated row: `Bool 8` (analyze, v1.2.0),
`Bool 12` (ride, v1.2.1), `Bool 10` (interaction type 12, v1.2.2), `Bool 11` (interaction type 10, v1.2.3).

Notes:
- **`Field_DispatchInteractAction` (`0x1401DFBD0`)** is a `switch` on the interaction-target *type*
  (`*sub_1405BCC70(off_141EDE9C0+3992)`). Most types (1/2/4/7/8/9/11) are **not** capability-gated and work
  regardless of model; only **type 12 (Bool 10)** and **type 10 (Bool 11)** consult the array. The
  in-world **ladder** prompt is one of these two model-gated types — enabling both `Bool 10` and `Bool 11`
  covers it either way (which is why the swap required reverting to human before v1.2.2/v1.2.3).
- **Ride** takes two independent pieces: `Bool 12` makes the mount prompt appear; the ride *start* is
  governed by the C# `AllowDigimonRide` hook + the ride-prohibit array.
- **Distinguish from the disable-block:** the script-driven `DisableSystemLadder` flag `+735` (table above)
  is a *separate* per-scene toggle, independent of the loaded model — not what the swap tripped.
