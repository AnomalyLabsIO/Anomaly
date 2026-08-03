# Anomaly — Server-Driven Lua on the IL2CPP Client (MoonSharp / SCriPt Feasibility)

**Date:** 2026-06-24
**Author:** Claude (Opus 4.8) + 3 parallel deep-dive investigation agents
**Question:** Can MoonSharp (the pure-C# Lua interpreter) — and the architecture of the existing server-side **SCriPt** plugin — be adopted to let the Anomaly **server push custom client-side logic as Lua text** to the IL2CPP game client? What are the IL2CPP support limits that could be bottlenecks?
**Status:** Investigation only — **no code changes were made.** This is a findings/design document.
**Sources investigated:**
- `E:\Projects\AnomalyLabs\Anomaly` — the MelonLoader + Il2CppInterop client mod (target environment)
- `E:\Projects\SCPSL\SCriPt` — the existing server-side MoonSharp Lua plugin (reuse source)
- `E:\Projects\moonsharp-ref\moonsharp` — MoonSharp 3.0.0.0 reference tree (capability/limit source)

---

## TL;DR — Verdict

**Feasible, and meaningfully easier than the "IL2CPP" framing implies.** The single most important finding reframes the whole risk model:

> **MoonSharp does NOT run under IL2CPP/AOT here.** The Anomaly mod (and any `MoonSharp.dll` it loads) runs in **MelonLoader's net6 CoreCLR domain with a full JIT** (`MelonLoader.runtimeconfig.json` → `Microsoft.NETCore.App 6.0.0`). IL2CPP/AOT constrains only the *game's* code; the mod layer is ordinary managed code. So MoonSharp's interpreter, its `Expression.Compile()` interop optimizer, `Reflection.Emit`, and even build-time-AOT escape hatches like *hardwiring* are **not required for correctness** — they all work, because there is a JIT.

What that means in practice:

| Concern (the usual IL2CPP worry) | Reality here | Severity |
|---|---|---|
| "Reflection.Emit / Expression.Compile crashes on AOT" | Runs on JIT'd CoreCLR — works fine. MoonSharp also has **zero** `Reflection.Emit` anyway. | **Non-issue** |
| "Need to run the MoonSharp Hardwire build pipeline for AOT" | Not needed — it's a *performance* option for true-AOT targets, not a correctness requirement here. | **Non-issue** |
| "MoonSharp can't reflect over types" | It reflects fine over **plain managed types**. | **Non-issue** |
| MoonSharp reflecting over **Il2CppInterop proxy types** (the real game objects) | Genuine sharp edges (unusual signatures, non-blittable structs, `ReadOnlySpan`/`Encoding` marshaling traps). | **The actual bottleneck** — design around it with a façade (below). |
| Security of running server-pushed code on the player's machine | RCE-by-design; the sandbox is the *only* defense. SCriPt's layered sandbox ports over and is essential. | **The other big one** |

**Recommended architecture in one line:** embed MoonSharp in `Anomaly.Client.Api`, deliver scripts over the existing encrypted `0xFE` `AnomalyMessaging` channel (or the SHA-256 `FileTransfer` pipeline), execute on the Unity main thread inside the existing receive handler, and **never expose raw Il2Cpp proxy types to Lua** — expose hand-written plain-C# façade classes (exactly as SCriPt exposes `LabApi.Features.Wrappers.*` instead of raw game types). Reuse SCriPt's engine/sandbox core almost verbatim; rewrite only the API façade against client APIs.

---

## 1. The reframing in detail: where MoonSharp actually runs

This is load-bearing, so it's worth being precise.

MelonLoader supports two backends for an IL2CPP game; this install uses the **net6** backend:
- `E:\Projects\AnomalyLabs\Client\MelonLoader\net6\` contains `MelonLoader.dll`, `Il2CppInterop.Runtime.dll`, `0Harmony.dll`, and `MelonLoader.runtimeconfig.json`.
- `MelonLoader.runtimeconfig.json`:
  ```json
  { "runtimeOptions": { "tfm": "net6.0",
      "framework": { "name": "Microsoft.NETCore.App", "version": "6.0.0" },
      "configProperties": {
        "System.Globalization.Invariant": true,
        "System.Reflection.Metadata.MetadataUpdater.IsSupported": false } } }
  ```
- `Anomaly.Client` and `Anomaly.Client.Api` both target **`net6.0`** (`x64`), referencing `MelonLoader/net6/*` and the generated `Il2Cpp*` proxy assemblies under `MelonLoader/Il2CppAssemblies/`.

**Il2CppInterop's entire purpose** is to let you write normal JIT-compiled managed code that bridges into the AOT-compiled (C++) game via generated proxy classes (`Il2CppMirror.NetworkClient`, etc.) that marshal through `IntPtr` handles. So:

```
┌─────────────────────────────────────────────────────────────┐
│  IL2CPP game (AOT / C++)   ← the only AOT-constrained layer  │
└───────────────▲─────────────────────────────────────────────┘
                │  Il2CppInterop proxy types (IntPtr-marshalled)
┌───────────────┴─────────────────────────────────────────────┐
│  MelonLoader net6 CoreCLR domain  (FULL JIT)                 │
│   • Anomaly.Client / Anomaly.Client.Api  (net6.0)           │
│   • MoonSharp.Interpreter.dll   ← runs HERE, with a JIT      │
└─────────────────────────────────────────────────────────────┘
```

`Reflection.Emit`, `Expression.Compile`, `MethodInfo.Invoke`, generic instantiation — all available in the CoreCLR domain. The `MetadataUpdater.IsSupported:false` flag only disables Edit-and-Continue hot-reload, not codegen. **MoonSharp's "AOT limitations" simply do not apply to MoonSharp's own execution.** They re-appear only when MoonSharp's interop reaches *across the proxy boundary* — which we control by choosing what to expose.

> Caveat worth a one-line spike: even the `Mono` backend MelonLoader ships (`MelonLoader\Mono\`) has a full JIT, so the conclusion holds on either backend. Neither runs the mod through IL2CPP.

---

## 2. MoonSharp capability & limit analysis (ref tree = v3.0.0.0)

Findings from `E:\Projects\moonsharp-ref\moonsharp`. Version: product `3.0.0.0` (`src/MoonSharp.Interpreter/Script.cs:28`), Lua 5.2, TFMs `netstandard2.0;net45` (`MoonSharp.Interpreter.csproj:4`). This is the canonical **xanathar/moonsharp** line (strong-name key matches SCriPt's vendored DLL — see §4). `netstandard2.0` is consumable by `net6.0`, so the client can reference it directly.

### 2.1 No `Reflection.Emit` anywhere; one optional codegen path
- Tree-wide grep: **zero** `Reflection.Emit` / `DynamicMethod` / `ILGenerator` in the interpreter. The `Emit(` hits are MoonSharp's own **bytecode** emitter, not IL.
- The **only** runtime codegen is `System.Linq.Expressions … .Compile()`, used solely to build fast-path accessors for **CLR interop** (read field/property, call method) on registered types:
  - `Interop/StandardDescriptors/ReflectionMemberDescriptors/PropertyMemberDescriptor.cs` (`OptimizeGetter/Setter`)
  - `…/FieldMemberDescriptor.cs` (`OptimizeGetter`; field setter is always reflection)
  - `…/MethodMemberDescriptor.cs` (`Optimize`; constructors & by-ref params always reflection)
- Every such descriptor checks `Platform.IsRunningOnAOT()` in its ctor and **downgrades to pure `MethodInfo.Invoke`/`FieldInfo.GetValue` reflection** if AOT. So on a true-AOT target it self-neuters to reflection; on our JIT target it uses the compiled fast path. **Both are safe for us** — the only question is *what types* it compiles against (§3).

### 2.2 AOT detection (`Platforms/PlatformAutoDetector.cs`)
```csharp
public static bool IsRunningOnAOT { get {
#if UNITY_WEBGL || UNITY_IOS || UNITY_TVOS || ENABLE_IL2CPP
    return true;                                  // compile-time, true AOT builds
#else
    try { Expression.Lambda<Func<int>>(Expression.Constant(5)).Compile();   // JIT probe
          m_IsRunningOnAOT = false; }
    catch { m_IsRunningOnAOT = true; }
    return m_IsRunningOnAOT.Value;
#endif
}}
```
- A **prebuilt** MoonSharp DLL (our case — the `ENABLE_IL2CPP`/`UNITY_*` symbols are not defined when the DLL was compiled) runs the **runtime probe**, which **succeeds** (we have a JIT) ⇒ `IsRunningOnAOT == false` ⇒ optimizer stays on.
- `IsRunningOnUnity` is set by scanning loaded assemblies for any `UnityEngine.*` type. Under Il2CppInterop those proxy assemblies **are** loaded ⇒ `IsRunningOnUnity == true` ⇒ MoonSharp selects `LimitedPlatformAccessor` (strips `io`/`os` — good) and `UnityAssetsScriptLoader` (reads from Unity `Resources` — you'll want to replace this with a custom loader; see §6).
- `IsUnityIL2CPP` is compile-time only (`#if UNITY_5 && ENABLE_IL2CPP`) ⇒ **always false** here. MoonSharp will not "know" it's adjacent to IL2CPP. That's fine given the reframing.
- One-time cost: that first-`Script` assembly/type scan iterates `GetTypes()` over the large Il2CppInterop proxy set once. Minor, but real — do it at load, not mid-round.

### 2.3 Escape hatches you can use but don't strictly need
- **Interop access modes** (`Interop/InteropAccessMode.cs`): `Reflection` (always-safe, slow), `LazyOptimized` (default; compile-on-first-use), `Preoptimized`, `Hardwired` (pre-generated, reflection-free), `HideMembers`, `NoReflectionAllowed`. Global lever: `UserData.DefaultAccessMode` (default `LazyOptimized`, `DataTypes/UserData.cs:53`) and the `CallbackFunction.DefaultAccessMode` twin.
- **Force reflection** (belt-and-suspenders) with `UserData.DefaultAccessMode = InteropAccessMode.Reflection;` or a custom `Script.GlobalOptions.Platform` whose `IsRunningOnAOT()` returns `true`. **Only needed if you can't guarantee façade purity (§3).** Costs per-call interop speed.
- **Hardwiring** (`src/MoonSharp.Hardwire`, CodeDOM-based, build-time): generates reflection-free descriptor `.cs`. This is the *true-AOT* answer and a *performance* lever here — **not a correctness requirement**, because we have a JIT. Skip it for v1; revisit only if reflection-mode interop becomes a per-frame hotspot.

### 2.4 Standard library / sandbox (`Modules/CoreModules.cs`)
- `[Flags]` presets: `Preset_HardSandbox` (Basic/String/Table/Math/Bit32/consts — **no io/os/load**), `Preset_SoftSandbox` (+ metatables, errors, coroutine, `os.time`, dynamic, json), `Preset_Default`/`Preset_Complete` (+ `LoadMethods`/`OS_System`/`IO` — "unlimited system access", avoid).
- `io` and `os.execute/remove/exit/getenv` route through `IPlatformAccessor`; under the Unity-selected `LimitedPlatformAccessor` they're **stripped and throw** (`FilterSupportedCoreModules` removes `IO | OS_System`). `os.time/clock/date` remain (pure managed).
- `load`/`loadstring`/`dynamic.eval` compile Lua *source → MoonSharp bytecode* (pure interpreter, **no CLR codegen**) — AOT-safe, but a sandbox-policy decision (lets a script compile more script).
- MoonSharp coroutines are **VM-level**, not OS threads (deterministic, AOT-safe) unless you explicitly opt into thread-backed coroutines.
- **Recommendation:** construct with `CoreModules.Preset_HardSandbox` (or SoftSandbox if you want coroutines/json/metatables) and treat `LoadMethods` as off by default.

### 2.5 Performance shape
- Parse → custom **bytecode VM** (register/stack processor). Fine for per-frame use **if you compile a script once and re-invoke its closures** — never `DoString` every frame.
- `Script.WarmUp()` primes the lexer/parser statics; call once at load.
- GC: `DynValue` is a class; table ops and CLR↔Lua marshalling box value types. For frame-rate-sensitive paths, pass primitives, reuse tables, batch interop into single callbacks, and avoid `dynamic.eval` in hot loops.

---

## 3. The actual bottleneck: the MoonSharp ↔ Il2Cpp proxy boundary

Everything genuinely risky lives here, and it's **avoidable by design**.

If you let MoonSharp **auto-register and reflect over raw Il2Cpp proxy types** (e.g. expose `Il2CppMirror.NetworkClient` or a raw `ReferenceHub`-equivalent directly to Lua), you inherit every IL2CPP marshaling sharp edge the codebase already documents:

1. **`ReadOnlySpan<T>.GetPinnableReference` is un-exported ⇒ `MissingMethodException`.** Any path that round-trips `Span<T>` through `Il2CppSystem` dies. Documented at `Anomaly.Client.Api/Assets/PngLoader.cs:13-21` and `AudioClipFactory.cs:16-28` (they went **fully pure-managed** — BigGustave/NVorbis — to dodge it).
2. **Il2CppInterop rejects delegates with non-blittable Il2Cpp-struct params** (`Anomaly.Client/Patches/Mirror/EncryptedChannelHandlerFix.cs:18-32`); the workaround is to invoke via `System.Reflection` `MethodInfo.Invoke` with boxed args. MoonSharp's `Expression.Compile()` accessors against such signatures are exactly the kind of binding that could marshal oddly.
3. **`Encoding`/array ambiguity** between `System.*` and `Il2CppSystem.*` silently corrupts data (`PlayerAuthenticationManagerPatch.cs:105-114`).
4. Proxy types have **huge, unusual member surfaces** (`Il2CppReferenceArray<T>`, pointers, `Il2CppSystem.*` returns) — MoonSharp's `FillMemberList` enumerating them is both slow and edge-case-prone.

**The fix is the SCriPt pattern itself.** SCriPt never exposes raw base-game types to Lua — it exposes **`LabApi.Features.Wrappers.*`**, which are plain managed wrapper classes. Mirror that here:

> Expose **hand-written, plain-C# façade classes** (POCOs in `Anomaly.Client.Api`) to MoonSharp. Each façade method does the Il2Cpp boundary work in hand-written, tested C# (using the codebase's existing value-type-safe patterns), and returns plain managed values. MoonSharp then only ever reflects over **plain managed types** — zero IL2CPP involvement in the interop layer, `LazyOptimized`/`Expression.Compile` is safe *and* fast, and all the marshaling traps are confined to code you control.

With façades, the proxy-boundary bottleneck disappears for MoonSharp and reduces to "write good wrapper classes," which the team already does for assets/audio/networking.

**#1 thing to validate in a spike:** register one plain façade exposing ~3 members that internally call the live game (e.g. `Local.SendHint(string)`, `Local.Position` getter returning a plain `(x,y,z)`), confirm `LazyOptimized` Expression-compiled access works against it under the running client, and confirm no `MissingMethodException`/marshaling corruption. If anything misbehaves, flip `UserData.DefaultAccessMode = Reflection` and re-test — that isolates "MoonSharp optimizer vs proxy" from "façade bug."

---

## 4. Reusing SCriPt — what ports as-is vs. what's rewritten

SCriPt (`E:\Projects\SCPSL\SCriPt`, net4.8, LabAPI plugin) embeds a **vendored** `MoonSharp.Interpreter.dll` — official strong-name key `921e73ce94aa17f8`, internal version `3.0.0.0`, i.e. **the same lineage as the reference tree**. Scripts run on a `ScriptHandler : Script` subclass and can be executed **from text** (`Script.DoString` / `ReplInterpreter.Evaluate`, driven by `ScriptLoader.ExecuteConsole(string)` from `Commands/LuaExecuteCommand.cs`). That "receive text → execute" path is exactly what a server-push client feature needs.

The codebase splits cleanly into a **game-agnostic engine/sandbox core** and a **game-bound API façade**:

### Ports almost verbatim (depends only on MoonSharp + BCL)
| File | Role |
|---|---|
| `API/Lua/SafeUserDataDescriptor.cs` | Conflict-tolerant descriptor; **hides `GetType()`** from Lua (critical sandbox barrier) |
| `API/Lua/AutoRegistrationPolicy.cs` | First-use auto-registration, swapping in the safe descriptor |
| `Utils/FrameworkTypes.cs` | Deny-list (`mscorlib`, `System`, `Microsoft`, `Mono.`, `MoonSharp`, …) — pure assembly-name predicate |
| `API/Lua/LibScriptLoader.cs` | Containment-rooted loader; rejects `..`/absolute traversal, ignores `LUA_PATH` |
| The **registration pattern** in `Handlers/ScriptLoader.cs` | "reflect a namespace → bulk-register wrappers + enums → register converters/extensions" — API-agnostic machinery |
| The **event bridge** (`Handlers/EventHandlers/LuaEvent.cs`, `DynamicLuaEventHandler.cs`) | Generic over `EventInfo` via `Delegate.CreateDelegate`; auto-unsubscribes a script's callbacks on unload. Re-aim at client event classes. |

### Must be rewritten against client APIs
- Every `API/Lua/Globals/Global*.cs` façade is bound to **server** types (`LabApi.Features.Wrappers.Player`, `Round`, `Warhead`, `Cassie`, `DecontaminationController`, …). These are the surface you re-author as **client façades** (local player, local UI/hints, client-side effects, camera, input, received-asset handles, etc.).
- `RegisterLabApiWrappers/Enums` and `Plugin.RegisterDynamicEvents` are anchored to the server LabAPI assembly/namespaces — re-anchor the reflective scans at the client API assembly.
- Server-only concepts (`LuaCustomCommand` RemoteAdmin, `GlobalSettings` Server-Specific-Settings) drop or get client analogues.
- Coroutine layer uses MEC `Timing`; on the client, back it with the existing `Scheduler` (§6) instead.

**Net:** keep the ~6 core files and the reflective-registration + event-bridge machinery; rewrite the `Globals/` folder against a curated client façade. There is no formal `ILuaApi` seam in SCriPt — the globals call game statics directly — but the engine/sandbox half only knows MoonSharp + a deny-list, so it lifts cleanly. Consider introducing the thin seam during the port.

### Sandbox model to carry over (and harden — §7)
SCriPt's layered, config-driven access control (`Config.FullAccess` default false) is the blueprint:
1. `CoreModules` soft-sandbox (no `io`/`os`/`dynamic`),
2. nil out `dofile`/`loadfile`/`load`/`loadsafe` so scripts can't compile/run arbitrary code,
3. `LimitedPlatformAccessor` (denies fs/process at the platform layer),
4. containment-rooted `require` loader,
5. framework deny-list + `GetType()` hidden ⇒ scripts can't walk from a game object back into `System.*`,
6. coroutine `AutoYieldCounter` pre-emption (default 100000) so an infinite loop force-suspends instead of hanging the host.
Gap noted in SCriPt: **no wall-clock timeout or memory cap** beyond the per-coroutine op counter — see §7.

---

## 5. Delivery & execution wiring (already exists in Anomaly)

A server-push Lua feature reuses infrastructure that's already built — it does **not** need new transport.

**Inline scripts (small):** the encrypted custom channel.
- `0xFE` `EncryptedChannel`, `AnomalyProtocol.CurrentVersion = 1`, AES-GCM (BouncyCastle), replay-protected. Add an `IAnomalyMessage` (e.g. `anomaly:lua:exec` / `anomaly:lua:load`) in `Anomaly.Shared/Networking/`, register it in `AnomalyMessageRegistry.RegisterDefaults()` (FNV-1a-32 name hash; client+server share the Shared assembly so hashes agree).
- Send/receive entry points are mod-facing: `Anomaly.Client.Api/Networking/AnomalyMessaging.cs` — `Send(IAnomalyMessage)` and `event Action<IAnomalyMessage> MessageReceived`.

**Script files (larger / cached):** the content-addressed transfer pipeline.
- `FileManifestMessage`/`FileRequestMessage`/`FileChunkMessage` + `Anomaly.Client/FileTransfer/FileTransferClient.cs` + `Anomaly.Client.Api/FileTransfer/FileCache.cs` (SHA-256 content-addressed, LRU 500 MB). Server-pushed `.lua` files arrive and cache exactly like PNG/OGG overrides; server is authoritative over which files the client fetches.

**Threading — the easy part:** the receive path runs **on the Unity main thread**. `EncryptedChannelManager.ClientReceivePackedMessage` is a Mirror handler dispatched synchronously during the client network update (Harmony prefix at `Anomaly.Client/Patches/Auth/PlayerAuthenticationManagerPatch.cs:168-303`), which fires `AnomalyMessaging.MessageReceived`. So **Lua can be executed directly in the `MessageReceived` handler and call game APIs immediately — no cross-thread marshaling queue.** For deferred/periodic Lua work use the existing main-thread `Anomaly.Client.Api/Scheduling/Scheduler.cs` (`In`/`Every`, ticked from `Core.OnUpdate` → `ClientRuntime.Update`, auto-cleared on disconnect/scene-load).

**Shipping `MoonSharp.Interpreter.dll`:** it's pure-managed, zero native deps — drop it in `UserLibs/` via the established pattern in `Anomaly.Client.Api.csproj` (PackageReference + `CopyLocalLockFileAssemblies=true` + an `AfterTargets="Build"` `<Copy>` to `$(AnomalyClientUserLibsDir)`, just like `NVorbis`/`BigGustave`/`YamlDotNet`). `Anomaly.Client.Api` is the natural host. Remember `EnableDefaultCompileItems=false` on `Anomaly.Client` — new `.cs` must be added to the compile list (see the `feedback_git_add_new_files` convention).

---

## 6. The `UnityAssetsScriptLoader` wrinkle

Because `IsRunningOnUnity` resolves **true**, MoonSharp defaults its `Script.Options.ScriptLoader` to `UnityAssetsScriptLoader`, which reads modules from Unity `Resources` — not what you want. Supply a **custom `IScriptLoader`** (port `LibScriptLoader` concept) that resolves `require()` against the `FileCache`/a curated in-memory module table, or disable `LoadMethods` entirely and forbid `require` in v1. This is a deliberate config step, not a blocker.

---

## 7. Security & trust model (the part that matters most on the client)

**Server-pushed code that executes on the player's machine is remote code execution by design.** On the server side, SCriPt's scripts are authored by the server owner running them — a trust-yourself model. On the **client**, the trust direction inverts: a player connects to an arbitrary server, and that server ships code that runs on the player's PC, **inside a full-JIT CoreCLR domain with reflection available and Il2CppInterop bridging to the entire game and engine.** The sandbox is the *only* thing between a malicious server and the player.

Consequences for design:
- **The sandbox is non-negotiable and must be rigorous.** Port SCriPt's full stack (§4.3) and treat `FullAccess`-equivalent as **non-existent** on the client. No `io`/`os`/`load`/`require`-from-arbitrary-path, ever.
- **`GetType()` hiding + framework deny-list are critical**, more so than on the server: without them, one reflection hop reaches `System.Reflection`/`System.Diagnostics.Process`/`System.IO` and it's game over. Audit that **no façade object transitively exposes** a `System.Type`, an `Assembly`, a delegate to a BCL method, or a raw Il2Cpp object that could be reflected into the runtime.
- **Add the limits SCriPt lacks:** a wall-clock execution budget per message (not just the coroutine op-counter) and a guard against allocation blow-ups, so a hostile script can't freeze or OOM the client.
- **Curate the façade as an allowlist, not a denylist.** Expose only the specific client capabilities a server should be able to drive (hints/UI, cosmetic effects, sounds, local visual state). Do **not** expose filesystem, process, network egress, input injection, or anything that could grief/deanonymize the player. Mind privacy (don't let scripts read arbitrary local state) and anti-cheat/ToS implications of letting servers run code on clients.
- **Authenticity:** scripts already arrive over the AES-GCM, replay-protected `0xFE` channel keyed by the Anomaly auth handshake, so a script is at least provably from the connected server — but that server may itself be hostile. The sandbox, not the transport, is the trust boundary.

Recommend a short written **threat model + capability allowlist** before exposing anything beyond a no-op spike.

---

## 8. Why Lua/MoonSharp over the alternatives (affirming the premise)

- **vs. ScriptableObjects (the stated alternative):** SOs are Unity *assets* — they'd need AssetBundle packaging and can only carry **data + references to behaviors that already exist in the client**. They can't express genuinely new logic as arbitrary runtime text, and they don't give a capability sandbox. Lua's selling points — *send as text* and *control the level of system access* — are real and not replicated by SOs.
- **vs. NLua / KeraLua:** these wrap **native** liblua; you'd need a native lib loaded and P/Invoked from the Il2CppInterop domain — strictly harder to ship and sandbox than a pure-managed DLL. MoonSharp being **100% managed, zero native deps** is a major fit advantage here.
- **vs. XLua:** designed for *Unity-IL2CPP-native* projects with a build-time codegen step; it assumes you control the Unity build, which a MelonLoader mod does not. Poor fit for this managed-mod model.
- **vs. C#/Roslyn scripting:** far heavier, and *much* harder to sandbox safely than a curated Lua surface — wrong tool for "let a server push small, sandboxed behaviors."
- **vs. the planned custom-content C# framework** (`project_custom_content_framework`): that's server-authoritative *C#* with declarative presentation — complementary, not competing. Lua fits the "server ships small bespoke client *behaviors* as text" niche the C# catalog doesn't cover. They can coexist (Lua scripts could even drive catalog-defined content).

---

## 9. Recommended path (phased, no commitment beyond the spike)

1. **Spike (proves the only real unknown).** Ship `MoonSharp.Interpreter.dll` to `UserLibs/`; at client init call `Script.WarmUp()`, set `CoreModules.Preset_HardSandbox`, register **one plain-C# façade** with ~3 members that call the live game, and execute a hard-coded Lua string that drives them. Validate: (a) loads/runs in the net6 domain, (b) `LazyOptimized` Expression-compiled access works against the façade with no `MissingMethodException`, (c) flipping to `Reflection` mode also works. **This is the §3 validation.**
2. **Transport.** Add `anomaly:lua:exec` (inline) over `0xFE`; execute inside `AnomalyMessaging.MessageReceived` on the main thread.
3. **Port the sandbox core** (the 6 reusable SCriPt files) + custom `IScriptLoader`; add wall-clock/alloc limits; write the threat model + capability allowlist.
4. **Author the client façade** (`Globals/` analogues) against the curated allowlist; re-aim the reflective event bridge at client events.
5. **Lifecycle:** tie script load/unload to round/scene events; auto-unsubscribe callbacks on unload (SCriPt's `LuaEvent.RemoveCallbacksForScript` pattern); back coroutines with `Scheduler`.

---

## 10. Open questions to resolve during the spike
- Does `Expression.Compile()`-based interop behave correctly against **plain façade** types in this exact net6+Il2CppInterop process? (Expected yes; verify, then decide LazyOptimized vs forced Reflection as the default.)
- Final `CoreModules` set — is `coroutine` (SoftSandbox) wanted in v1, or HardSandbox only?
- Per-message execution budget value and how to abort a runaway script mid-call (coroutine pre-emption covers loops; a hard time cap needs a watchdog).
- Module/`require` policy: forbid entirely in v1, or allow from a server-pushed, hash-verified module set via the `FileCache`?

---

### Key files referenced
**Anomaly (target):** `Anomaly.Client.Api/Networking/AnomalyMessaging.cs`, `Anomaly.Client/Patches/Auth/PlayerAuthenticationManagerPatch.cs:168-303`, `Anomaly.Shared/Networking/AnomalyMessageRegistry.cs`, `Anomaly.Client.Api/Scheduling/Scheduler.cs`, `Anomaly.Client.Api/FileTransfer/FileCache.cs`, `Anomaly.Client.Api/Assets/PngLoader.cs` + `AudioClipFactory.cs` (the canonical IL2CPP marshaling workarounds), `Anomaly.Client.Api/Anomaly.Client.Api.csproj` (UserLibs ship pattern), `Client/MelonLoader/net6/MelonLoader.runtimeconfig.json` (the runtime proof).
**SCriPt (reuse):** `API/Lua/{SafeUserDataDescriptor,AutoRegistrationPolicy,LibScriptLoader}.cs`, `Utils/FrameworkTypes.cs`, `Handlers/ScriptLoader.cs`, `API/Lua/Objects/ScriptHandler.cs`, `Handlers/EventHandlers/{LuaEvent,DynamicLuaEventHandler}.cs`, `Config.cs`, `API/Lua/Globals/Global*.cs`.
**MoonSharp (limits):** `Platforms/PlatformAutoDetector.cs`, `Platforms/LimitedPlatformAccessor.cs`, `Interop/InteropAccessMode.cs`, `Interop/StandardDescriptors/ReflectionMemberDescriptors/{Method,Field,Property}MemberDescriptor.cs`, `DataTypes/UserData.cs:53`, `ScriptGlobalOptions.cs:29`, `Modules/CoreModules.cs`, `src/MoonSharp.Hardwire/` (unused-but-available escape hatch).
