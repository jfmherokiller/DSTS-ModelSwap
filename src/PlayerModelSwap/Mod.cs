using System.Diagnostics;
using System.Runtime.InteropServices;
using Reloaded.Hooks.Definitions;
using Reloaded.Hooks.Definitions.X64;
using Reloaded.Mod.Interfaces;
using Reloaded.Memory.SigScan.ReloadedII.Interfaces;
using TimeStranger.PlayerModelSwap.Template;
using TimeStranger.PlayerModelSwap.Configuration;
using IReloadedHooks = Reloaded.Hooks.ReloadedII.Interfaces.IReloadedHooks;

namespace TimeStranger.PlayerModelSwap;

public class Mod : ModBase
{
    // FieldPlayer_ResolveModelRef (0x1409ADEE0). Fills a std::string (a2) with the player's model
    // name, branching on the +477 "change model" flag. We let the original run (avatar path,
    // flag untouched) then overwrite the output string with our chosen Digimon's model code.
    // This never sets the persistent flag -> the save is never modified -> safe to add/remove.
    private const string ResolveModelRefSig =
        "48 85 D2 0F 84 09 01 00 00 48 89 5C 24 08 48 89 6C 24 10 48 89 74 24 18";

    [Function(CallingConventions.Microsoft)]
    public delegate void ResolveModelRefFn(nint self, nint outStr, nint outRef, uint arg4);

    // Look-at/aim blend virtual method (sub_140222560): computes *a1 = t*v6[26] + (1-t)*v6[25] from a
    // per-model part-list. Crashes at 0x140222589 (`mov rbx,[rax+0D8h]`) when *(v5+24) is null, which
    // happens for a Digimon rig used in cutscene animation (the part/sub-object is absent). We null-guard.
    private const string BlendSig =
        "48 89 5C 24 10 57 48 83 EC 20 48 8B 42 08 48 8B F9 48 3B 42 10 75 04 33 C0 EB 03 48 8B 00 48 8B 40 18 48 8B 98 D8 00 00 00";

    [Function(CallingConventions.Microsoft)]
    public delegate nint BlendFn(nint a1, nint a2);

