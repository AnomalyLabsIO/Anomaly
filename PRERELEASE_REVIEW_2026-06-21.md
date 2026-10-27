# Anomaly — Pre-Release Framework Review

> **Superseded for release decisions on 2026-08-22.** This file is retained as historical audit evidence. Its findings describe an older codebase and must not be treated as the current public-v1 contract; use `anomaly-docs/RELEASE_REVIEW_2026-08-22.md` for the current classification and release gates.

**Date:** 2026-06-21
**Reviewer:** Claude (Opus 4.8) + 4 parallel deep-dive review agents
**Scope:** `Anomaly.Client`, `Anomaly.Client.Api`, `Anomaly.Server`, `Anomaly.Shared` (the four projects in `Anomaly.slnx`). Launcher and Installer were out of scope.
**Method:** Full read of all 377 source files across the four projects, cross-checked against the AssetRipper game decomp (`~/Documents/SCPSLExport/...`), the live deployed server assemblies, and LabAPI. Build-integrity (csproj compile lists) verified mechanically. Harmony semantics verified against the decomp. Crypto/auth/networking verified by hand.
**Status:** Review only — **no code changes were made.**

---

## How to read this document

Each finding has an ID, severity, confidence, location(s), the reasoning behind it, and a fix direction. Severities:

- **BLOCKER** — fix before any release. Ships broken, breaks live servers, or exposes users.
- **HIGH** — real bug with user/mod impact; fix before or immediately after first public release.
- **SECURITY** — abuse/DoS surface a hostile client or server can exploit.
- **MEDIUM / LOW / NIT** — polish, consistency, and hardening; post-release acceptable.

Confidence reflects how certain the finding is: **high** (verified against code/decomp), **medium** (depends on a runtime behavior that is standard but not directly observed here), **low** (theoretical or trigger-dependent).

"*Verified directly*" marks findings I confirmed by reading the code/decomp myself during consolidation, as opposed to agent-reported (still high-quality, but not independently re-confirmed line-by-line).

---

## Executive verdict

**The architecture is sound, but the framework is not feature-complete for release as it stands.** The problems are integration/wiring/hardening gaps, not design flaws:

- Three finished features are **dead in the build** (one un-applied Harmony patch, two files missing from the csproj).
- One feature is **hard-disabled** in a way that desyncs clients from the server's map.
- **Debug scaffolding ships enabled**, including a behavior change applied to all servers.
- Two **server concurrency bugs** can freeze a live server under normal operation.

None require a redesign. A focused bug-fix pass stands between this and "feature complete."

### What is genuinely good (the logic *is* sound)

This is well-engineered for a pre-release framework:

- Clean, enforced four-layer separation: `Anomaly.Shared` (wire contract) → `Anomaly.Client`/`Anomaly.Server` (runtimes) → `Anomaly.Client.Api` (public surface). `InternalsVisibleTo` is used correctly; the API never references the runtime.
- The auth handshake (ECDSA identity + X25519 ECDH + AES-GCM) is implemented correctly.
- Password auth is textbook: PBKDF2-SHA256 (120k iterations, 10k floor), 16-byte salt, HMAC-SHA256 proof bound to both nonces + server challenge + server identifier, constant-time comparison (`PasswordAuth.FixedTimeEquals`), per-IP lockout checked *before* any crypto work.
- Replay protection (`AnomalyReplayState`) is an above-par IPsec-style 128-bit sliding window for unordered channels and strict-monotonic for ordered channels.
- The event bus (`EventInvoker`) isolates per-subscriber exceptions so one bad mod can't break the dispatch.
- File transfer is **content-addressed** — clients request by 32-byte SHA-256 hash, the server resolves against its own pre-scanned dictionary, and the client cache writes by hash hex inside its own cache dir. This eliminates path traversal *by construction* (verified both directions).
- Deserialization is uniformly wrapped in try/catch with bounded (`byte`/`ushort`) length fields, so hostile/truncated payloads drop cleanly instead of crashing.
- Vanilla clients are rejected at the first preauth byte and can never reach the custom channel.
- IL2CPP hazards are clearly understood: fake-null checks, delegate rooting, off-thread logging via a concurrent queue drained on the main thread (`OffThreadLogger`).

---

# BLOCKERS

## B1 — `Scp079GeneratorPatch` is never applied (missing class-level `[HarmonyPatch]`)

- **Severity:** BLOCKER · **Confidence:** high · *Verified directly*
- **Location:** `Anomaly.Client/Patches/Runtime/Scp079GeneratorPatch.cs:7`
- **Status:** This is the **new, untracked file** (`git status` shows it as `A`). It ships broken.

**Reasoning.** The class is declared `public static class Scp079GeneratorPatch` with **no class-level `[HarmonyPatch]` attribute** and no nested annotated classes — only method-level `[HarmonyPatch(typeof(Scp079Generator), nameof(...))]` + `[HarmonyPrefix]`/`[HarmonyPostfix]`. Under HarmonyX (the engine MelonLoader uses, `HarmonyX 2.16.0` per the csproj), `Harmony.PatchAll(assembly)` constructs a `PatchClassProcessor` per type with `allowUnannotatedType = false`. When the type has no class-level Harmony attribute, `containerAttributes` stays null and `Patch()` returns early — **the class is skipped entirely**, even though its methods are annotated.

I confirmed this is the unique exception among the 20 Runtime patches. The other five files that lack a *bare* `[HarmonyPatch]` are all still valid because they use either a class-level `[HarmonyPatch(typeof(X))]` container (`AudioLogServerSyncClipFix`, `PlayerRoleManagerPatch`, `ThrownProjectilePatch`) or nested annotated classes (`AtlasZoneGeneratorPatch`, `SilentRoleChangePatch`). `Scp079GeneratorPatch` has neither.

**Impact.** `Scp079Generator.DeserializeSyncVars` is never patched → `ClientRuntime.HandleGeneratorEngaged` (its only caller) never runs → `GeneratorEvents.OnEngaged` is a dead public API event. The patch *logic* itself is correct (verified `DeserializeSyncVars(NetworkReader, bool)` override, `Network_flags`, and `GeneratorFlags.Engaged = 0x10` against the decomp).

**Fix.** Add a class-level `[HarmonyPatch]` (matching the convention in `ItemPickupBasePatch.cs:7`, `ReferenceHubPatch.cs:7`, etc.). Both methods target the same `DeserializeSyncVars`, so a class-level `[HarmonyPatch(typeof(Scp079Generator), nameof(Scp079Generator.DeserializeSyncVars))]` would also work and let you drop the redundant method-level `typeof`.

---

## B2 — [RESOLVED in current tree] Two source files were missing from the csproj and silently never compiled

- **Severity:** ~~BLOCKER~~ → **RESOLVED** (working tree) · **Confidence:** high · *Verified directly*
- **Status update (Review 2, 2026-06-21):** Fixed. `Anomaly.Client.csproj` now includes `Patches\Input\VirtualInputPatches.cs` (csproj:409) and `Patches\Runtime\BasicRagdollPatch.cs` (csproj:436) — confirmed by a fresh 93/93 compile-list-vs-disk diff and `git diff` (uncommitted working-tree change). `VirtualInputs` and `RagdollEvents` are now live; their Feature-Completeness rows are updated below. Keep **CC1** (the compile-list CI guard) — the `EnableDefaultCompileItems=false` footgun remains. Original finding retained below for the record.
- **Locations:**
  - `Anomaly.Client/Patches/Input/VirtualInputPatches.cs` (on disk, not in csproj)
  - `Anomaly.Client/Patches/Runtime/BasicRagdollPatch.cs` (on disk, not in csproj)
  - Root cause: `Anomaly.Client/Anomaly.Client.csproj:360` (`<EnableDefaultCompileItems>false</EnableDefaultCompileItems>`)

**Reasoning.** `Anomaly.Client` disables default compile-item globbing, so every `.cs` file must appear in an explicit `<Compile Include=...>`. I diffed the 91 csproj entries against the 93 `.cs` files on disk (excluding `obj/`): exactly these two files are present on disk and tracked in git (both added in commit `fdbe750`) but were never added to the csproj. With globbing off, MSBuild excludes them from the build **with no warning or error**. (Server and Shared also use explicit compile lists; I checked both — they are complete. This is Client-only.)

I confirmed both files are the *only* patches targeting their respective game types (`UnityEngine.Input` and `BasicRagdoll`), so nothing else compensates, and both still compile cleanly against the current API (the `internal` members they call — `VirtualInputs.Resolve*`, `Ragdoll.Get`/`Unregister`, `RagdollEvents.On*` — all exist with matching signatures). These are forgotten csproj entries, not abandoned code.

**Impact.**
- **`VirtualInputPatches`** is the *entire engine-side wiring* for the public `VirtualInputs` API. `ClientRuntime.Update` calls `VirtualInputs.TickProviders()` every frame and `VirtualInputs.Reset()` on disconnect, but without these `UnityEngine.Input.GetKey/GetAxis/...` postfixes, the game never consults registered providers. Mods can register virtual buttons/axes and nothing happens. The `VirtualInputs` XML docs even name these postfixes as their counterpart.
- **`BasicRagdollPatch`** fires `RagdollEvents.OnSpawned/OnRemoved` and calls `Ragdoll.Unregister` on each ragdoll's `OnDestroy`. Without it, those public events never fire, and the `Ragdoll` wrapper cache is never evicted per-object — only the bulk `Ragdoll.ClearAll()` at disconnect bounds it, so wrappers accumulate for the whole session (ragdolls churn constantly).

**Fix.** Add both `<Compile Include>` entries. Then add a build-time guard (see Cross-Cutting Recommendation CC1) so this class of bug can't recur — it has now hit three files.

---

## B3 — Client facility-recipe application is hard-disabled while the server still sends recipes → map desync

- **Severity:** BLOCKER · **Confidence:** high · *Verified directly*
- **Location:** `Anomaly.Client/Patches/Runtime/AtlasZoneGeneratorPatch.cs:32` (`private const bool PatchesEnabled = false;`), with `[HarmonyPrepare]` gates at the three nested patches (lines ~43/80/118).

**Reasoning.** All three client-side patches (transform override, room skip, atlas pixel edits) are skipped at patch time via `[HarmonyPrepare] => PatchesEnabled` returning false. The file's own comment says the dependent features "silently become no-ops." Meanwhile:
- The client *receive* pipeline (`Anomaly.Client/Internal/Map/FacilityRecipeClient.cs`) still registers handlers, deserializes recipes, and stores them — into state nothing consumes.
- The **server twin** (`Anomaly.Server/Map/Patches/AtlasZoneGeneratorPatch.cs`) still applies mutations and `FacilityLayout` still sends `FacilityLayoutRecipeMessage` to every client.

The facility generator is deterministic and seed-driven on both sides. If the server applies recipe mutations the client does not, the two produce **different facilities from the same seed** — a hard, gameplay-breaking desync (doors/rooms in different places).

**Why it was disabled:** the comment cites an IL2CPP trampoline NRE firing 50+ times/sec during generation. That's a real engineering problem, but disabling only the client half is worse than disabling the feature end-to-end.

**Fix.** Either (a) resolve the trampoline issue and re-enable, or (b) gate the feature off **end-to-end** — have the server refuse to send recipes when the client feature is off (or flag the whole `FacilityLayout` feature off). Do not ship with the client half disabled and the server half live.

---

