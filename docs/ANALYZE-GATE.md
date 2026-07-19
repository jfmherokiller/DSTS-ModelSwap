# Analyze while transformed — confirmed gate and fix

**Status: RESOLVED in v1.2.0.** The generated `player_change_model` rows now set `Bool 8=true`.
In-game verification confirmed that `Q Analyze` remains visible and opens the Analyze screen while the
player is rendered as a Digimon. No native Analyze hook is required. Addresses below use IDA image base
`0x140000000`.

## Root cause

The field command bar is a data-driven KeyHelp layout. Human and transformed states selected different
layout IDs:

| State | Layout | Entries | Difference |
|---|---:|---:|---|
| Human | `111001` | 8 | Includes `+10000` component |
| Digimon | `101001` | 7 | Omits `+10000` component |

The missing component expands to KeyHelp part `23`, `key_help_0055`, action type `6`: the `Q Analyze`
entry. The prompt was not merely hidden; the game omitted it from the selected layout.

`FieldKeyHelp_UpdateLayout` (`0x1401EEE10`, named in the IDB) builds the field layout. Its Analyze term is:

```c
layout = baseLayout;
if (Field_CanShowAnalyzeKeyHelp(fieldLogic))
    layout += 10000;
```

`Field_CanShowAnalyzeKeyHelp` (`0x1401FB090`, named in the IDB) obtains the active player-model state.
When that state's byte `+0x1C0` is zero, it uses an all-true default capability block. When `+0x1C0` is
one, it copies 12 bytes from `+0x1B0`/`+0x1B8` and requires byte `+0x1B3` (index 3) for Analyze.

Live comparison at the same state object:

| State | `+0x1C0` | Capability bytes `+0x1B0..+0x1BB` | Analyze byte |
|---|---:|---|---:|
| Human | `0` | `[1,0,0,0,1,0,0,0,0,0,1,0]` (not selected) | default `1` |
| Digimon | `1` | `[1,0,0,0,1,0,0,0,0,0,1,0]` | index 3 = `0` |

The 12 runtime bytes map exactly to `player_change_model` columns `Int 5` followed by `Bool 6..16`.
Therefore runtime byte index 3 is MBE column **`Bool 8`**. The three stock Aegiomon change-model rows set
`Bool 8=false`, and the mod's generator originally copied that template to all 582 Digimon rows.

## Shipped fix

`scripts/gen_mbe_rows.py` now emits:

```text
Int 5 = 1
Bool 6..16 = false,false,true,true,false,false,false,false,false,true,false
                         ^ Bool 8 / field Analyze capability
```

Only `Bool 8` changed. The remaining movement/action capability values are untouched. The regenerated
append CSV passed MVLibrary's `AppendCsv` plus MBE serialization round-trip (`Write OK`), and the installed
v1.2.0 build was verified in game:

1. Player remained visibly transformed.
2. `Q Analyze` was present.
3. Pressing `Q` opened Analyze successfully.

The Temporary-Human hotkey is retained as an optional fallback for other human-only interactions. The
Digimon-ride crash guard remains active and continues to block ride start while transformed.

## Confirmed native/UI functions

| Address | IDB name | Role |
|---|---|---|
| `0x1401EEE10` | `FieldKeyHelp_UpdateLayout` | Builds and submits the field KeyHelp layout; adds `10000` for Analyze |
| `0x1401FB090` | `Field_CanShowAnalyzeKeyHelp` | Full Analyze availability predicate; consumes model capability byte index 3 |
| `0x1409D4680` | `KeyHelp_ExpandLayout` | Expands a numeric layout into KeyHelp parts |
| `0x140889910` | `UIKeyHelpFront_SetLayout` | Configures persistent KeyHelp child entries |
| `0x140888DA0` | `UIKeyHelpFront_TickStateMachine` | Ticks the active KeyHelp object and its children |
| `0x1406C5FB0` | `UIAnalyze_ConstructWidgets` | Builds the downstream Analyze screen after the action is accepted |
| `0x140C42690` | `FieldPlayerPreparation_RebuildModels` | Rebuilds player model objects; useful debugger control point, not the gate |
| `0x140A30620` | `FieldPlayerModelRefresh_ExecuteSequence` | Calls the model rebuild during the mod's refresh path |

Unique AOB for `Field_CanShowAnalyzeKeyHelp`:

```text
48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 57 48 83 EC 60 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 44 24 ?? 48 8B F9 48 8B 05 ?? ?? ?? ?? 80 B8 E0 12 00 00 00 0F 85 ?? ?? ?? ?? 8B 90 30 12 00 00
```

## Debugging trail

- A breakpoint on `UIKeyHelpFront_TickStateMachine` hit immediately and exposed the live layout object.
- Human layout `111001` contained action types `16,16,16,16,6,7,19,3`; transformed layout `101001`
  contained `16,16,16,16,7,19,3`. Action type `6` was the sole missing entry.
- Conditional breakpoints on every short-circuit branch in `Field_CanShowAnalyzeKeyHelp` stopped first at
  `0x1401FB195`, the `jz` after testing byte 3 of the copied capability block.
- In transformed form, `+0x1C0=1` and byte 3 was zero. Temporary-Human changed `+0x1C0` to zero, causing
  the function to bypass the row block and use all-true defaults.
- `UIKeyHelpFront_SetLayout` and the old call-site breakpoint at `0x1401EF129` did not fire during the
  first hotkey experiment; the live tick object and later layout-builder decompilation provided the
  reliable trace.
- `FieldPlayerPreparation_RebuildModels` was the control breakpoint that proved debugger delivery and
  captured caller `FieldPlayerModelRefresh_ExecuteSequence` at `0x140A3064B`.

## Ruled-out paths retained for future naming

These functions are not the field Analyze availability gate:

- `0x140563E70`, `0x140563FB0` — always-on Analyze target/locator scanning.
- `0x1405892A0`, `0x140586490` — Analyze effect-manager construction/driver path.
- `0x14077A1B0` — Analyze effect state machine.
- `0x1409E7130`, `0x140C68640`, `0x140C6A010` — another Analyze context, likely battle; forcing
  `0x1409E7130` true had no effect on the field prompt.
- Disable/prohibit bytes `+732` and `+734` — scripted disable API, not baseline availability. See
  [FIELD-DISABLE-API.md](FIELD-DISABLE-API.md).
- `FieldPlayerPreparationSystem +400` — model mismatch candidate; read zero while transformed.
- `FieldPlayer_ResolveModelRef` callers — rendering/model rebuild only; the gate consumes stored model
  capability state rather than re-resolving the model.

## IDA stability notes

- Do static decompilation only while detached and rebased to `0x140000000`.
- While attached, use debugger-memory/register operations only; avoid static sweeps and `auto_wait()`.
- IDA rebases to the live module base on attach. Compute runtime addresses as
  `runtimeBase + (staticAddress - 0x140000000)`.
- `ida_idd.dbg_read_memory(ea, size)` worked reliably for bounded live reads.
- Conditional breakpoints on branch instructions using `ZF == 0/1` isolated the first rejecting
  short-circuit without per-frame stepping.
