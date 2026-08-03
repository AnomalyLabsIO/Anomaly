# Threat Model — SCriPt MoonSharp Lua Sandbox (current implementation)

**Date:** 2026-06-24
**Author:** Claude (Opus 4.8) + 2 adversarial review agents; key findings re-verified by hand against the source.
**Subject:** `E:\Projects\SCPSL\SCriPt` — the server-side MoonSharp Lua plugin.
**Purpose:** Reconnaissance for the client-side Lua port (see [LUA_CLIENT_SCRIPTING_INVESTIGATION_2026-06-24.md](LUA_CLIENT_SCRIPTING_INVESTIGATION_2026-06-24.md)). Documents SCriPt's security posture so we know exactly which controls are load-bearing, which are "safe only because we trust the author," and what must be rebuilt when scripts arrive from an **untrusted remote server**.
**Status:** Analysis only — no code changed. Two findings here are arguably bugs in SCriPt *under its own trust model* and worth fixing upstream regardless of the port (flagged ⚠️ SCRIPT-BUG).

---

## 0. The one thing to internalize

> **SCriPt's sandbox reliably blocks the classic MoonSharp escape — reflection into the BCL (`Assembly.Load` → `Activator`/`MethodInfo.Invoke` → arbitrary .NET). That boundary is well-built and the review could not break it.**
>
> **It does NOT contain capability that already lives in non-framework assemblies.** The deny-list is by *assembly name prefix* (`System.*`, `mscorlib`, `Microsoft.*`, `Mono.`, `MoonSharp`). Everything in `Assembly-CSharp`, `UnityEngine.*`, and LabAPI is "not framework" and therefore fully reachable by a *sandboxed* script — including command execution, file I/O, HTTP, and permission mutation.

This is fine under SCriPt's actual trust model (**the server owner writes the scripts they run** — they already have full server control). It is a **complete compromise** the moment a script comes from someone you don't trust. The client port is exactly that scenario, so this document is mostly a map of "controls that silently depend on author-trust."

---

## 1. Trust model, assets, boundaries

**Intended trust model (server):** Scripts are authored by the server operator (or installed by them from a source they chose). `Config.FullAccess` (default `false`) is a *convenience* throttle to reduce footguns / sandbox semi-trusted snippets — not a hard security boundary against a hostile author. An operator who wants full power flips `FullAccess=true`.

**Assets at risk** (ordered): the host machine (filesystem, processes, network), server integrity (admin/permissions, bans, config), other players (state, bans, kicks, PII like IP), service availability (no hangs/OOM/crashes), and per-script data integrity.

**Trust boundary:** the Lua VM. Untrusted *input* is the Lua source text plus anything a script reads (player input, settings values). Trusted side is the .NET host (full server authority). Everything in this doc is about what crosses that VM boundary.

**Entry points where Lua executes** (each is an attack surface): top-level script load (`DoFile`), the REPL (`script execute …` console/chat command), event callbacks, custom-command handlers, **Server-Specific-Settings callbacks (client-triggerable!)**, module `load`/`unload`, and scheduled coroutines/timing closures.

---

## 2. Findings at a glance