## B4 — `MirrorDiagnosticPatches` (temporary diagnostics) ships enabled, including a behavior change for all servers

- **Severity:** BLOCKER · **Confidence:** high · *Verified directly (header + structure)*
- **Location:** `Anomaly.Client/Patches/Diagnostics/MirrorDiagnosticPatches.cs:11-29` (header), `:155-214` (the suppressor)

**Reasoning.** The file's own header says: *"Temporary diagnostic patches for investigating the post-auth spawn failures... Remove or gate on a config flag once the root cause is understood."* There is no config gate and no `[HarmonyPrepare]`. The whole suite is unconditionally applied in release:
- The postfix on `NetworkClient.UnpackAndInvoke` forces the result to `true` for every unknown message id, **permanently disabling Mirror's tear-down-on-unknown-message behavior for all servers (vanilla or modded)**.
- Nested patches dump `NetworkClient` state on every connect, log per-spawn, log RA RPCs, and reference a hardcoded metadata token.

**Impact.** A genuine protocol/version mismatch — exactly the kind of thing you want to catch before release — is silenced into log lines instead of a clean disconnect, and after the flood threshold, into nothing. This masks real bugs and changes Mirror's contract globally.

**Fix.** Gate the entire class behind a debug config flag (default off), or remove it now that auth/spawn work. At minimum, the unknown-message suppressor must not ship enabled.

---

## B5 — Two server concurrency bugs that can freeze a live server

- **Severity:** BLOCKER · **Confidence:** high · *Verified directly*

### B5a — `_storedPlayers` is an unlocked cross-thread `Dictionary`

- **Severity:** BLOCKER (upheld) · **Confidence:** high · *Threading verified directly against the live deployed DLL (Review 2)*
- **Location:** `Anomaly.Server/Auth/AuthHandler.cs:17` (declaration), written at `Anomaly.Server/Patches/ProcessConnectionRequestPatch.cs:484-489`, read + `Remove`d at `Anomaly.Server/Patches/PlayerAuthenticationManagerPatch.cs:59-67`.
- **Review 2 note — R1-C2 is overridden.** OpenCode (R1-C2) qualified this finding because the AssetRipper *export* shows `ProcessConnectionRequest` dispatched via `PollEvents()` from `LateUpdate()` (main thread, since `UnsyncedEvents` is unset). That export is one game version behind. I decompiled the **live deployed** `Assembly-CSharp.dll` with `ilspycmd`, and it confirms the off-thread premise (proof below). **B5a stays a BLOCKER.**

**Threading proof (live `Assembly-CSharp.dll`).** `LiteNetLib4MirrorServer.StartServer` spawns a dedicated background thread and runs connection requests on it, *not* on the main-thread event pump:

```csharp
_connectionRequestThread = new Thread(ConnectionRequestLoop) { Name = "ConnectionRequestThread", IsBackground = true };
_connectionRequestThread.Start();
// ...
private static void ConnectionRequestLoop() {                  // background thread
    while (_connectionRequests.TryDequeue(out var result))
        LiteNetLib4MirrorTransport.Singleton.ProcessConnectionRequest(result);   // OFF the main thread
}
private static void OnConnectionRequest(ConnectionRequest request) => _connectionRequests.Enqueue(request);
```

By contrast, `OnPeerConnected`/`OnNetworkReceive` in the same class `Enqueue` to the main-thread pump (`LiteNetLib4MirrorTransport.Singleton.Events`) — connection requests deliberately do not. This is also why NW switched `Peers`/`UserIds` to `ConcurrentDictionary` in this build (the export still has `Peers` as `NetPeer[]`). So the Anomaly mod's write to `_storedPlayers` (inside `ProcessConnectionRequest`) runs on `ConnectionRequestThread`, while the read/remove runs in `FixedUpdate` (main thread).

**Reasoning.** `_storedPlayers` is a plain `Dictionary<IPEndPoint, PlayerStorage>`, **written from `ConnectionRequestThread`** and **read + removed from the main thread** (`FixedUpdate` postfix), with no lock — unlike its sibling `_connections`, which correctly uses `_connectionsLock`.

Concurrent write-during-read on a Mono/.NET Framework `Dictionary` can corrupt the bucket chain and send a reader into an **infinite `FindEntry` loop = full server freeze** (the classic non-thread-safe-dictionary failure). The trigger is ordinary: one player connecting while another's auth postfix runs. An attacker can force the timing by hammering connections.

Secondary: no TTL. A client that preauthenticates but never completes the Mirror connect leaks an entry forever (keyed by `ip:port`, so an attacker can cycle ports → unbounded growth). `_connections` has TTL eviction; `_storedPlayers` does not.

**Fix.** Use `ConcurrentDictionary` (matching Northwood's own pattern for `UserIds`) or guard with `_connectionsLock`. Add TTL eviction / disconnect cleanup.

### B5b — `AssetFileWatcher` drives MEC and mutates a shared dict from worker threads

- **Location:** `Anomaly.Server/Assets/AssetFileWatcher.cs:57-70`

**Reasoning.** `Debounce` runs on `FileSystemWatcher` threadpool threads (the comment at lines 62-63 acknowledges this). Yet it (a) reads/writes `_pendingDebounces` (plain `Dictionary`, also mutated on the main thread by `ProcessChange` at line 74) with no lock, and (b) calls `Timing.KillCoroutines` and `Timing.CallDelayed` **directly from the worker thread**. MEC's `Timing` is main-thread-only (it mutates per-instance coroutine arrays and reads Unity time with no internal locking). Scheduling from a worker thread can corrupt the global coroutine scheduler that file-transfer pacing, dummy normalization, and `OffThreadLogger` all depend on, or throw on Unity API access. Two rapid file events can also run `Debounce` concurrently on two pool threads.

The correct pattern already exists in this codebase: `OffThreadLogger` enqueues to a `ConcurrentQueue` and drains on a main-thread MEC pump.

**Fix.** Enqueue changed paths to a concurrent queue; drain and schedule from a main-thread pump.

---

## B6 — Debug scaffolding ships enabled

- **Severity:** BLOCKER (cumulative) · **Confidence:** high · *Partially verified directly*
- **Locations:**
  - `Anomaly.Client/Core.cs:50` — `MelonPreferences.CreateCategory("Anomaly Test").CreateEntry("TestBool", ...)` renders as a visible **"Anomaly Test" row in every user's Mods settings tab** (the ModSettings renderer only filters `anomaly_config_`/`anomaly_persistence_` prefixes). *Flagged by 3 reviewers.*
  - `Anomaly.Server/Patches/ProcessConnectionRequestPatch.cs:31-36` — writes `anomaly_debug.log` via `File.AppendAllText` from the network thread (unbounded, CWD-relative).
  - Same file `:218-219` — hex-dumps handshake bytes/nonce on every connection; `:234-307` log every auth step at Info level (including the raw `serverIdentifier` and a useless `sharedKey.ToString()` → "System.Byte[]"), not gated by `DisplayPreauthLogs`.
  - `Anomaly.Client/Patches/Auth/PlayerAuthenticationManagerPatch.cs` — `Warning`-level logs on every send (`:135`) and every receive (`:185`, fires 2-3× per message due to IL2CPP multi-dispatch).
  - Scattered: `NewServerBrowserOnEnablePatch.cs:16`, `PlayerRoleManagerPatch.cs:26`, `PrefabRegistrationFix.cs`, `ModSettingsRenderer.cs:236-272`, `FacilityRecipeClient.cs:53`.

**Reasoning.** Beyond the user-visible test pref, the auth-path logging is a log-spam vector triggered by unauthenticated packets (vanilla suppresses this via `SuppressRejections`), buries real warnings, and leaks crypto-adjacent material into logs. `anomaly_debug.log` is a development artifact writing from a hot network path.

**Fix.** Remove the "Anomaly Test" pref and the `anomaly_debug.log` writer; demote/remove the per-send/per-receive logs; gate remaining preauth diagnostics behind `DisplayPreauthLogs`.

---

## B7 — Uncommitted `Core.cs` change removes the only enforced game-version gate

- **Severity:** BLOCKER (decision needed) · **Confidence:** high · *Verified directly*
- **Location:** `Anomaly.Client/Core.cs:21`. The uncommitted diff deletes `[assembly: MelonGameVersion("14.2.6")]`, keeping only `[assembly: SupportedGameVersion("14.2.6^")]`.

**Reasoning.** `MelonGameVersion` is MelonLoader's *enforced* compatibility check. `SupportedGameVersionAttribute` (`Anomaly.Client.Api/Compatibility/SupportedGameVersionAttribute.cs`) is a bare data attribute with a `Version` property and **no reader anywhere in the repo** — I grepped; the `"^"` caret semantics are unimplemented. So after this change there is **no enforced version gate at all**: the mod loads against any SCPSL build. With IL2CPP, offset drift on a future build tends toward undefined behavior (crashes, silent misbehavior) rather than a clean "wrong version" rejection. (Note: the public `MODDING.md` still instructs modders to use `MelonGameVersion`.)

**Fix.** Decide intent. If you want a version floor (recommended for an IL2CPP mod), either implement a loader that reads `SupportedGameVersion` and enforces the caret semantics, or keep `MelonGameVersion` until that loader exists. If the broadening is deliberate, document the risk.

---

# HIGH PRIORITY

## Lifecycle / state-reset bugs

The dominant theme across the codebase: most bugs are states that *didn't* get routed through the disconnect / round-restart cleanup paths. The cleanup that exists (`ClientRuntime.HandleClientDisconnected`, `NetworkManagerRegisterClientMessages` disconnect lambda, `FacilityLayout`'s round-restart hook) is good — these are the gaps.

### H1 — Server `AssetSpawner` / `AssetOverrides` are never cleared on round restart

- **Severity:** HIGH · **Confidence:** high (nothing clears) / medium-high (unintended)
- **Location:** `Anomaly.Server/Assets/AssetSpawner.cs:32`, `Anomaly.Server/Assets/AssetOverrides.cs:40`. Contrast `Anomaly.Server/Map/FacilityLayout.cs:344-347`, which hooks `ServerEvents.RoundRestarted` and clears correctly.

**Reasoning.** Room/door `NetworkIdentity`s are destroyed and respawned with new netIds on round restart while clients stay connected. Neither `_spawns` nor `_overrides` has a `RoundRestarted` hook, so every override keyed by an old netId becomes garbage that is **still replayed to each late-joiner** (`AssetOverrides.cs:144-160`) and can retarget whatever object now owns that netId. Spawned assets persist into the next round's (different) map at stale positions. Plugins have no documented obligation to clear these.

**Fix.** Hook `ServerEvents.RoundRestarted` in both, clearing per-round state (mirror `FacilityLayout`).

### H2 — Superseded by descriptor content-pack admission

The historical directory/admission finding no longer describes the supported
authoring system. Server content now uses `Config.ContentPackDirectory` and
explicit `ContentPacks/<namespace>/<pack>/pack.json` descriptors, including empty
authenticated inventories. See the current [content-pack contract](Anomaly.Client.Scripting/SCRIPT-PACKS.md).

### H3 — Client.Api wrapper-cache leaks (several never cleared; `Item.Unregister` never called)

