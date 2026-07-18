# Analyze-disabled-while-Digimon — reverse-engineering notes & follow-up plan

**Status:** open / documented limitation. This file is a self-contained handoff so a fresh session (or
another contributor) can resume without redoing the investigation. Addresses are IDA static (image base
`0x140000000`); at runtime the module rebases (one observed session: runtime base `0x7ff7e38b0000`,
slide `0x7ff6a38b0000`).

## Symptom
With the mod set to any Digimon, the field **`Q Analyze`** action is gone: the prompt never appears in
the command bar **and** pressing `Q` does nothing. As a normal (human) player it works normally. This is
purely an *availability* gate on the analyze action — not the analyze effect itself.

## What it is (confirmed by live debugging)
The game decides whether to offer Analyze by reading the **currently loaded player model every frame**.
Our swap changes the visible/loaded model to a Digimon, so the gate hides Analyze. There is **no cached
"analyze available" flag** to flip — availability is *computed live from the model*.

### How this was proven
1. **Ruled out the obvious state.** `+477` change-model flag (we restore it to 0), `FieldPlayer_GetModelName`
   (returns the human/base name at rest — why HUD/gender work), and `model_setting` col81/col82 (tested
   in-game, no effect).
2. **Ruled out the `DisableAnalyze` mechanism.** Lua `DisableAnalyze` sets byte `+732`, `CancelDisableAnalyze`
   clears it, on the FieldSystem disable-block at `*(*(off_7FF7E578E9C0)+0xD18)+0x33F8`. That block is
   **null** in normal field play while Analyze works → not the basic gate. (`DisableAnalyzeOnlyThisFrame`
   → per-object byte `+750`.)
3. **The gate does not re-query the model resolver.** An IDC conditional breakpoint on
   `FieldPlayer_ResolveModelRef` (`0x1409ADEE0`) that auto-continues for benign callers showed only TWO
   callers ever hit it: the render prep `sub_140AC1690` and the field-player model refresh
   `sub_140C42690`. Both are benign model-rendering. With both filtered, Digivice cycles + movement
   produced no other caller. So the gate reads *stored* model state, not a fresh resolve.
4. **`+400` mismatch flag DISPROVEN.** `sub_140C42690` sets `*(prepSystem+400)=1` when the loaded model's
   skeleton id != expected player id. Grabbed the live `FieldPlayerPreparationSystem` ptr at the refresh
   entry (RCX); `+400` reads **0** while a Digimon with Analyze disabled → not the gate.
5. **Clean same-position differential → NO flag exists.** Config hot-reloads; a **Digivice open/close
   re-resolves the model in place** (no area load, no position change), giving a fixed-position toggle.
   Captured `fp`(0x1000)/`prep`(0x800)/`fs`(0x400) in human vs Digimon at the *identical* position and
   diffed. Differences were **only** model-object churn: prep model-height/scale floats (2.61 vs 1.67),
   prep model-object pointers, `fp+0x50` (a model-geometry double), `fp+0x9e8` (pose float). **No boolean
   analyze-available byte anywhere.** Definitive: availability is computed each frame from the model.

## Key addresses / structures (IDA static base 0x140000000)
- `FieldPlayer_ResolveModelRef` `0x1409ADEE0` — resolves the visible model; our mod hooks this. Only
  callers at idle: render prep `0x140AC1690`, model refresh `0x140C42690`.
- `FieldPlayer_GetModelName` `0x1409ADDB0` — returns base (human) name when `+477==0`.
- `FieldPlayer_IsModelChanged_flag` `0x1409AF6D0` — returns `*(a1+477)`.
- `sub_140C42690` — FieldPlayerPreparationSystem model refresh/rebuild (RCX = prep system; sets `+400`
  mismatch, rebuilds visible model objects into prep slots `+280..+360/+392`).
- `sub_140AC1690` — render prep (`FieldPlayerPreparationSystem::PlayerModelGetter` build).
- Live pointer chain (stable across area loads within a session): `fs = *(0x7ff7e544a5d0)` (CGameData);
  `fp = *(fs+64)` (FieldPlayerSystem, vtable static `0x141430fd0`, unnamed → no field xrefs);
  `prep = FieldPlayerPreparationSystem` (vtable static `0x141... /` runtime `0x7ff7e4d2db78`).
- Analyze **effect** system (NOT the availability gate, for reference): state machine `0x14077A1B0`,
  `ObjectAnalyzeEffectManager` ctor `0x1405892A0`, locator/effect driver region `sub_140563E70`+
  `sub_1405E6490`-area reading `field_analyze_setting`.
- Analyze **UI**: `ui_fld_analyze` string `0x140... (aUiFldAnalyze)` referenced only from a name table
  (`0x7ff7e5359d48` runtime) → widget created by name from a data-driven field-UI layout. `UIAnalyze`
  vtable ctor `sub_1... (0x7ff7e3f714c0 runtime)` builds the analyze *screen* (after `Q`), constructed by
  the big UI factory `sub_7FF7E4654B80` (jump-table by widget id) — downstream of the gate.

## Where the gate actually lives (not yet pinned)
The decision to include the `Q Analyze` entry is in the **field command / action-availability layer** that
builds the command-bar/key-guide widget set. It reads the loaded model object (via the prep system's
model-object pointers) and excludes Analyze for non-standard models. It is data-driven (widget-by-name)
and sits behind pointer indirection, so it was not pinned by field xref (FieldPlayer is untyped) or by the
memory differential (no flat flag).

## Follow-up plan (to actually fix it)
Goal: offer Analyze while visibly a Digimon, without reverting the visual swap or breaking the field UI.

1. **Find the gate function.** Best lever is the live in-place toggle (Digivice open/close re-resolves the
   model at a fixed position). Options, roughly in order of promise:
   - Breakpoint the field command-bar / key-guide *builder* and diff its branch for the analyze entry
     between human and Digimon. Locate the builder by tracing the creator of the `ui_fld_analyze` widget
     (name-table driven) or the field action-prompt manager that also handles `V Photo Mode` / `C Digivice`
     (both remain available as a Digimon — compare how those are added vs Analyze).
   - Or set a hardware read watchpoint on a prep model-object *type* field the gate compares, filtering
     benign readers (same IDC-conditional-bp technique used here), during a toggle.
2. **Confirm the exact model check** the gate performs (e.g., "loaded model is a `pc*` player model" vs a
   `chr*` Digimon), and verify it against the differential facts above.
3. **Patch minimally.** Prefer a targeted hook that makes *only the gate's* model query report the human
   model, leaving rendering untouched — analogous to how the existing mod drives the game's own change-
   model path. Avoid clearing shared flags (`+400`, model-object state) that rendering also reads.
4. **Regression-check** the other model-keyed systems (cutscene look-at guard, HUD/gender, motion) after
   any patch, since they share the loaded-model coupling.

## Tooling notes (ida-pro-mcp live debugging)
- IDB auto-rebases to the runtime base on attach; use a `rt()`/`stx()` slide helper.
- Read live memory with `idc.read_dbg_memory(ea,n)` (the `ida_dbg` variant needs a buffer arg).
- Nested functions in `py_eval` don't see module globals → use `py_exec_file`.
- **IDC** conditional breakpoints auto-continue when the condition is false (use for "log/halt only for
  non-benign callers"). **Python `DBG_Hooks` do NOT fire under scripted continue/wait control** — that
  passive-logging path is dead here.
- Config hot-reloads; a Digivice open/close (or a loading zone) re-resolves the model in place — the clean
  fixed-position toggle that made the differential possible.