| # | Vector | Verdict | Server-trust impact | Untrusted-source (client) impact |
|---|---|---|---|---|
| 1 | Reflection → BCL runtime (via `GetType`) | **BLOCKED** ✅ | — | Boundary holds; preserve it |
| 2 | In-process capability surface (non-framework statics) | **OPEN** (by design) | Acceptable (author already has power) | **Critical — full host/server compromise** |
| 3 | Auto-registration navigation to game types | **OPEN** (by design) | Acceptable | Critical (permission self-escalation, etc.) |
| 4 | Code-compilation escape (`load`/`dynamic`) | **BLOCKED** ✅ | — | Holds; `dynamic.eval` provably can't touch CLR |
| 5 | Lua-stdlib filesystem/process/env + path traversal | **BLOCKED** ✅ | — | Holds (stdlib only — see #2 for the .NET route) |
| 6a | `Admin.LACommand` ungated command exec | **OPEN** ⚠️ SCRIPT-BUG | Gate bypass even server-side | Critical |
| 6b | Raw `HttpQuery` bypasses `GlobalHttp` allowlist | **OPEN** | Egress control is illusory | Critical (SSRF/exfil) |
| 6c | `Data` store: path traversal + no isolation | **PARTIAL** ⚠️ SCRIPT-BUG | Cross-script tamper; `.json` write-anywhere | Critical |
| 7 | Cross-script isolation (global registry/data/commands) | **OPEN** | Acceptable (cooperative) | Problem when mixing trust levels |
| 8 | DoS — no time/memory limit on main path | **OPEN** | Self-inflicted hang/OOM | **Critical — any server can freeze/OOM/crash every client** |

The **OPEN by design** rows (2, 3, 7) and the **DoS** row (8) are the ones that flip from "fine" to "fatal" for the port. Rows 6a/6c are worth fixing in SCriPt now.

---

## 3. What's solid (invariants to preserve in the port)

These were attacked and held — the port must not regress them:

- **3.1 Reflection boundary (Vector 1) — BLOCKED.** `SafeUserDataDescriptor.FillMemberList` skips `GetType` for all reference types (`SafeUserDataDescriptor.cs:60`), applied uniformly because `AutoRegistrationPolicy.HandleRegistration` swaps every auto-registered external type to `SafeUserDataDescriptor` (`AutoRegistrationPolicy.cs:38-42`). Crucially, the *return-value* conversion path also enforces the deny-list: any property/method that returns a `System.Reflection.Assembly`/`MethodInfo`/`MemberInfo` is refused by `UserData.Create`→`AllowTypeAutoRegistration` (`= !FrameworkTypes.IsFramework`) and raises a runtime error instead of exposing the object (`ClrToScriptConversions.cs:100-147`). The one special case — a returned `System.Type` → `UserData.CreateStatic` — re-applies the deny-list to the target and yields only *static members*, not reflection; you cannot pivot from a `System.Type` value back to `.Assembly`/`.GetMethod()` because those are instance members of a framework type. **Net: no path from any exposed object to `Assembly.Load`/`Activator`/`MethodInfo.Invoke`.**
- **3.2 `dynamic.eval` cannot touch CLR (Vector 4).** `Dynamic` is enabled in the soft sandbox but dynamic expressions are tree-walked with hard restrictions: `FunctionCallExpression.Eval` throws "Dynamic Expressions cannot call functions" (`FunctionCallExpression.cs:98`) and `IndexExpression.Eval` throws unless the base is a plain Lua `Table` (`IndexExpression.cs:73`). So `dynamic.eval` can read a global and index Lua tables but **cannot call methods or index any userdata/CLR member.**
- **3.3 Loader set is complete (Vector 4).** When sandboxed, `ScriptHandler.SetupAPI` nils `load`, `loadsafe`, `loadfile`, `loadfilesafe`, `dofile` (`ScriptHandler.cs:50-57`); `string.dump` is absent; metatables operate only on Lua tables. No residual compile/eval surface.
- **3.4 Stdlib FS/process/env blocked + path confinement (Vector 5).** `Preset_SoftSandbox` excludes `IO`/`OS_System` and `LimitedPlatformAccessor.FilterSupportedCoreModules` strips them again; `io.*`, `os.execute/remove/rename/exit` are absent. `LibScriptLoader.IsInsideRoot` normalizes via `Path.GetFullPath` then prefix-checks the containment root with a trailing separator, and `IgnoreLuaPathGlobal=true` — so `require`/`DoFile` reject `..`/absolute/`LUA_PATH` traversal. *(Note: this locks the **Lua** stdlib; the .NET file route in Vector 2/6c is separate and NOT blocked.)*
- **3.5 `RACommand`/`RACommandAs` are correctly FullAccess-gated** (`GlobalAdmin.cs:21-47`), and `LuaCustomData.ToPrime` restricts persisted values to nil/bool/number/string/table (no userdata smuggling).

A regression guard worth adding to the port: assert that **no reachable member returns a `System.Type`/`Delegate`/static `MethodInfo`, and no path coerces a static-userdata to a `Type`-typed parameter** — a game/LabAPI update could silently reopen 3.1/3.2.

---

## 4. The open holes (detail)

### 4.1 Capability surface — any non-framework static is reachable (Vectors 2 & 3) — OPEN by design
**Mechanism (verified by hand):** `SCriPt.RegisterType(name)` and `SCriPt.AddStaticGlobal(name[, global])` (`GlobalSCriPt.cs:107-144`) resolve an **attacker-supplied assembly-qualified type name** via `Type.GetType(typeName)` and gate only on `!FullAccess && FrameworkTypes.IsFramework(type)`. Any type that is not BCL passes. So a *sandboxed* script can do:
```lua
SCriPt:AddStaticGlobal("SomeNamespace.SomeType, Assembly-CSharp")
```
and obtain every `public static` member of any game/Unity/LabAPI/plugin type. Auto-registration (`AutoRegistrationPolicy.cs:21`) gives the same reach by navigation: touch any value of a non-framework type and it registers on first use.

**Reachable sinks (decomp-sourced examples; the *mechanism* above is certain):**
- `ServerConsole.EnterCommand(string, sender=null)` — run any command as the server console (grant admin, ban, RA). Unconditional takeover.
- `FileManager.*` (`WriteStringToFile`/`DeleteFile`/`ReadAllText`/`FileStreamCreate`) — arbitrary file read/write/delete with attacker paths.
- `HttpQuery.Get/Post(string)` — arbitrary HTTP, bypassing `GlobalHttp` (see 4.3).
- `ReferenceHub.AllHubs` / `player.ReferenceHub.serverRoles` → set `Permissions = ~0UL`, `RemoteAdmin = true` — instant self-admin, no validation.
- `ServerStatic.PermissionsHandler`, `WhiteList.Users`, `ReservedSlot.Users`, `BanPlayer.*` — persistent privilege/ban mutation.

**Refuted leads (good news — these are *not* exploitable):** feeding a `System.Type` to a `Type`-typed parameter (e.g. `AddComponent(Type)`, `AssetBundle.LoadFromFile`) fails because `ScriptToClrConversions` only coerces *instance* userdata (`.Object != null`) to its type, and script-obtainable type handles are *static* descriptors (`.Object == null`) → conversion throws (`ScriptToClrConversions.cs:166-178`). So there's no path to native-code/asset injection via type handles.

**Why it's OPEN by design:** the deny-list's job was never to sandbox the game API — it was to stop reflection into the BCL. For an operator running their own scripts, full game-API access is the *point*. For the port, this entire row must be inverted: **expose an allowlist of curated façades, never raw types, and remove `RegisterType`/`AddStaticGlobal`/auto-registration of arbitrary names** (see the feasibility doc's façade recommendation).

### 4.2 `Admin.LACommand` is not FullAccess-gated (Vector 6a) — ⚠️ SCRIPT-BUG
`GlobalAdmin.cs:49-52`:
```csharp
public static string LACommand(string command) {
    return Server.RunCommand(command);   // no FullAccess check
}
```
`RACommand`/`RACommandAs` immediately above both check `Config.FullAccess` and refuse when sandboxed; the class doc-comment says command-runners "require FullAccess." `LACommand` is the lone exception — a direct, ungated command-execution facade callable by any sandboxed script. Looks like an oversight; it should share `RACommand`'s gate. **Fix in SCriPt regardless of the port.**

### 4.3 Raw `HttpQuery` defeats `GlobalHttp` gating (Vector 6b) — OPEN
`GlobalHttp` enforces `Config.HttpEnabled` + a host allowlist + http/https-only — but a script reaches the game's own `HttpQuery.Get/Post` via 4.1, bypassing all of it. Even within `GlobalHttp`, there's no localhost/internal-range/SSRF filter (an empty allowlist means *any* host). So the config's egress controls give false assurance. For the port: there is no raw `HttpQuery` equivalent we must expose — simply don't, and treat any network capability as deny-by-default.

### 4.4 `Data` store — path traversal + no isolation (Vector 6c) — ⚠️ SCRIPT-BUG / PARTIAL
- **Path traversal (verified):** `WriteToDisk` builds `Path.Combine(ScriptLoader.ScriptPathParent.FullName, "SCriPt/Data", name + ".json")` where `name` is the script-controlled key from `Data.Get(name)`, never sanitized (`GlobalData.cs:121-132`). `Data.Get("..\\..\\..\\evil")` escapes the Data directory on flush. Constrained (forced `.json` extension, JSON content) but it's an arbitrary-location write distinct from the FileManager route, reachable by **any** script. Sanitize/validate `name` (reject separators/`..`, or hash it).
- **No isolation (verified):** `CustomData` is one process-global `Dictionary<string,LuaCustomData>` keyed by `name` with no per-script scoping (`GlobalData.cs:19,28-40`); any script can read/modify any other script's store by name.

### 4.5 Cross-script / shared-state leakage (Vector 7) — OPEN
`UserData` type registration is process-global (`TypeDescriptorRegistry` is static), so a type registered by *any* script (or during a `FullAccess` session) is visible to every other script, including later sandboxed ones, until process restart. Custom command names register into the global RA/client command tables (one script can shadow another's command); settings concatenate globally; `Data` is global (4.4). Lua *VM* state (globals/closures) **is** per-`ScriptHandler`, so scripts can't read each other's Lua locals — isolation exists at the VM level but not at the shared-registry/data/command level. Cooperative-author model tolerates this; mixed-trust does not.

---

## 5. The resource-exhaustion answer (what "wall-clock / alloc limits SCriPt lacks" means)

This is Vector 8, and it's the direct answer to the clarification. SCriPt has **one** resource limiter and it covers almost none of the execution surface.

**What exists:** `Config.CoroutineAutoYield` (default 100000) is pushed into MoonSharp's per-coroutine `AutoYieldCounter` in `GlobalCoroutines.CallCoroutine` (`GlobalCoroutines.cs:77,93`). It counts **VM instructions** and force-suspends a coroutine when exceeded.

**Why it covers almost nothing:** MoonSharp disables the counter on the main execution path. In `Processor_InstructionLoop.cs:22`:
```csharp
bool canAutoYield = (AutoYieldCounter > 0) && m_CanYield && (this.State != CoroutineState.Main);
```
The main `Script.DoString`/`Script.Call` processor is `CoroutineState.Main` (and sets `m_CanYield=false`), so **`AutoYieldCounter` has zero effect there.** It only bounds code running inside an explicitly-created MoonSharp coroutine.

**Therefore every main-thread entry point runs with NO instruction or time limit:**
- Event handlers — `LuaEvent.Invoke` → `script.Call(...)` (`LuaEvent.cs:78,105`). `Player.Joined.add(function() while true do end end)` **hangs the server thread forever.**
- Custom-command handlers — `LuaCustomCommand.Execute` → `Owner.Call(...)` (`LuaCustomCommand.cs:48`).
- REPL — `ScriptLoader.ExecuteConsole` → `_repl.Evaluate(code)`.
- Module load/unload and top-level `DoFile`.
- **Server-Specific-Settings callbacks** — `LuaCustomSettings.ServerOnSettingValueReceived` → `Owner.Call(...)` (`LuaCustomSettings.cs:55`), which **a connecting client can trigger** by sending a setting value. So even a non-author can drive an unbounded callback.

**The three missing limits, precisely:**

1. **Wall-clock / CPU (TIME):** No timeout on synchronous script execution on the main path. No `CancellationToken`, no instruction cap, no per-instruction hook active by default. An infinite loop in any callback spins the calling thread indefinitely; MoonSharp offers no built-in way to interrupt it. *(The only built-in lever that can bound the main path at all is attaching an `IDebugger` — its `IsPauseRequested()` is invoked per instruction, `Processor_InstructionLoop.cs:32-35` / `Debugging/IDebugger.cs:30-33` — which can be repurposed as an instruction/time watchdog that throws to unwind. That's the realistic mechanism to ADD; it isn't present in SCriPt.)*
2. **Allocation / MEMORY:** None whatsoever — no heap accounting, no table-element cap, no string-length cap, no memory option in `ScriptOptions`/`ScriptGlobalOptions`. `local t={} while true do t[#t+1]=1 end` grows until the host **OOMs**. The only real guarantee is an out-of-process memory cap.
3. **Recursion / STACK (bonus):** Bounded only by MoonSharp's fixed 131072-slot VM stacks (`USE_DYNAMIC_STACKS` not defined), and overflow surfaces as a raw `IndexOutOfRangeException` — *not* a catchable `ScriptRuntimeException`. Worse, recursion that re-enters the host (a metamethod calling back into `Script.Call`) uses the real CLR stack and can hard-crash the process with a native `StackOverflowException`.

**Server impact:** an infinite loop or OOM is a self-inflicted operator bug — annoying, not a security issue under the trust model.
**Client-port impact (the reason this matters):** a **hostile server can freeze, OOM, or crash the game of every connected player** with a one-line script — a trivial, high-impact availability/grief attack. The port MUST add: an `IDebugger`-based wall-clock+instruction watchdog on the main path, run untrusted scripts in a killable/isolated context as a backstop for un-yieldable CLR regions, an out-of-process or hard memory ceiling, and a recursion guard. None of these exist today.

---

## 6. Implications for the client port — the "trust flip" checklist

Each item below is a control that is *absent or open* in SCriPt because it relies on author-trust, and therefore must be **built** for the untrusted-server scenario. This is the "what needs to be looked at" list:

1. **Invert the type model.** Remove `RegisterType`/`AddStaticGlobal`/arbitrary auto-registration (§4.1). Expose only hand-written plain-C# façades as a capability **allowlist**. No `Type.GetType(attackerString)` anywhere.
2. **No raw game/Unity/LabAPI types to Lua, ever** — they are "non-framework" and thus carry the entire capability surface. Façades return only primitives/plain DTOs.
3. **Add the resource limits (§5):** main-path wall-clock+instruction watchdog (IDebugger), memory ceiling, recursion guard, killable execution context. Treat every callback (esp. anything network-triggerable) as a DoS vector.
4. **Deny-by-default capabilities:** no command execution, no filesystem, no process, no network egress, no permission/role mutation, no input injection, no PII (IP/HWID) reads. Curate the façade to cosmetic/UI/local-visual capability only (mind anti-cheat/ToS).
5. **Per-script isolation (§4.5):** scope data stores, command names, and (if possible) type registration per script/source; don't share a global registry across trust levels.
6. **Preserve the invariants (§3):** keep `GetType` hidden, the BCL deny-list, the nil'd loaders, `dynamic` restrictions, and stdlib FS/path confinement — and add a regression guard (§3 end).
7. **Sanitize all script-derived paths/keys** (§4.4) — though ideally the port exposes no disk write at all in v1.

## 7. Action items for SCriPt itself (independent of the port)
- ⚠️ **Gate `Admin.LACommand` on `FullAccess`** like `RACommand` (`GlobalAdmin.cs:49-52`).
- ⚠️ **Sanitize `Data` keys** before they reach the filesystem path (`GlobalData.cs:126`); consider per-script namespacing.
- Consider a `FullAccess`-independent wall-clock guard on event/settings callbacks so a buggy (not even malicious) script can't hang the server — the settings callback is client-triggerable.
- Optionally add localhost/internal-range filtering in `GlobalHttp` even though raw `HttpQuery` exists, for defense-in-depth.

---

### Evidence index (file:line)
Verified by hand this session: `GlobalAdmin.cs:21-52`, `GlobalData.cs:19,28-40,121-132`, `GlobalSCriPt.cs:107-144`, and MoonSharp `Processor_InstructionLoop.cs:22`.
Agent-sourced (high confidence): `SafeUserDataDescriptor.cs:60`, `AutoRegistrationPolicy.cs:21,38-42`, `ClrToScriptConversions.cs:100-147`, `ScriptToClrConversions.cs:166-178`, `IndexExpression.cs:73`, `FunctionCallExpression.cs:98`, `ScriptHandler.cs:50-57`, `LibScriptLoader.cs:62-78`, `LuaEvent.cs:78,105`, `LuaCustomCommand.cs:48`, `LuaCustomSettings.cs:55`, `GlobalCoroutines.cs:77,93`, MoonSharp `Coroutine.cs:294-298`, `Debugging/IDebugger.cs:30-33`, `FastStack.cs:28-32`, `Processor.cs:11,30-31,38,81`.