- **Severity:** HIGH · **Confidence:** high
- **Locations:**
  - No `ClearAll` and not in `ClientRuntime.HandleClientDisconnected`'s list: `Features/Role.cs:7`, `Features/PlayerStat.cs:7`, `Features/PlayerEffect.cs:7`, `Features/PlayerEffects.cs:9` (the plural collection wrapper — added per R1-C4), `Features/PlayerInventory.cs:9`, `Features/PlayerStatsWrapper.cs:8`, `Features/Room.cs:9`, `Features/AdminToy.cs:9`, `Features/PlayerAnchors.cs:107`.
  - `Features/Item.cs:205-220` — `Item.Unregister` XML doc says "Called by the loader when an item is destroyed," but **nothing calls it** (only `Item.ClearAll()` at disconnect).
  - Cleared only on disconnect (not round restart): `Door.cs:113`, `Generator.cs:107`, `Tesla.cs:85`, `Elevator.cs:99`, `FacilityCamera.cs:109`, `Locker.cs:109`.
  - **Not affected (Review 2, per R1-C4):** `Pickup` and `Ragdoll` are correctly wired — `Pickup.Unregister` is called from `ClientRuntime.HandlePickupRemoved`, and `Ragdoll.Unregister` from `BasicRagdollPatch.OnDestroy` (now compiled after the B2 fix). Do not chase these.

**Reasoning.** These static `Dictionary<Il2CppObject, Wrapper>` caches grow for the process lifetime. `Role` is the worst: SL allocates a new `PlayerRoleBase` per life, so every role change of every player adds a permanent entry. Each key is an `Il2CppObjectBase` holding a GCHandle, so destroyed native objects are pinned and cannot be collected on either side. `Item`/pickups churn constantly. The doc claim on `Item.Unregister` is simply false.

Related: the fake-null guards in `Pickup.cs:258-266` (`if (Base != null)`) and `Item.cs:207` (`if (itemBase == null) return false`) are inverted for eviction — an already-destroyed base (which is exactly what you want to evict) fails the guard and is never removed.

**Fix.** Give each cache a `ClearAll`, add them to the disconnect cleanup, and add round-restart clearing for the facility-scoped ones. Wire `Item.Unregister` (or fix its doc). Fix the eviction guards to handle the destroyed case.

### H4 — `PlayerRegistry.Remove` can never evict a destroyed hub

- **Severity:** HIGH · **Confidence:** medium-high
- **Location:** `Anomaly.Client.Api/Internal/Wrappers/PlayerRegistry.cs:114-117` (`Remove`), `:170-185` (`RemoveMissing`).

**Reasoning.** `Remove` begins with `if (hub == null || ...) return false;`. `RemoveMissing` collects stale hubs precisely *because* `pair.Key == null` (IL2CPP fake-null `op_Equality` on destroyed objects), then calls `Remove(staleHub)` — which immediately early-returns on that same fake-null check. Destroyed hubs are detected as stale but never actually removed. `_hubToPlayer`, `_players` (and thus public `Player.PlayerList`), and `_gameObjectToPlayer` retain stale wrappers until the full `Clear()` at disconnect. The OnDestroy-driven `HandlePlayerRemoved` path works (hub still alive then), but any hub destroyed without that hook firing (scene-teardown ordering) is stuck for the session.

**Fix.** In `RemoveMissing`, remove stale entries directly from the dictionaries rather than routing through the null-guarded `Remove`.

### H5 — `PendingClientMessages` survives disconnect → cross-session message replay

- **Severity:** HIGH · **Confidence:** high (leak) / medium (impact)
- **Location:** `Anomaly.Client/Patches/Auth/PlayerAuthenticationManagerPatch.cs:38, 290-294, 324-367`.

**Reasoning.** Encrypted-channel messages that arrive before their `ClientChannelHandlers` entry exists are queued in a static dictionary. Nothing clears it on disconnect (`AuthHandler.OnDisconnected()` resets auth flags only). If a connection drops while messages are queued (most plausible in the early-connect window this queue exists for), the next connection's `PlayerAuthenticationManager.Start` postfix flushes **server A's RemoteAdmin/GameConsole payloads into server B's session**.

**Fix.** Clear `PendingClientMessages` in the disconnect path.

### H6 — `RoundEvents.CurrentPhase` and `SilentRoleChange.IsPending` are not reset on disconnect

- **Severity:** HIGH · **Confidence:** high (missing reset) / medium (race)
- **Locations:** `Anomaly.Client/Internal/Round/RoundStateReceiver.cs:31-50`; `Anomaly.Client/Internal/SilentRoleChange.cs:10` + `Internal/Networking/SilentRoleChangeHandler.cs:15-19` (set), cleared only in `Patches/Runtime/PlayerRoleManagerPatch.cs:22-28`.

**Reasoning.**
- `RoundStateReceiver.DispatchPhase` early-outs when `newPhase == previous`. After disconnecting mid-round, `CurrentPhase` stays e.g. `InProgress`: `Round.IsInProgress` reports true while sitting in the main menu, and on reconnect to a server whose first `RoundStateMessage` is the same phase, the initial phase-change event is swallowed.
- `SilentRoleChange.IsPending`: if a `SilentRoleChangeMessage` arrives but the local role change never lands (server aborts / kick / disconnect), the flag stays true across disconnect and suppresses the role-help overlay + AloneSoundtrack reset on the *next* local role change, possibly on a different server. Also a cross-path race: the flag rides the Anomaly encrypted channel while the role change rides Mirror's sync — ordering is not guaranteed.

**Fix.** Reset `CurrentPhase` (to `WaitingForPlayers` or an `Unknown` sentinel) and clear `IsPending` in `ClientRuntime.HandleClientDisconnected`.

## Concurrency / correctness

### H7 — `Plugin.Disable` calls `Harmony.UnpatchAll()` with no ID → strips other plugins' patches

- **Severity:** HIGH · **Confidence:** high
- **Location:** `Anomaly.Server/Plugin.cs:110`.

**Reasoning.** `Harmony.UnpatchAll(null)` removes **all** Harmony patches from every owner, not just `"Anomaly.Server"` — a well-documented Harmony footgun. Disabling/reloading Anomaly silently unpatches Exiled and any other LabAPI plugins on the same server.

**Fix.** `Harmony.UnpatchSelf()` or `Harmony.UnpatchAll(Harmony.Id)`.

### H8 — `AuthCrypto.CreateIdentity` fails ~0.4% of the time (BigInteger fixed-width bug)

- **Severity:** HIGH · **Confidence:** high · *Verified directly*
- **Location:** `Anomaly.Shared/Auth/AuthCrypto.cs:46`.

**Reasoning.** `Array.Copy(privateParams.D.ToByteArrayUnsigned(), 0, buf, 0, 32)`. `BigInteger.ToByteArrayUnsigned()` returns the *minimal-length* big-endian representation, stripping leading zero bytes. When the private scalar's most-significant byte is zero (~1/256), it returns 31 bytes; `Array.Copy(src, 0, dst, 0, 32)` then throws `ArgumentException`. So ~1/256 ≈ 0.4% of first-run identity creations fail (caught in `AuthHandler.Init`, leaving auth dead until next launch). The read side (`new BigInteger(1, privateKeyBytes)`, line 56) is symmetric and correct once the write is fixed.

Line 47 (`Q.GetEncoded()`, expected 65 bytes for an uncompressed point) is stable in practice but would benefit from explicit `GetEncoded(false)`.

**Fix.** Left-pad the scalar into a fixed 32-byte field: `BigIntegers.AsUnsignedByteArray(32, privateParams.D)` (BouncyCastle helper that does exactly this).

### H9 — `CharacterModels.DetachHook` unsubscribe is a no-op → stale hooks on pooled models

- **Severity:** HIGH · **Confidence:** medium-high
- **Location:** `Anomaly.Client.Api/Features/CharacterModels.cs:244-245` (subscribe), `:283-285` (detach).

**Reasoning.** Subscribe uses `DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(entry.BeforeDelegate)` without storing the converted instance; `DetachHook` calls `ConvertDelegate` **again** on the same managed delegate for `-=`. Il2CppInterop wraps each conversion in a *new* native delegate, so the il2cpp `Delegate.Remove` finds no match and removal silently fails. Consequence: on every local role change, `OnAnyRoleChanged` "detaches" (no-op) then re-attaches, so the old (pooled, reused) model's `CullingSubcontroller` keeps firing the stale closure against the captured old model — hooks run against models now belonging to other players, and subscriptions accumulate per role change.

**Fix.** Cache the converted `Il2CppSystem.Action` on the `HookEntry` and use the cached instance for both `+=` and `-=`.

### H10 — Crash diagnostics: corrupt minidumps, hijacked SEH, never-cleaned dumps

- **Severity:** HIGH · **Confidence:** high (M5/M7), medium (M6 net impact)
- **Locations:** `Anomaly.Client/Diagnostics/CrashHooks.cs:304` (dump arg), `:85-96`+`:312` (SEH), `Anomaly.Client/Diagnostics/CrashReportWriter.cs:50-66` (cleanup), `NativeMethods.cs:77-84`.

**Reasoning.**
- **Wrong P/Invoke arg:** `SehFilter` passes the raw `EXCEPTION_POINTERS*` straight into `MiniDumpWriteDump`'s `ExceptionParam`, which requires a `MINIDUMP_EXCEPTION_INFORMATION { ThreadId, ExceptionPointers, ClientPointers }*`. dbghelp reads garbage (ThreadId = low dword of the pointer) → the dump fails or has a bogus exception stream. The most important crash artifact is corrupt. (Hang/managed paths pass `IntPtr.Zero`, which is valid, so only the native-crash path is broken.)
- **SEH hijack:** `RegisterNative` discards the previous filter from `SetUnhandledExceptionFilter` (no chaining) and `SehFilter` always returns `EXCEPTION_EXECUTE_HANDLER`, so Unity's crash handler and WER never run for *any* native crash, even ones unrelated to Anomaly.
- **Cleanup mismatch:** `CleanupOldReports` deletes `.dmp` files derived via `Path.ChangeExtension(json, ".dmp")`, but the `.json` name uses a fresh `shortGuid` independent of the `.dmp` names (which use `ReportId` / their own guids). No `.dmp` can ever match → the 20-report cap applies to JSON only; dumps and `.txt` files accumulate forever in `%APPDATA%\Anomaly\CrashReports`.

**Fix.** Marshal a proper `MINIDUMP_EXCEPTION_INFORMATION`; return `EXCEPTION_CONTINUE_SEARCH` (or chain to the saved previous filter); make dump/txt names derive from the same id used for the json so cleanup matches.

### H11 — `CustomNetworkManagerPatch` doesn't dedup IL2CPP double-dispatch

- **Severity:** HIGH · **Confidence:** medium-high
- **Location:** `Anomaly.Client/Patches/Network/CustomNetworkManagerPatch.cs:10-36`.

**Reasoning.** All four postfixes patch virtual `NetworkManager` overrides (`OnStartClient`, `OnClientConnect`, `OnClientDisconnect`, `OnClientSceneChanged`) and use neither `PatchInvocationGuard` nor a frame guard. The codebase itself documents that these exact methods double-dispatch in IL2CPP — `NetDataWriterPut.cs:48-58` frame-dedups its own prefix on `OnClientDisconnect`, and `PlayerAuthenticationManagerPatch.cs:85-91` handles 2-3× dispatch. So `ClientEvents.Connected/Disconnected/Ready` likely fire **twice** per transition, and the disconnect-cleanup lambda runs twice (the Clear()s are idempotent, but doubled events are an API-correctness bug). The inconsistency is stark: the same method is dedup'd in one file and not here.