    // --- Temporary-Human hotkey plumbing ---------------------------------------------------------
    // GetFieldSystem (0x14099F990): returns the CGameData singleton (creates it if null at runtime).
    private const string GetFieldSystemSig =
        "48 83 EC 28 48 8B 05 ?? ?? ?? ?? 48 85 C0 0F 85 ?? ?? ?? ?? B9 C0 00 00 00";
    // sub_1409769F0: "ensure field manager + refresh" — the exact ensure+refresh the Digivice-close
    // path uses (-> the model rebuild sub_140C42690). Called with *(CGameData+128) = the manager slot.
    private const string RefreshTriggerSig =
        "40 53 48 83 EC 20 48 8B D9 48 8B 01 48 85 C0 75 ?? B9 B8 CA 02 00 E8 ?? ?? ?? ?? 48 89 44 24 ?? 48 85 C0 74 ?? 48 8B C8 E8 ?? ?? ?? ?? 90 48 89 03 48 85 C0";
    // sub_1401E50C0: field state update, runs per-frame during field play. We hook it only to get a
    // game-thread "tick": after the original runs, if a refresh is queued we fire it (a valid point,
    // it's where the game itself triggers refreshes -> no re-entrancy).
    private const string FieldUpdateSig =
        "F3 0F 11 4C 24 ?? 53 57 41 56 48 83 EC 40";
    private const int OffFieldManagerSlot = 128; // *(CGameData + 128) = arg for the refresh trigger

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] // x64: single ABI, attribute ignored
    private delegate nint GetFieldSystemFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint RefreshTriggerFn(nint managerSlot);
    [Function(CallingConventions.Microsoft)]
    public delegate nint FieldUpdateFn(nint a1, float a2);

    private readonly IModLoader _modLoader = null!;
    private readonly IReloadedHooks? _hooks;
    private readonly ILogger _logger = null!;
    private readonly IModConfig _modConfig = null!;

    private Config _config = null!;
    private IHook<ResolveModelRefFn>? _hook;
    private IHook<BlendFn>? _blendHook;
    private IHook<FieldUpdateFn>? _fieldUpdateHook;
    private volatile int _targetKey; // player_change_model key (90000 + digimon id), or 0 = none

    // Temporary-Human hotkey state.
    private GetFieldSystemFn? _getFieldSystem;
    private RefreshTriggerFn? _refreshTrigger;
    private volatile bool _suppressHuman;  // true = swap suppressed this session (render the human model)
    private volatile bool _pendingRefresh; // set on toggle; consumed on the game thread by the field hook
    private Thread? _pollThread;

    public Mod(ModContext context)
    {
        _modLoader = context.ModLoader;
        _hooks = context.Hooks;
        _logger = context.Logger;
        _modConfig = context.ModConfig;
        _config = context.Configuration;
        UpdateTarget();
        InstallCrashLogger();

        var scannerController = _modLoader.GetController<IStartupScanner>();
        if (scannerController == null || !scannerController.TryGetTarget(out var scanner))
        {
            _logger.WriteLine($"[{_modConfig.ModId}] SigScan controller unavailable; swap disabled.");
            return;
        }

        scanner.AddMainModuleScan(ResolveModelRefSig, result =>
        {
            if (!result.Found)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] ResolveModelRef signature not found; swap disabled.");
                return;
            }

            var baseAddr = Process.GetCurrentProcess().MainModule!.BaseAddress;
            var addr = (long)baseAddr + result.Offset;
            _hook = _hooks!.CreateHook<ResolveModelRefFn>(ResolveModelHook, addr).Activate();
            _logger.WriteLine($"[{_modConfig.ModId}] Hooked ResolveModelRef @ 0x{addr:X}; target={_config.PlayerDigimon}");
        });

        scanner.AddMainModuleScan(BlendSig, result =>
        {
            if (!result.Found)
            {
                _logger.WriteLine($"[{_modConfig.ModId}] blend null-guard signature not found.");
                return;
            }
            var addr = (long)Process.GetCurrentProcess().MainModule!.BaseAddress + result.Offset;
            _blendHook = _hooks!.CreateHook<BlendFn>(LookAtBlendHook, addr).Activate();
            _logger.WriteLine($"[{_modConfig.ModId}] Hooked look-at blend null-guard @ 0x{addr:X}");
        });

        // --- Temporary-Human hotkey: resolve the refresh trigger + hook the per-frame field update. ---
        scanner.AddMainModuleScan(GetFieldSystemSig, result =>
        {
            if (!result.Found) { _logger.WriteLine($"[{_modConfig.ModId}] GetFieldSystem sig not found; instant hotkey refresh disabled."); return; }
            var addr = (nint)((long)Process.GetCurrentProcess().MainModule!.BaseAddress + result.Offset);
            _getFieldSystem = Marshal.GetDelegateForFunctionPointer<GetFieldSystemFn>(addr);
            _logger.WriteLine($"[{_modConfig.ModId}] Resolved GetFieldSystem @ 0x{addr:X}");
        });

        scanner.AddMainModuleScan(RefreshTriggerSig, result =>
        {
            if (!result.Found) { _logger.WriteLine($"[{_modConfig.ModId}] refresh-trigger sig not found; instant hotkey refresh disabled."); return; }
            var addr = (nint)((long)Process.GetCurrentProcess().MainModule!.BaseAddress + result.Offset);
            _refreshTrigger = Marshal.GetDelegateForFunctionPointer<RefreshTriggerFn>(addr);
            _logger.WriteLine($"[{_modConfig.ModId}] Resolved model refresh trigger @ 0x{addr:X}");
        });

        scanner.AddMainModuleScan(FieldUpdateSig, result =>
        {
            if (!result.Found) { _logger.WriteLine($"[{_modConfig.ModId}] field-update sig not found; instant hotkey refresh disabled."); return; }
            var addr = (long)Process.GetCurrentProcess().MainModule!.BaseAddress + result.Offset;
            // Assign before Activate: this hook fires per-frame immediately, so the field must be set
            // before the handler (which calls OriginalFunction) can run.
            _fieldUpdateHook = _hooks!.CreateHook<FieldUpdateFn>(FieldUpdateHook, addr);
            _fieldUpdateHook.Activate();
            _logger.WriteLine($"[{_modConfig.ModId}] Hooked field update (hotkey tick) @ 0x{addr:X}");
        });

        StartHotkeyPoll();
    }

    // Null-guard for the cutscene look-at/aim blend. Original null-derefs when the Digimon rig lacks
    // the expected part (v5==0 or *(v5+24)==0). Those are never null for a valid player rig, so this
    // only fires in the swapped-model case; we return a zeroed vector instead of crashing.
    private bool _blendGuardLogged;

    private unsafe nint LookAtBlendHook(nint a1, nint a2)
    {
        // Validate the exact pointer chain the original walks, using IsBadReadPtr so garbage/unmapped
        // (not just null) pointers are caught. Original:
        //   v3 = *(a2+8);  v5 = (v3 == *(a2+16)) ? 0 : *v3;
        //   p  = *(v5+24);  v6 = *(p+0xD8);   crash reads [v6+0x1B8] (movss @ 0x140222589).
        // v6 is null for a Digimon rig -> we bail (zeroed output) if ANY step is bad, incl. v6 itself.
        if (a1 != 0 && a2 != 0 && !IsBadReadPtr(a2 + 8, 16))
        {
            nint v3 = *(nint*)(a2 + 8);
            nint end = *(nint*)(a2 + 16);
            nint v5 = v3 == end ? 0 : (IsBadReadPtr(v3, 8) ? 0 : *(nint*)v3);

            bool bad = v5 == 0 || IsBadReadPtr(v5 + 24, 8);
            nint p = 0;
            if (!bad) { p = *(nint*)(v5 + 24); bad = p == 0 || IsBadReadPtr(p + 0xD8, 8); }
            if (!bad)
            {
                nint v6 = *(nint*)(p + 0xD8);
                bad = v6 == 0 || IsBadReadPtr(v6 + 0x190, 0x40); // fields read at +0x190..+0x1C4
            }

            if (bad)
            {
                *(long*)a1 = 0;
                *(long*)(a1 + 8) = 0;
                if (!_blendGuardLogged)
                {
                    _blendGuardLogged = true;
                    OutputDebugStringA($"[{_modConfig.ModId}] blend null-guard TRIGGERED (skipped crash)\n");
                    _logger.WriteLine($"[{_modConfig.ModId}] blend null-guard TRIGGERED");
                }
                return a1;
            }
        }
        return _blendHook!.OriginalFunction(a1, a2);
    }

    // --- Temporary-Human hotkey -----------------------------------------------------------------
    // Per-frame field update hook. We only use it as a game-thread tick: run the game's update, then
    // if a toggle queued a refresh, fire the game's own model refresh so the (now un/re-suppressed)
    // model reloads in place. Doing it AFTER the original returns keeps us out of the refresh call
    // tree the update itself uses, so there is no re-entrancy.
    private nint FieldUpdateHook(nint a1, float a2)
    {
        var ret = _fieldUpdateHook!.OriginalFunction(a1, a2);
        if (_pendingRefresh)
        {
            _pendingRefresh = false;
            TriggerModelRefresh();
        }
        return ret;
    }

    private unsafe void TriggerModelRefresh()
    {
        var getFs = _getFieldSystem;
        var refresh = _refreshTrigger;
        if (getFs == null || refresh == null) return; // sigs not resolved -> reverts on next natural refresh
        try
        {
            nint fs = getFs();
            if (fs == 0 || IsBadReadPtr(fs + OffFieldManagerSlot, 8)) return;
            nint slot = *(nint*)(fs + OffFieldManagerSlot);
            if (slot == 0) return;
            refresh(slot);
        }
        catch { /* never take down the game thread over a refresh */ }
    }

    private void StartHotkeyPoll()
    {
        _pollThread = new Thread(HotkeyPollLoop) { IsBackground = true, Name = "PMS-hotkey" };
        _pollThread.Start();
    }

    // Background poll: on each fresh press of the configured key (only while a Digimon is selected),
    // flip the suppress flag and queue a game-thread refresh. Setting flags is all this thread does;
    // the actual game call happens on the field-update hook's thread.
    private void HotkeyPollLoop()
    {
        bool prevDown = false;
        while (true) // background thread; ends with the process (mod is CanUnload=false)
        {
            int vk = (int)_config.TemporaryHumanKey;
            bool down = vk != 0 && (GetAsyncKeyState(vk) & 0x8000) != 0;
            if (down && !prevDown && _targetKey != 0)
            {
                _suppressHuman = !_suppressHuman;
                _pendingRefresh = true;
                _logger.WriteLine($"[{_modConfig.ModId}] temporary-human toggled -> {(_suppressHuman ? "HUMAN" : "Digimon")}");
            }
            prevDown = down;
            Thread.Sleep(25);
        }
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
    private static extern void OutputDebugStringA(string msg);

    [DllImport("kernel32.dll")]
    private static extern nint AddVectoredExceptionHandler(uint first, VectoredHandler handler);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
    private static extern nint GetModuleHandleA(string? name);

    [DllImport("kernel32.dll")]
    private static extern bool IsBadReadPtr(nint lp, nuint ucb);

    private delegate int VectoredHandler(nint exceptionInfo);
    private VectoredHandler? _veh; // keep alive against GC
    private nint _gameBase;

    // Logs the faulting address of access violations as "game+0xOFFSET" (IDA addr = 0x140000000 + off).
    // Observe-only: returns EXCEPTION_CONTINUE_SEARCH so normal crash handling still runs.
    private void InstallCrashLogger()
    {
        _gameBase = GetModuleHandleA(null);
        _veh = Veh;
        AddVectoredExceptionHandler(1, _veh);
        OutputDebugStringA($"[{_modConfig.ModId}] crash logger installed; game base=0x{_gameBase:X}\n");
    }

    private unsafe int Veh(nint info)
    {
        // EXCEPTION_POINTERS: +0 = PEXCEPTION_RECORD
        var rec = *(nint*)info;
        uint code = *(uint*)rec;                 // EXCEPTION_RECORD.ExceptionCode
        if (code == 0xC0000005u)                 // EXCEPTION_ACCESS_VIOLATION
        {
            var addr = *(nint*)(rec + 16);       // EXCEPTION_RECORD.ExceptionAddress
            long off = (long)addr - (long)_gameBase;
            OutputDebugStringA($"[{_modConfig.ModId}] ACCESS_VIOLATION at game+0x{off:X} (ida 0x{0x140000000L + off:X})\n");
        }
        return 0; // EXCEPTION_CONTINUE_SEARCH
    }

    private readonly HashSet<string> _logged = new();

    // FieldPlayerSystem field offsets (from FieldPlayer_SetChangeModelId @ 0x1409AD4E0 and the
    // avatar path in FieldPlayer_ResolveModelRef @ 0x1409ADEE0):
    //   +464 (int)  = avatar model id (player_avatar_model key; costume/gender)
    //   +480 (int)  = change-model id (player_change_model key)
    //   +477 (byte) = "model changed" flag
    private const int OffAvatarId = 464;
    private const int OffChangeId = 480;
    private const int OffChangedFlag = 477;

    private unsafe void ResolveModelHook(nint self, nint outStr, nint outRef, uint arg4)
    {
        var key = _targetKey;
        var s = (byte*)self;

        byte origFlag = self != 0 ? *(s + OffChangedFlag) : (byte)0;
        int origId = self != 0 ? *(int*)(s + OffChangeId) : 0;
        int avatarId = self != 0 ? *(int*)(s + OffAvatarId) : 0;

        // (1) Unforced resolve: see what the game wants for this call.
        _hook!.OriginalFunction(self, outStr, outRef, arg4);
        var want = ReadStdString(outStr) ?? "";

        if (_logged.Add(want))
        {
            var line = $"[{_modConfig.ModId}] resolve want=\"{want}\" flag={origFlag} id={origId} avatar={avatarId} arg4={arg4} ref={(outRef == 0 ? 0 : 1)}";
            OutputDebugStringA(line + "\n");
            _logger.WriteLine(line);
        }

        // (2) Apply the swap only when the game is NOT already driving its own change-model
        //     (origFlag == 0 = normal field player). Cutscenes that set a specific player model
        //     (flag == 1) are left untouched to avoid the crash. Transiently set the change-model
        //     so BOTH the output string and the ref come from player_change_model[key] consistently,
        //     then restore (nothing persists -> save-safe).
        if (key != 0 && !_suppressHuman && self != 0 && origFlag == 0)
        {
            *(int*)(s + OffChangeId) = key;
            *(s + OffChangedFlag) = 1;
            _hook!.OriginalFunction(self, outStr, outRef, arg4);
            *(s + OffChangedFlag) = origFlag;
            *(int*)(s + OffChangeId) = origId;
        }
    }

    private static unsafe string? ReadStdString(nint p)
    {
        if (p == 0) return null;
        var b = (byte*)p;
        ulong size = *(ulong*)(b + 16);
        ulong cap = *(ulong*)(b + 24);
        if (size > 0x1000) return null;
        byte* data = cap > 15 ? (byte*)*(nint*)b : b;
        return data == null ? null : Marshal.PtrToStringAnsi((nint)data, (int)size);
    }

    private void UpdateTarget()
    {
        var id = (int)_config.PlayerDigimon;
        _targetKey = id != 0 ? 90000 + id : 0;
        _suppressHuman = false; // a config change gives a clean state: render exactly what's selected
        _logged.Clear();
    }

    public override void ConfigurationUpdated(Config configuration)
    {
        _config = configuration;
        UpdateTarget();
        _logger.WriteLine($"[{_modConfig.ModId}] Player Digimon = {_config.PlayerDigimon} (key {_targetKey})");
    }

#pragma warning disable CS8618
    public Mod() { }
#pragma warning restore CS8618
}