**Fix.** Route these through `PatchInvocationGuard` (or a per-frame guard), as the sibling lifecycle patches do.

## Exception isolation (public API guarantee)

### H12 — Input events and `AnomalyMessaging.MessageReceived` bypass per-subscriber isolation

- **Severity:** HIGH · **Confidence:** high
- **Locations:** `Anomaly.Client.Api/Input/InputRegistry.cs:41-46, 133-134`; `Anomaly.Client.Api/Networking/AnomalyMessaging.cs:116-124`.

**Reasoning.** The framework's stated guarantee is "a misbehaving mod can't break other mods," implemented by `EventInvoker`'s per-subscriber try/catch. But the two hottest mod-facing paths bypass it:
- `InputRegistry.Pressed/Released` are raw `Action<string>` multicasts (`Pressed?.Invoke(id)`). One throwing subscriber (a) suppresses all later subscribers, (b) propagates into `InputPoller.cs:32-35` and skips `_previous[id] = held` — so the Pressed edge **re-fires every frame** while held, and (c) aborts the foreach so remaining bindings get no transitions that frame.
- `AnomalyMessaging.MessageReceived?.Invoke(message)` is a raw multicast inside a single try/catch, and `NetworkMessageEvents.OnMessageReceived` runs in the same block — a throwing subscriber starves later subscribers *and* the entire `NetworkMessageEvents` dispatch for that message.

**Fix.** Route both through `EventInvoker` (or replicate its per-subscriber try/catch).

### H13 — Persistence debounced writes are cancelled on disconnect → data loss on crash

- **Severity:** HIGH · **Confidence:** high
- **Location:** `Anomaly.Client.Api/Internal/Persistence/MelonPreferencesPersistenceStore.cs:93-97`; cancelled by `ClientRuntime.HandleClientDisconnected`'s `Scheduler.ClearAll()` (`ClientRuntime.cs:158`); only flushed at `Core.OnApplicationQuit` (`Core.cs:62-68`).

**Reasoning.** Persistence writes are debounced via `Scheduler.In(500ms)`. The disconnect handler runs `Scheduler.ClearAll()` last — by design, to cancel tasks scheduled during disconnect — which also cancels pending persistence writes, including ones scheduled by mods' own Disconnected handlers. After that, nothing flushes until clean app quit. Any crash between disconnect and quit (not rare in a modded IL2CPP game) silently loses the session's persisted changes. Additionally, the public `Scheduler.PauseAll()` freezes persistence writes for as long as any mod leaves it paused.

**Fix.** `FlushAll()` on disconnect, or give the persistence store its own timer independent of the shared scheduler.

---

# SECURITY & DoS

The server accepts input from potentially hostile clients; the client trusts a potentially hostile server.

### S1 — Preauth flood DoS (vanilla anti-spoof challenge + IP rate-limit dropped)

- **Severity:** SECURITY (HIGH) · **Confidence:** high
- **Location:** `Anomaly.Server/Patches/ProcessConnectionRequestPatch.cs:164-231` (type-3 path). Compare vanilla `CustomLiteNetLib4MirrorTransport.ProcessConnectionRequest` (decomp), which runs `UseChallenge` (anti-IP-spoof cookie) and `CheckIpRateLimit` before expensive work.

**Reasoning.** The replacement implements neither the challenge nor per-IP rate limiting (only per-userId `UserRateLimit`, and only on type-4). Every spoofable single UDP type-3 packet costs an X25519 keypair gen + `SecureRandom` challenge + `GetStringSecure(50)` + a 60s `_connections` entry. `EvictExpiredLocked` is an O(n) scan inside the lock on every add → quadratic under flood. No cap on `_connections.Count`. Type-4 adds ECDH + AES-GCM + ECDSA verify per packet. Cheap remote DoS.

**Fix.** Restore vanilla's IP challenge and IP rate-limit (or equivalents) before the crypto work; cap `_connections`.

### S2 — `FileTransferSession`: bandwidth amplification + concurrent-coroutine corruption

- **Severity:** SECURITY (HIGH) · **Confidence:** high
- **Location:** `Anomaly.Server/FileTransfer/FileTransferSession.cs:93-139, 141-189`.

**Reasoning.** `HandleFileRequest` never checks whether a transfer is already running. Each `FileRequestMessage` (client-controlled, repeatable — the session stays in `ActiveSessions` until its queue drains) enqueues up to 1024 files and calls `SendNextFile()`, which starts a **new** `SendFileChunks` coroutine while previous ones still run. Consequences: (a) a malicious client re-requests the full asset set forever → unbounded `_pendingFiles` growth, repeated whole-file `File.ReadAllBytes`, ~3.2 MB/s upstream per coroutine — 32 KB request → gigabytes out (amplification); (b) concurrent coroutines share `_currentFile`/`_retryCount`; a coroutine reads `_currentFile.Hash` inside its chunk loop, so a second request reassigning `_currentFile` tags in-flight chunks with the **wrong hash** → corrupt client cache. `_sendHandle` only tracks the latest coroutine, so `CleanupPlayer` leaks older ones. The `_sending` flag (written but never read) was evidently the intended guard, never finished.

**Fix.** Wire the in-flight guard (`_sending`): reject/queue new requests while a transfer runs; track all coroutines for cleanup; cap total bytes/requests per session.

### S3 — Client-side file transfer is unbounded (malicious server can OOM the client)

- **Severity:** SECURITY (HIGH) · **Confidence:** high
- **Location:** `Anomaly.Client/FileTransfer/FileTransferClient.cs:119-144` (`HandleChunk`), `Anomaly.Client/FileTransfer/ChunkBuffer.cs:41-56` (`Reassemble`).

**Reasoning.** `HandleChunk` creates a `ChunkBuffer` for **any** hash, including hashes never requested and absent from the manifest (path falls back to `"unknown"`). No cap on per-file size vs the manifest's `file.Size`, no cap on concurrent buffers, no total-bytes budget. `FileChunkMessage` allows 65,535 chunks × 64 KB ≈ 4 GB buffered per fake hash; the number of fake hashes is unbounded. `ChunkBuffer.Reassemble` sums sizes into an `int` — >2 GB overflows negative and throws (caught, but only after memory is committed). What's done right: index/duplicate checks in `AddChunk`, SHA-256 verification before caching, content-addressed disk path (no traversal), partial buffers dropped on disconnect.

**Fix.** Accept chunks only for hashes that were requested and appear in the manifest; enforce the manifest's declared `Size`; cap concurrent buffers and total in-flight bytes; use `long` in `Reassemble`.

### S4 — Handshake does not bind session keys → active MITM defeats the encrypted channel and password

- **Severity:** SECURITY (HIGH, design) · **Confidence:** high (mechanics) / medium (threat weight)
- **Location:** `Anomaly.Shared/Auth/ClientAuthSession.cs:40-50` (signs `[12B clientNonce][20B serverChallenge]` only), `Anomaly.Shared/Auth/ServerAuthSession.cs:37-60`.

**Reasoning.** The client's ECDSA signature covers only the client nonce and server challenge. Neither side's X25519 `sessionPublic`, nor the client `identityPublic`, is part of the signed transcript, and the server has no identity key at all. An active on-path attacker can substitute X25519 keys in both directions and transparently decrypt/modify all 0xFE traffic (file transfer, future admin-ish messages). The PBKDF2 password proof binds clientNonce/serverChallenge/passwordNonce/serverIdentifier — all observable/relayable — but **not** the session publics, so password mode does not stop the MITM. This is roughly vanilla parity (vanilla ECDH is also unauthenticated), but Anomaly advertises password auth, so it's worth fixing before the protocol freezes.

**Fix.** Include both session publics + `identityPublic` in the signed transcript; ideally pin a server static key on first join (TOFU). Bump `AnomalyProtocol.CurrentVersion` while it's still cheap.

### S5 — Server accepts unencrypted (`Unsecured`) 0xFE messages from remote peers

- **Severity:** SECURITY (MEDIUM) · **Confidence:** high · *Verified directly*
- **Location:** `Anomaly.Server/Patches/PlayerAuthenticationManagerPatch.cs:303-371, 420-424` (`RequiresEncryption` covers only RemoteAdmin/AdminChat).
- **Correction (Review 2, per R1-C3):** This is **server-side only.** The original wording ("mirrored on the client receive path") was wrong. The client receive path (`Anomaly.Client/Patches/Auth/PlayerAuthenticationManagerPatch.cs:238-247`) drops unsecured messages from a remote server unless it is the local host (`NetworkServer.active`). The client is **not** vulnerable; only the server non-local path is.

**Reasoning.** The server non-local receive path dispatches channel 0xFE to `AnomalyMessaging.OnReceived` even at `SecurityLevel.Unsecured`, because `RequiresEncryption` doesn't include the Anomaly channel. The 4-byte "hash" in the envelope is a message-type FNV-1a id, not a MAC, so unsecured 0xFE has zero integrity/authenticity beyond LiteNetLib headers — an off-path UDP injector who guesses endpoint+sequence could forge file requests attributed to a player. (Replay protection itself is well done.)

**Fix.** Add `0xFE` to `RequiresEncryption` (free hardening) — reject `Unsecured` Anomaly messages from non-local connections.

### S7 — Identity/key handling (design notes for the README and a small fix)

- **Severity:** SECURITY (LOW/design) · **Confidence:** high · *Partially verified directly*
- **Locations:** `Anomaly.Client/Auth/PlayerAuth.cs:15` + `AuthHandler.cs:41` (plaintext key in `%APPDATA%/Anomaly/user.json`); `AuthHandler.CurrentKey` nulled only in `CancelPasswordPrompt` (`AuthHandler.cs:276`), **not** in `OnDisconnected`.

**Reasoning.**
- `@anomaly` IDs are `SHA256(identityPublic)+"@anomaly"`, minted from a local keypair. Impersonation is correctly prevented (you can't forge another user's ID without their private key), but **ban evasion is trivial** — delete `user.json` and a new identity is generated. This is inherent to the decentralized (Axon-style) model; it should be documented as an accepted property, and bans should be understood as IP-based in practice.
- The private key is stored as plaintext base64 with default user-profile ACLs (no DPAPI, unzeroizable). Theft = durable impersonation on all Anomaly servers.
- `AuthHandler.CurrentKey` (the session AES key) is not cleared on disconnect, and `UsingAnomalyAuth` is set true in `AuthWrite` for every connect before the server is known to speak Anomaly — a stale key from server A can be used as the encryption fallback in a later session.

**Fix.** Clear `CurrentKey` in `OnDisconnected`. Consider DPAPI for `user.json` on Windows. Document the ban-evasion property.

### S8 — Server-shipped bundle prefabs execute scripts before stripping (instantiate-active → deferred Destroy)

- **Severity:** SECURITY (HIGH) · **Confidence:** high (ordering) / medium (runtime exploitability) · *Verified directly (Review 2; restores agent finding M4, = OpenCode R1-N2)*
- **Locations:** `Anomaly.Client/Assets/SpawnedObjectRegistry.cs:93-96` (instantiate then strip), `Anomaly.Client/Assets/ScriptStripper.cs:22-39` (deferred `Object.Destroy`). Same shape in `Anomaly.Client.Api/Features/ItemModels.cs:101, 128-160`.

**Reasoning.** `SpawnedObjectRegistry.HandleSpawn` does `UnityEngine.Object.Instantiate(prefab)` — which instantiates the GameObject **active** — and only then calls `ScriptStripper.Strip(go)`. `Strip` removes MonoBehaviours with `UnityEngine.Object.Destroy` (deferred to end of frame), so any MonoBehaviour's `Awake`/`OnEnable` (with attacker-chosen serialized state, including UnityEvent persistent calls) has **already executed** before it is destroyed. `ScriptStripper`'s own doc frames stripping as the defense against "attempted code-injection" from server-shipped bundles (`ScriptStripper.cs:9-12`), so this is a stated-invariant violation, not just hygiene. The comment in `ItemModels.StripPickupBehaviour` even claims "Destroying them before the next frame prevents that" — but deferred `Destroy` does not prevent the same-frame `OnEnable`. (For `ItemModels` the prefabs are local vanilla item prefabs, so the stakes there are correctness/ordering; the **security** stakes are on the `SpawnedObjectRegistry` path, which loads untrusted server bundles.)

**Fix.** Instantiate inactive (set the clone inactive before activation), strip with `DestroyImmediate`, then activate; or have the asset pipeline **reject** bundle prefabs containing `MonoBehaviour`/`NetworkBehaviour`/`NetworkIdentity` before clients load them. (This was dropped from the first-pass written report — track it as a real pre-release security item.)

---

# FEATURE-COMPLETENESS (dead / stubbed / incomplete surface)

These are public API or features that exist but do not work, separate from the build issues above. Decide per item: wire it, gate it behind a "coming soon" flag, or remove it from the public surface + docs so modders don't build on it.

| Feature | State | Reference |
|---|---|---|
| `VirtualInputs` | ~~Dead~~ → **Fixed** (B2 resolved in working tree — engine wiring now compiled) | `VirtualInputPatches.cs` |
| `RagdollEvents` | ~~Dead~~ → **Fixed** (B2 resolved in working tree — patch now compiled) | `BasicRagdollPatch.cs` |
| `GeneratorEvents.OnEngaged` | Dead — patch not applied (B1) | `Scp079GeneratorPatch.cs` |
| `FacilityLayout` recipes | Client application disabled (B3) → desync | `AtlasZoneGeneratorPatch.cs:32` |
| `HudEvents.HintDisplaying` | Patched onto a server-only method by author's admission; never fires on client | `HintDisplayPatch.cs:14-26` |
| `Hud.PushHide()` / global HUD hide | Non-functional — `FireToggleHud` is `_ = visible;` stub; relies on change-detection that doesn't exist in `HideHUDController` | `Hud.cs:174-212` |
| `bonetransforms` / `blendshapes` / `silentrolechange` | Registered in the shared registry but the server has no handler/relay → client-to-client pose/emotion sync unimplemented server-side | `AnomalyMessageRegistry.RegisterDefaults` |
| Scheduler pause infra | `PauseAll`/`ResumeAll` and per-task `Paused` never invoked; doc claims scene-load clearing that doesn't happen | `Scheduler.cs:124-128` |
| `ScreenEffects` / `Subtitles` | Self-described v1 stubs (HDRP volume / prefab library deferred) | `ScreenEffects.cs`, `Subtitles.cs` |
| `WorldObjects.PruneDeadEntries`, `Item.Unregister` | Implemented, documented as live, never called | `WorldObjects.cs:326`, `Item.cs:205` |
| Proxy support, Geoblocking | TODO / `if (false)` placeholder in server preauth | `ProcessConnectionRequestPatch.cs:137, 384-401` |
| Local path overrides (textures, hot-reload) | Phase 1 (audio) only; Phase 2/3 not implemented (documented) | `PathOverrideRegistry.cs` |
| Bundle hot-reload | Explicitly stubbed ("despawn/respawn required") | asset pipeline |

---

# WHAT THE BASE GAME OFFERS THAT WOULD IMPROVE THIS

Places where the rewrite diverged from vanilla and lost something, cross-referenced against the decomp/live assemblies:

1. **`ConcurrentDictionary` for preauth state** — Northwood uses it for the equivalent `UserIds`/`Peers`/`Challenges`. Your `_storedPlayers` is the one plain `Dictionary` on the cross-thread auth path (B5a). Match their pattern.
2. **Restore `UseChallenge` + `CheckIpRateLimit`** — vanilla's anti-spoof cookie and per-IP limiter exist precisely for the flood vector reopened in S1.
3. **Use vanilla's ECDH KDF** — `ECDH.DeriveKey` derives the AES key; `AuthCrypto` uses raw X25519 output directly as the key (`AuthCrypto.cs`). It works, but hashing/KDF-ing the shared secret is the vanilla and correct practice.
4. **Wrap map generators like `SeedSynchronizer` does** — vanilla try/catches each generator so a throw doesn't corrupt atlas state. Your `Anomaly.Server/Map/Patches/AtlasZoneGeneratorPatch.cs:52-88` restore is a Postfix (skipped on exception) → leaked edited atlases across rounds. Use `[HarmonyFinalizer]` / try-finally.
5. **Use Mirror's `GetStableHashCode` registration** instead of the homebrew 16-bit hashes in `FacilityLayout`/`FacilityRecipeClient` — a collision with a vanilla message id silently misroutes (no check against Mirror's table).
6. **Pass the real userId to LabAPI preauth events** — `ProcessConnectionRequestPatch.cs:417, 441` raise `PreAuthenticating/PreAuthenticated` with `string.Empty`, though the `@anomaly` id is known. Whitelist/VPN/ban plugins keyed on `ev.UserId` silently no-op.

---

# MEDIUM / LOW / NIT (grouped)

### Public-API consistency a modder will trip on
- **Two event idioms:** most events are `AnomalyEventHandler<TArgs>` via static handler classes, but `InputRegistry.Pressed/Released`, `AnomalyMessaging.MessageReceived`, and `CameraManager.Changed` are raw `Action<T>` (and lack the isolation guarantees — see H12). `MessageReceived` duplicates `NetworkMessageEvents.AnomalyMessageReceived` for the same message.
- **Destroyed-wrapper behavior is split:** `Elevator`/`Tesla`/`Generator`/`Ragdoll`/`FacilityCamera`/`Locker` null-guard and return defaults; `Door`/`Item`/`Pickup`/`Player`/`Room` throw on a destroyed `Base`. No single mental model.
- **Mixed registration shapes / lifetimes:** `CommandRegistry.Register`/`InputRegistry.Register` return void (app-lifetime); `VirtualInputs`/`CameraManager`/`CharacterModels`/`Hud` return `IDisposable` (session-lifetime — `VirtualInputs.Reset()`/`CameraManager.Reset()` wipe registrations on disconnect). The lifetime split is undocumented and will bite every input/camera mod once.
- `IConfigSource.Save()` (write now) vs `IPersistenceStore.Save(key,value)` (set + debounced write) — same word, different meaning; `Get/Set/Has/Remove` vs `Load/Save/Exists/Delete` for the same shape.
- `Tr.RegisterMod` docs say ship JSON (`en.json`); the loader reads YAML (`en.yaml`) → following the doc yields zero translations with no error (`Tr.cs:27-29` vs `LocalizationService.cs:95`).
- `PlayerStatsWrapper`/`LockerChamberWrapper` are the only `-Wrapper`-suffixed types; `LocalBroadcast.Show(..., ushort seconds)` vs `LocalHint.Show(..., float seconds)`; `Coroutines.Start` returns `object`.

### Reentrancy / iteration hazards
- `Scheduler.ClearAll()` called from within a tick empties `_tasks` while the loop holds the old count → `ArgumentOutOfRangeException`, aborting the frame (`Scheduler.cs:149-207`).
- `InputRegistry.Register` during a `Pressed` handler mutates the dict mid-iteration → `InvalidOperationException` aborts the poll frame (`InputPoller.cs:24`).
- `OverrideRegistry.Apply` fires `AssetEvents.OnOverrideApplied` while iterating `_overrides` in `ReapplyAll` → re-entrant add/clear throws (`OverrideRegistry.cs:65-83`).

### `ICancellableEvent` contract not enforced
- `Events/ICancellableEvent.cs:14-17` documents "later subscribers cannot un-cancel," but every implementation is a plain `IsAllowed { get; set; } = true` (17 of them) — a later mod can silently un-veto. *Verified directly.* Either latch (`set => _allowed &= value;`) or fix the doc. "Veto wins" is the safer semantic.

### Read-path side effects / store hygiene
- `Config.Has()`/`Get()` call `GetOrCreateEntry` → merely probing a key creates a permanent MelonPreferences entry (`MelonPreferencesConfigSource.cs:71-78`). Category/entry ids are hex-encoded UTF-8 (`anomaly_config_6d796d6f64`), so the "user-editable tunables" story collapses for anyone opening `MelonPreferences.cfg`.
- `FileCache.WriteFile` doesn't verify `hash == SHA256(data)` — a public API on a content-addressed cache that lets any caller poison entries other mods resolve by hash. `HasFile`/`WriteFile` before `Init()` NRE; `ToHex(null)`/`HasFile(null)` throw rather than returning false (`FileCache.cs`).

### Diagnostics / disk
- `HangWatchdog` false positives on slow facility loads / shader warmup (>15s heartbeat) → spurious crash reports + minidumps; combined with H10's cleanup mismatch, grows disk on normal machines. (Correctly runs off-thread and never kills the process.)
- `DiagnosticCollector.RefreshGameState` allocates a snapshot + two IL2CPP string marshals every frame; 1 Hz would suffice (`DiagnosticBootstrap.cs:58`).

### Robustness / self-heal
- Corrupt `user.json` isn't regenerated (only empty/"pending" is healed) → every connect fails in `AuthWrite` (`AuthHandler.cs:45-59`).
- `RejectAuth` lacks the null/try-catch guard `AuthWrite` has; an empty-payload rejection NREs out of the prefix (`AuthHandler.cs:153-216`).
- `_connections` keyed by IP without port → two instances on one host collide; an aborted post-handshake connect leaves a dead single-use handshake that forces one guaranteed failed attempt on the next connect (`AuthHandler.cs:71`).
- Plugin reload double-subscribes: `Plugin.Enable`'s anonymous handlers are never removed in `Disable` (`Plugin.cs:67-84`).
- `FireSessionCompleted` / `RaiseClientReady` wrap the whole multicast in one try/catch — one throwing subscriber starves the rest (`FileTransferSession.cs:191`, `AnomalyMessaging.cs:28`).

### Nits
- `AuthCrypto.SingingCurve` [sic] typo in a public field; dead `AuthCrypto.Main()` demo method shipped in the crypto file; raw X25519 output used as AES key (see base-game #3).
- Commented-out proxy/statistics blocks left in `ProcessConnectionRequestPatch.cs:464-476`.
- Absolute machine-specific `HintPath`s in every csproj (`E:\Projects\AnomalyLabs\Client\...`, `..\..\Server\server\...`) block any other checkout/CI build.
- `Computer` exposes MAC address / machine name / username as public one-liners (`Computer.cs:10-27`) — gratuitous fingerprinting surface on the public API.
- `LangVersion` and `ImplicitUsings` each declared twice in `Anomaly.Client.csproj`.
- `EncryptedMessageTransport.cs:12` hardcoded Mirror message id `9934`; a startup assertion against the live registration would catch a game-update shift.
- `n`: `ModSettingMetadata.Order` vs `ModSettingCategoryMetadata.SortOrder` (same concept, two names); `CurrentItemChanging/Changed` args have TODOs to expose `ItemIdentifier` (the serial→Item mapping they wait on now exists).

---

# VERIFIED NON-ISSUES (checked, and fine)

To bound the review — these were specifically examined and are sound:

- **Path traversal: none.** Clients request by 32-byte SHA-256 hash; server resolves against a pre-scanned dictionary (`ServerAssetManager.TryGetAsset`); the client cache writes by hash hex inside its cache dir (`FileCache.GetFilePathInternal`). The server-supplied manifest `Path`/`OriginalPath` is metadata only and never touches the filesystem. *Verified both directions.*
- **Allocation bombs: contained.** All client-deserializable messages use `byte`/`ushort` counts with fixed-size reads; `ClientLoginAttempt` enforces `[32, 512]`; `ClientHandshake` is a fixed 110 bytes with a protocol-version gate; malformed payloads throw into per-message try/catch.
- **Vanilla clients** are rejected at the first preauth byte (`requestType < 3` → VersionMismatch); they never reach Mirror, the 0xFE channel, or bootstrap messages.
- **Replay protection** (`AnomalyReplayState`) is correct (IPsec sliding window unordered, strict-monotonic ordered); per-hub state is removed on `PlayerEvents.Left` (the live game fires this from `ReferenceHub.OnDestroy` for every non-local hub). Client `AnomalyReplayTable.Reset()` is wired on disconnect.
- **Password auth** end-to-end (PBKDF2 challenge with salt/iteration/nonce validation, HMAC proof bound to both nonces + challenge + identifier, constant-time compare, per-IP lockout before crypto, failures recorded only on bad proof and cleared on success). Note: the stored verifier is password-equivalent (HMAC key) — theft of the `password` config file grants access; standard for this construction.
- **Round bridges** (`RoundStartBridge`, `RoundStateBroadcaster`) — clean subscribe/unsubscribe symmetry, guarded init, exceptions contained. The reflective re-fire of `CharacterClassManager.OnRoundStarted` is justified and argued double-fire-safe.
- **Patch targets** verified against decomp: `PlayerAuthenticationManager.FixedUpdate`/`_authenticationRequested`, `SeedSynchronizer.OnNewPlayerConnected`, `CharacterClassManager.OnRoundStarted` backing field, `DummyUtils.SpawnDummy`, the `MessageChannel`↔transport channel mapping, `Scp079Generator.DeserializeSyncVars`/`GeneratorFlags.Engaged`. The manual `PlayerEvents.OnJoined` is justified (vanilla's publishers don't run in this flow).
- **`PatchInvocationGuard`** is a clean centralized per-frame dedup (instance id + kind packed into a long), used correctly in six files (the gap is H11, where it isn't used).

---

# REMEDIATION PLAN

Phased by what gates a release vs. what can follow.

## Phase 0 — Release blockers (must fix before any release)

> **Status (2026-06-21): all Phase 0 blockers addressed.** Both affected projects build clean
> (`Anomaly.Client`: 0 errors / 0 warnings; `Anomaly.Server`: 0 errors, pre-existing warnings only).
> API/behavior-affecting changes recorded in `anomaly-docs/PRERELEASE_FIXES.md`. Two items need
> in-game verification (B4) — see notes.

1. ~~**B1** — add class-level `[HarmonyPatch]` to `Scp079GeneratorPatch`.~~ **DONE** — added; `GeneratorEvents.OnEngaged` now fires.
2. ~~**B2** — add `VirtualInputPatches.cs` and `BasicRagdollPatch.cs` to `Anomaly.Client.csproj`.~~ **DONE in working tree** (Review 2 verified). Still ship **CC1**, the compile-list CI guard.
3. ~~**B3** — re-enable the client atlas/recipe patches, or gate the whole `FacilityLayout` feature off end-to-end.~~ **DONE — disabled end-to-end** (team decision). Added `FacilityLayout.Enabled = false`: staging methods no-op, custom recipe/atlas sends skipped (vanilla `SeedMessage` still sent), server generates vanilla layout → no desync. Feature inert this release; re-enable path documented in PRERELEASE_FIXES.md.
4. ~~**B4** — gate or remove `MirrorDiagnosticPatches`.~~ **DONE — removed** the file + csproj entry. ⚠️ **Needs in-game smoke test:** the unknown-message suppressor is gone (correct), so if "Unknown message id" disconnects reappear there is a real registration bug to fix at the root.
5. ~~**B5a** — make `_storedPlayers` a `ConcurrentDictionary` and add TTL/cleanup.~~ **DONE** — `ConcurrentDictionary` + `StorePlayer`/`TryTakePlayer` with 60s TTL; live-DLL-confirmed off-thread write is now safe.
6. ~~**B5b** — marshal `AssetFileWatcher` events to the main thread.~~ **DONE** — worker threads enqueue to a `ConcurrentQueue`; a main-thread MEC pump drains and debounces (OffThreadLogger pattern).
7. ~~**B6** — remove the "Anomaly Test" pref, `anomaly_debug.log`, and hot-path logging.~~ **DONE** — test pref removed; `anomaly_debug.log` routed through `OffThreadLogger`; handshake hex dumps + `sharedKey.ToString()` removed; client per-packet `Warning` spam removed. (Some Info-level preauth step logging remains — minor follow-up, not a blocker.)
8. ~~**B7** — decide the version-gate question.~~ **DONE — decision: leave unenforced + document** (team). No code change; risk + launcher-metadata note recorded in PRERELEASE_FIXES.md. Consider a follow-up if a game update causes instability.
9. **CC1 (recommended, not yet done)** — add a build/CI check: every `.cs` under `Anomaly.Client` is in the csproj, and every type with `[HarmonyPatch]` methods has a class-level container. Prevents B1/B2 recurrence.

## Phase 1 — High-priority correctness (before or immediately after first public release)

10. **H1/H2** — round-restart clearing for `AssetSpawner`/`AssetOverrides`; fire `SessionCompleted` when there's nothing to transfer.
11. **H3/H4** — wrapper-cache `ClearAll` + disconnect/round-restart wiring; fix `PlayerRegistry.RemoveMissing` to actually evict; wire or re-doc `Item.Unregister`.
12. **H5/H6** — clear `PendingClientMessages`, reset `RoundEvents.CurrentPhase` and `SilentRoleChange.IsPending` on disconnect.
13. **H7** — `Harmony.UnpatchSelf()` in `Plugin.Disable`.
14. **H8** — fix `AuthCrypto.CreateIdentity` padding (`BigIntegers.AsUnsignedByteArray(32, D)`).
15. **H9** — cache the converted delegate in `CharacterModels`.
16. **H10** — fix the minidump struct, SEH chaining, and dump/txt cleanup naming.
17. **H11** — dedup `CustomNetworkManagerPatch`.
18. **H12/H13** — route input + `MessageReceived` through `EventInvoker`; flush persistence on disconnect.

## Phase 2 — Security & DoS hardening

19. **S1** — restore IP challenge + IP rate-limit; cap `_connections`.
20. **S2** — wire the `_sending` in-flight guard; cap per-session bytes/requests; track all coroutines.
21. **S3** — client: accept only requested+manifested hashes; enforce declared sizes; cap buffers; `long` in `Reassemble`.
22. **S4** — sign the full handshake transcript (both session publics + identity); bump protocol version now.
23. **S5** — add `0xFE` to `RequiresEncryption`.
24. **S7** — clear `CurrentKey` on disconnect; consider DPAPI for `user.json`; document ban-evasion.

## Phase 3 — Feature completion & polish (post-release acceptable)

25. Triage the Feature-Completeness table: wire, flag, or remove each dead/stub surface (notably `Hud.PushHide`, `HintDisplayPatch`, the pose/emotion relay, Scheduler pause infra).
26. API consistency pass (event idioms, destroyed-wrapper contract, registration lifetimes, Save semantics, JSON↔YAML doc fix).
27. Reentrancy guards (Scheduler/InputRegistry/OverrideRegistry).
28. The MEDIUM/LOW/NIT cleanup list.
29. Adopt the base-game improvements (ECDH KDF, finalizer-wrapped generators, Mirror stable-hash registration, real userId to LabAPI preauth).

---

## Appendix — review methodology

- **Build integrity** was checked by diffing each project's `<Compile Include>` entries against the `.cs` files on disk (PowerShell `Compare-Object`). Client: 91 entries / 93 files → 2 missing (B2). Server/Shared: complete.
- **Harmony semantics** (B1) were verified by confirming HarmonyX's `PatchClassProcessor` requires a class-level container attribute and that `Scp079GeneratorPatch` is the unique Runtime patch lacking one.
- **Crypto/auth/networking** (Shared layer, the handshake, replay state, file-transfer paths, message framing) were read end-to-end and cross-checked against the decomp.
- **Per-file depth** on Patches/Components/Internal, the full `Anomaly.Client.Api` surface, the full `Anomaly.Server` plugin, and Client core systems (Core/Auth/Assets/FileTransfer/Diagnostics) was provided by four parallel review agents; their findings were consolidated and de-duplicated here, with the headline items independently re-verified.
- **No code was modified.**

---

# REVIEW 1

**Date:** 2026-06-21
**Reviewer:** OpenCode (`gpt-5.5`)
**Scope:** Document-only verification pass over the findings above, using the current working tree, the server/client export references, LabAPI source, and an `ilspycmd` decompile of HarmonyX where needed.
**Status:** No source changes were made. This section appends confirmations, corrections, and additional findings against the current code.

## Review 1 Summary

- Most release-blocker/security findings I checked are real and should stay in the remediation plan.
- The current working tree has already corrected B2's two missing client compile entries, so B2 and the feature-completeness rows for `VirtualInputs` and `RagdollEvents` should be updated if this document is used as a live release checklist.
- A few claims are overbroad or depend on a live DLL not represented by the available export, especially B5a's off-thread dictionary-freeze rationale and S5's claim that the client mirrors server-side unsecured `0xFE` acceptance.
- Additional prerelease issues found during this pass are listed below as `R1-N*` findings.

## Corrections / Qualifications

### R1-C1 - B2 is already fixed in the current working tree

- **Result:** Corrected in current tree.
- **References:** `Anomaly.Client/Anomaly.Client.csproj:360` still disables default compile items, but `Patches/Input/VirtualInputPatches.cs` is included at `Anomaly.Client/Anomaly.Client.csproj:409` and `Patches/Runtime/BasicRagdollPatch.cs` is included at `Anomaly.Client/Anomaly.Client.csproj:436`.
- **References:** `Anomaly.Client/Patches/Runtime/BasicRagdollPatch.cs:16-17` has a class-level `[HarmonyPatch]`, and `Anomaly.Client/Patches/Runtime/BasicRagdollPatch.cs:36-47` raises removal events and calls `Ragdoll.Unregister`.
- **Suggestion:** Keep CC1's compile-list guard because `EnableDefaultCompileItems=false` remains a buildability footgun, but downgrade/remove B2 as a current blocker unless the review intentionally describes an earlier commit.

### R1-C2 - B5a's plain dictionary/no-TTL concern is real; the off-thread freeze premise is not confirmed by the available export

- **Result:** Qualified.
- **References:** `_storedPlayers` is a plain `Dictionary<IPEndPoint, PlayerStorage>` at `Anomaly.Server/Auth/AuthHandler.cs:16-17`, written at `Anomaly.Server/Patches/ProcessConnectionRequestPatch.cs:484-489`, and read/removed at `Anomaly.Server/Patches/PlayerAuthenticationManagerPatch.cs:58-67`.
- **References:** The available server export shows `LiteNetLib4MirrorTransport.LateUpdate()` calling `LiteNetLib4MirrorCore.Host.PollEvents()` at `%USERPROFILE%/Documents/SCPSLExport/AssetRipper_export_Server/ExportedProjectServer/Assets/Scripts/Assembly-CSharp/Mirror/LiteNetLib4Mirror/LiteNetLib4MirrorTransport.cs:109-114`, and `LiteNetLib4MirrorServer.OnConnectionRequest` calls `ProcessConnectionRequest` from that polled event path at `%USERPROFILE%/Documents/SCPSLExport/AssetRipper_export_Server/ExportedProjectServer/Assets/Scripts/Assembly-CSharp/Mirror/LiteNetLib4Mirror/LiteNetLib4MirrorServer.cs:109-114`.
- **Suggestion:** Keep the fix direction (`ConcurrentDictionary`, locking, and TTL/disconnect cleanup), but phrase the current severity as confirmed lifetime/state hardening unless the exact live deployed `Assembly-CSharp.dll` is separately verified to invoke `ProcessConnectionRequest` off-thread.

### R1-C3 - S5 is server-confirmed, but the normal remote client path does not mirror it

- **Result:** Partially corrected.
- **References:** The server accepts remote unsecured Anomaly channel data because `RequiresEncryption` only covers RemoteAdmin/AdminChat at `Anomaly.Server/Patches/PlayerAuthenticationManagerPatch.cs:420-424`, and channel `0xFE` dispatch happens after that at `Anomaly.Server/Patches/PlayerAuthenticationManagerPatch.cs:362-370`.
- **References:** The client receive path drops unsecured messages from a remote server unless it is running as a local host/server at `Anomaly.Client/Patches/Auth/PlayerAuthenticationManagerPatch.cs:238-247`.
- **Suggestion:** Update S5 to say the server non-local receive path is confirmed vulnerable. Keep adding `AnomalyChannel.ChannelId` to the server `RequiresEncryption` check as the main fix.

### R1-C4 - H3 should separate `Pickup`/`Ragdoll` from `Item`, and should add `PlayerEffects`

- **Result:** Partially corrected.
- **References:** `Pickup.Unregister` is called from `ClientRuntime.HandlePickupRemoved` at `Anomaly.Client/Internal/Bootstrap/ClientRuntime.cs:302-313`.
- **References:** `BasicRagdollPatch` is currently compiled and calls `Ragdoll.Unregister` at `Anomaly.Client/Patches/Runtime/BasicRagdollPatch.cs:36-47`.
- **References:** `Item.Unregister` still has no caller in the source tree; its doc/call mismatch remains at `Anomaly.Client.Api/Features/Item.cs:200-205`.
- **References:** `PlayerEffects` has another static wrapper cache missing from H3's list at `Anomaly.Client.Api/Features/PlayerEffects.cs:7-10`.
- **Suggestion:** Keep the cache-leak finding, but refine the examples so contributors do not chase already-wired `Pickup`/`Ragdoll` paths.

### R1-C5 - The bundle hot-reload table row needs a more specific failure mode

- **Result:** Qualified and expanded by R1-N1.
- **References:** `SpawnedObjectRegistry` explicitly warns that bundle geometry hot-reload is unsupported at `Anomaly.Client/Assets/SpawnedObjectRegistry.cs:186-190`.
- **References:** Texture overrides do attempt a live reapply at `Anomaly.Client/Assets/OverrideRegistry.cs:157-184`, but that reapply depends on the new hash already being in the client cache.
- **Suggestion:** Split the row into bundle geometry hot-reload unsupported and texture hot-reload missing a transfer path for connected clients.

## Confirmed High-Impact Findings

- **B1 confirmed:** `Scp079GeneratorPatch` has no class-level Harmony container at `Anomaly.Client/Patches/Runtime/Scp079GeneratorPatch.cs:7-18`. `ilspycmd` decompile of `%USERPROFILE%/.nuget/packages/harmonyx/2.16.0/lib/netstandard2.0/0Harmony.dll` confirmed `PatchClassProcessor(Harmony, Type)` uses `allowUnannotatedType: false`, returns early when the patch type has no Harmony attributes, and `Patch()` returns null when `containerAttributes` is null. Add a class-level `[HarmonyPatch]`.
- **B3 confirmed:** Client recipe/atlas patches are hard-disabled by `PatchesEnabled = false` and `[HarmonyPrepare]` at `Anomaly.Client/Patches/Runtime/AtlasZoneGeneratorPatch.cs:25-32`, `:39-43`, `:76-80`, and `:114-118`, while the server still sends recipe/atlas bootstrap data at `Anomaly.Server/Map/FacilityLayout.cs:261-311`. Gate the feature end-to-end or re-enable the client side.
- **B4 confirmed:** `MirrorDiagnosticPatches` is compiled at `Anomaly.Client/Anomaly.Client.csproj:417`, labels itself temporary/remove-or-gate at `Anomaly.Client/Patches/Diagnostics/MirrorDiagnosticPatches.cs:11-29`, and changes `NetworkClient.UnpackAndInvoke` failures into success at `Anomaly.Client/Patches/Diagnostics/MirrorDiagnosticPatches.cs:155-213`. Remove it or make it debug-config off by default.
- **B5b confirmed:** `AssetFileWatcher` acknowledges `FileSystemWatcher` worker-thread callbacks at `Anomaly.Server/Assets/AssetFileWatcher.cs:60-63`, then mutates `_pendingDebounces` and calls MEC `Timing.KillCoroutines`/`Timing.CallDelayed` at `:66-70`; `ProcessChange` mutates the same dictionary at `:72-75`. Queue paths to a main-thread pump before touching MEC or shared dictionaries.
- **B6/B7 confirmed:** The visible test preference remains at `Anomaly.Client/Core.cs:50`; `anomaly_debug.log` is still written from the preauth patch at `Anomaly.Server/Patches/ProcessConnectionRequestPatch.cs:31-36`; `MelonGameVersion` is absent while only the data-only `SupportedGameVersion` remains at `Anomaly.Client/Core.cs:19-21` and `Anomaly.Client.Api/Compatibility/SupportedGameVersionAttribute.cs:3-7`.
- **H1 historical finding; H2 superseded:** `AssetSpawner` and `AssetOverrides` replay state via `FileTransferSession.SessionCompleted` but had no round-restart clear, while `FacilityLayout` cleared on `RoundRestarted`. H2's directory example has been replaced by the current descriptor-driven content-pack contract; this note does not revalidate unrelated historical findings.
- **H7/H8 confirmed:** `Plugin.Disable` calls global `Harmony.UnpatchAll()` at `Anomaly.Server/Plugin.cs:104-110`. `AuthCrypto.CreateIdentity` copies a variable-length `BigInteger.ToByteArrayUnsigned()` into a 32-byte field at `Anomaly.Shared/Auth/AuthCrypto.cs:38-48`; use fixed-width left padding.
- **H9-H13 confirmed:** Delegate conversion is not cached for `CharacterModels` attach/detach at `Anomaly.Client.Api/Features/CharacterModels.cs:241-245` and `:282-285`; lifecycle network patches lack `PatchInvocationGuard` at `Anomaly.Client/Patches/Network/CustomNetworkManagerPatch.cs:10-36`; raw input/message events bypass `EventInvoker` at `Anomaly.Client.Api/Input/InputRegistry.cs:41-46`, `:133-134` and `Anomaly.Client.Api/Networking/AnomalyMessaging.cs:116-124`; debounced persistence uses the shared scheduler at `Anomaly.Client.Api/Internal/Persistence/MelonPreferencesPersistenceStore.cs:93-97` and disconnect clears the scheduler at `Anomaly.Client/Internal/Bootstrap/ClientRuntime.cs:135-158`.
- **S1 confirmed against vanilla export:** Anomaly performs phase-1 server auth work before any vanilla-style challenge/IP limiter at `Anomaly.Server/Patches/ProcessConnectionRequestPatch.cs:164-231`. Vanilla runs challenge/IP rate limiting before later auth flow at `%USERPROFILE%/Documents/SCPSLExport/AssetRipper_export_Server/ExportedProjectServer/Assets/Scripts/Assembly-CSharp/CustomLiteNetLib4MirrorTransport.cs:223-339` and `:667-691`.
- **S2/S3/S7 confirmed:** Server file requests can start overlapping sends because `_sending` is set but never checked at `Anomaly.Server/FileTransfer/FileTransferSession.cs:93-139`; the client accepts chunks for any hash and buffers by attacker/server-controlled hash at `Anomaly.Client/FileTransfer/FileTransferClient.cs:119-144`; `ChunkBuffer.Reassemble` sums sizes into `int` at `Anomaly.Client/FileTransfer/ChunkBuffer.cs:41-47`; `CurrentKey` is set at `Anomaly.Client/Auth/AuthHandler.cs:138` and only nulled in password-cancel at `:273-277`, not normal disconnect at `:313-340`.
- **S4 confirmed mechanically:** The login signature covers only client nonce and server challenge at `Anomaly.Shared/Auth/ClientAuthSession.cs:40-49`; server validation reconstructs the same 32-byte buffer and verifies it at `Anomaly.Shared/Auth/ServerAuthSession.cs:37-60`. Neither side signs the X25519 public keys or `identityPublic`.

## Additional Findings

### R1-N1 - Asset hot-reload broadcasts the new hash but does not deliver the new bytes to connected clients

- **Severity:** HIGH · **Confidence:** high
- **References:** The server rescans then only sends `AssetReloadMessage` at `Anomaly.Server/Assets/AssetFileWatcher.cs:105-125`.
- **References:** The client only requests file bytes from the initial manifest path at `Anomaly.Client/FileTransfer/FileTransferClient.cs:80-103`.
- **References:** Texture reload handling swaps to `NewHash` and immediately calls `Apply`, but `Apply` returns pending when `AnomalyResources.TryGetTexture(entry.TextureHash, out var tex)` fails at `Anomaly.Client/Assets/OverrideRegistry.cs:157-184`.
- **References:** Deletes are not watched (`Changed`, `Created`, `Renamed` only) at `Anomaly.Server/Assets/AssetFileWatcher.cs:42-44`, and deleted paths return silently at `Anomaly.Server/Assets/AssetFileWatcher.cs:76-77`.
- **Impact:** Already-connected clients usually do not have the new content-addressed bytes, so texture hot-reload becomes a pending/no-op until a reconnect or some other cache population path. Deleted assets also remain stale for connected clients.
- **Suggestion:** Treat hot-reload as a delta file-transfer operation: send a manifest/delta for `NewHash`, let clients request and cache it, then apply `AssetReloadMessage` after the bytes are available. Add `Deleted` handling that clears or despawns stale content.

### R1-N2 - Server-shipped visual prefabs can run scripts before stripping

- **Severity:** HIGH / SECURITY · **Confidence:** high for ordering, medium for runtime exploitability
- **References:** Spawned bundle prefabs are instantiated active before stripping at `Anomaly.Client/Assets/SpawnedObjectRegistry.cs:87-97`.
- **References:** `ScriptStripper` promises no bundle script execution but uses deferred `UnityEngine.Object.Destroy` on `MonoBehaviour`s at `Anomaly.Client/Assets/ScriptStripper.cs:7-13` and `:27-33`.
- **References:** `ItemModels.InstantiateVisual` has the same instantiate-then-deferred-destroy shape for pickup/network components at `Anomaly.Client.Api/Features/ItemModels.cs:96-103` and `:128-160`.
- **Impact:** Unity can run `Awake`/`OnEnable` during active instantiation, and deferred `Destroy` leaves stripped components alive until end of frame. That undercuts the stated visual-only/no-script-execution guarantee for server-shipped bundles.
- **Suggestion:** Instantiate inactive or validate/reject bundle prefabs before instantiation, then remove disallowed components with an immediate safe path before activation. If `DestroyImmediate` is unsafe in this runtime, make the asset pipeline reject prefabs containing `MonoBehaviour`, `NetworkIdentity`, or `NetworkBehaviour` components before clients load them.

### R1-N3 - Several shared serializers silently truncate over-limit lengths/counts

- **Severity:** MEDIUM / HIGH for public API robustness · **Confidence:** high
- **References:** `FileManifestMessage.Serialize` casts `Files.Count` to `ushort` and path byte length to `byte` at `Anomaly.Shared/Networking/Messages/FileManifestMessage.cs:15-24`.
- **References:** `AssetSpawnMessage.Serialize` casts `BundleAssetName` byte length to `byte` at `Anomaly.Shared/Networking/Messages/AssetSpawnMessage.cs:32-39`.
- **References:** Additional count/length casts exist at `Anomaly.Shared/Networking/Messages/AnnouncerVocabularyMessage.cs:14-24`, `BoneTransformsMessage.cs:14-18`, `EmotionBlendShapesMessage.cs:14-18`, and `FileRequestMessage.cs:13-17`.
- **Impact:** Oversized mod/plugin inputs produce corrupted wire payloads instead of clear rejection. For example, a manifest path over 255 UTF-8 bytes writes a wrapped length, causing the receiver to parse leftover path bytes as hash/size/kind data.
- **Suggestion:** Add explicit upper-bound validation in each serializer and fail loudly with `InvalidDataException`/logged rejection. Also validate fixed hash lengths before writing `byte[]` fields that are assumed to be exactly 32 bytes.

### R1-N4 - Reserved-slot users can still be rejected by the LabAPI preauth `CanJoin` value

- **Severity:** HIGH · **Confidence:** high
- **References:** Anomaly first uses reserved-slot-aware `HasSlot(userId)` at `Anomaly.Server/Patches/ProcessConnectionRequestPatch.cs:403-412` and `:524-541`.
- **References:** It then constructs `PlayerPreAuthenticatingEventArgs` with `!CustomLiteNetLib4MirrorTransport.IsServerFull()` and `string.Empty` user ID at `Anomaly.Server/Patches/ProcessConnectionRequestPatch.cs:415-418`, and rejects when `ev.CanJoin` is false at `:437-450`.
- **References:** Vanilla passes the real user ID into `IsServerFull(result10, flags)` and the LabAPI event at `%USERPROFILE%/Documents/SCPSLExport/AssetRipper_export_Server/ExportedProjectServer/Assets/Scripts/Assembly-CSharp/CustomLiteNetLib4MirrorTransport.cs:550-597`; vanilla reserved-slot logic is at `:641-664`.
- **Impact:** A player who passed Anomaly's reserved-slot check can still be rejected by the later LabAPI branch if normal slots are full. Plugins also receive an empty user ID despite Anomaly knowing the stable `@anomaly` ID.
- **Suggestion:** Pass the real `userId` to both LabAPI preauth events and use the same reserved-slot-aware join decision for `CanJoin` that Anomaly already computed.

### R1-N5 - Managed crash reports can omit the minidump path they just created

- **Severity:** MEDIUM · **Confidence:** high
- **References:** `OnUnhandledException` writes the crash JSON at `Anomaly.Client/Diagnostics/CrashHooks.cs:106-108`, then creates the dump and assigns `report.MiniDumpPath` at `:109-114`.
- **References:** `CrashReportWriter.Write` serializes immediately at `Anomaly.Client/Diagnostics/CrashReportWriter.cs:24-30`, so the JSON on disk does not include the later `MiniDumpPath` assignment.
- **Impact:** Even when a terminating managed crash produces a dump, the report that users send back may not point to it. This compounds H10's cleanup/naming issues.
- **Suggestion:** Compute and assign `MiniDumpPath` before the first `CrashReportWriter.Write(report)`, or re-write the JSON after dump creation.

## Build / Verification Notes

- I did not run project builds because the user requested document-only review and this repository has environment-specific game/MelonLoader references.
- `ilspycmd` was available and used only to inspect HarmonyX patch processing behavior for B1.
- All paths and line references above refer to the current working tree or the local export paths listed in `AGENTS.md`.

---

# REVIEW 2 — Reconciliation

**Date:** 2026-06-21
**Reviewer:** Claude (Opus 4.8, original author of the main report)
**Scope:** Verification of every Review 1 (OpenCode) correction and addition against the current working tree, and — where the export was insufficient — against the **live deployed** `Assembly-CSharp.dll` via `ilspycmd`. The main-report findings above (B2, B5a, S5, H3) and the Feature-Completeness table / Phase 0 plan have been edited in place to reflect the verified outcomes; this section records the verdicts and evidence.

## Verdict summary

Review 1 is high-quality and mostly correct. **Four of five corrections are adopted; one (R1-C2) is overridden** because it relied on the AssetRipper export, which is a game version behind — the live binary the mod targets confirms the original finding. All five new findings (R1-N1…N5) are valid and verified.

## Corrections

| Ref | Verdict | Notes |
|---|---|---|
| **R1-C1** (B2 already fixed) | ✅ **Adopted** | Re-ran the compile-list-vs-disk diff: 93/93, both files now in csproj (409/436), confirmed by `git diff` (uncommitted). B2 marked RESOLVED above; Feature-Completeness rows for `VirtualInputs`/`RagdollEvents` flipped to Fixed; Phase 0 item 2 marked done. Keep CC1. |
| **R1-C2** (B5a off-thread premise unconfirmed) | ❌ **Overridden** | OpenCode reasoned from the export (main-thread `PollEvents`). The **live `Assembly-CSharp.dll`** runs `ProcessConnectionRequest` on a dedicated `ConnectionRequestThread` (background) — proof embedded in B5a above. The `_storedPlayers` write (preauth thread) vs read/remove (`FixedUpdate`, main thread) race is real. **B5a stays a BLOCKER.** OpenCode's caveat ("unless the live DLL is separately verified") was the right instinct; the verification confirms off-thread. |
| **R1-C3** (S5 client does not mirror) | ✅ **Adopted** | Client drops unsecured from a remote server unless local host (`Client/Patches/Auth/PlayerAuthenticationManagerPatch.cs:238-247`). S5 corrected to server-only above. |
| **R1-C4** (H3: split Pickup/Ragdoll; add PlayerEffects) | ✅ **Adopted** | `Features/PlayerEffects.cs:9` has its own uncleared cache — added to H3. `Pickup.Unregister`/`Ragdoll.Unregister` are correctly wired — noted as out-of-scope in H3. |
| **R1-C5** (split bundle hot-reload row) | ✅ **Adopted (see R1-N1 below)** | Verified geometry hot-reload is unsupported (`SpawnedObjectRegistry.cs:186-190`) and texture reapply no-ops without the new bytes cached (`OverrideRegistry.Apply:177-184`). The substantive gap is captured by R1-N1. |

## New findings — verified

All confirmed against source; promote into the tracked backlog alongside the main report's findings.

- **R1-N1** (hot-reload broadcasts the new hash but never delivers the bytes) — ✅ **Valid.** `AssetFileWatcher` sends only `AssetReloadMessage`; the client only requests bytes from the initial manifest; `OverrideRegistry.Apply` (`:177-184`) bails to `Applied=false` when `TryGetTexture(NewHash)` misses. Deletes aren't watched. Result: texture hot-reload is a pending no-op for connected clients until reconnect. Genuinely new (neither my pass nor the agents isolated it). **Severity: HIGH.**
- **R1-N2** (bundle prefabs run scripts before stripping) — ✅ **Valid.** Verified; promoted to **finding S8 in the main report above** (it also matches the Client-core agent's M4, which I had dropped). Security stakes are on the `SpawnedObjectRegistry` (untrusted-bundle) path. **Severity: SECURITY (HIGH).**
- **R1-N3** (serializers silently truncate over-limit lengths on send) — ✅ **Valid.** `FileManifestMessage`/`AssetSpawnMessage`/announcer/bone/blendshape/file-request all cast counts/lengths to `byte`/`ushort` on the **send** side, so oversized mod/plugin inputs corrupt the wire instead of failing loudly (complements the main report's note that the *receive* side is safe). **Severity: MEDIUM (HIGH for public-API robustness).**
- **R1-N4** (reserved-slot users can be rejected by the LabAPI `CanJoin`) — ✅ **Valid (medium confidence).** `HasSlot(userId)` is reserved-slot-aware (`ProcessConnectionRequestPatch.cs:404, 524-541`) but the LabAPI event is built with the parameterless `!IsServerFull()` (`:417`) and rejection keys on `ev.CanJoin` (`:437-450`); vanilla passes the real userId into a reserved-slot-aware `IsServerFull(userId, flags)`. Overlaps the main report's base-game item #6 (empty userId to LabAPI preauth). **Severity: HIGH.** (Confidence hinges on the parameterless `IsServerFull()` not being reserved-aware, which the vanilla overload distinction implies.)
- **R1-N5** (managed crash report omits the minidump path) — ✅ **Valid.** `CrashHooks.OnUnhandledException` calls `CrashReportWriter.Write(report)` (`:107`, serializes JSON immediately) *before* assigning `report.MiniDumpPath` (`:113`), so the on-disk report never references the dump. Compounds H10. **Severity: MEDIUM.**

## Confirmations

All of Review 1's confirmed items (B1, B3, B4, B5b, B6/B7, H1/H2, H7/H8, H9–H13, S1–S4, S7) match the main report. B1 remains broken in the current tree (`Scp079GeneratorPatch.cs:7` unchanged — no class-level container). OpenCode's `ilspycmd` check of HarmonyX `PatchClassProcessor` (`allowUnannotatedType: false`) independently confirms the B1 reasoning.

## Net effect on the release gate

- **Removed from Phase 0:** B2 (fixed in working tree).
- **Upheld in Phase 0:** B5a (live-DLL proof), B1, B3, B4, B5b, B6, B7.
- **Added to the backlog:** S8 (= R1-N2), R1-N1, R1-N3, R1-N4, R1-N5.
- **Corrected:** S5 (server-only), H3 (+PlayerEffects, −Pickup/Ragdoll).

## Review 2 — verification notes

- Live binary inspected: `E:\Projects\AnomalyLabs\Server\server\SCPSL_Data\Managed\Assembly-CSharp.dll` (dated 2026-04-05), decompiled per-type with `ilspycmd` (`Mirror.LiteNetLib4Mirror.LiteNetLib4MirrorServer`). This is the build the mod's deploy targets, and it differs from the AssetRipper export (e.g. `Peers` is `ConcurrentDictionary<int, NetPeer>` in the live build vs `NetPeer[]` in the export), which is why the export-based threading conclusion in R1-C2 does not hold.
- csproj completeness re-checked mechanically (PowerShell `Compare-Object` of `<Compile>` entries vs on-disk `.cs`): 93/93, none missing.
- No source changes were made; only this review document was edited.
