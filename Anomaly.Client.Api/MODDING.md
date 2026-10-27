# Modding against Anomaly.Client.Api

## What this library is

`Anomaly.Client.Api` is the public **peer library** that exposes event buses, feature wrappers, registries, and helpers to other MelonLoader mods targeting the Anomaly-enabled SCP:SL client. `Anomaly.Client` is the runtime mod that owns the MelonMod lifecycle — Harmony patches, auth, diagnostics, network handlers — and raises events on the Api surface. Your mod links against `Anomaly.Client.Api.dll` (plus `Anomaly.Shared.dll`) and never needs to touch `Anomaly.Client.dll`.

Start with an ordinary IL2CPP MelonMod and use Anomaly's static helpers directly.
The [HelloAnomalyMod starter](../samples/HelloAnomalyMod/README.md) includes a
command, input binding, preference, events, scheduling, and explicit cleanup.
Managed modules implementing `IAnomalyMod` remain an advanced alternative for
framework-owned initialization, cleanup, dependency ordering, and fault attribution.

See [Client game API](GAME_API.md) for the LabAPI member mapping, typed item and
facility wrappers, observation events, and client data-availability rules.

This early access SDK intentionally resets the development API baseline while
retaining version `1.0.0`. Rebuild earlier development mods; see
[API_VERSIONING.md](API_VERSIONING.md) for the migration list.

## Prerequisites

1. Install the modded client through the Anomaly Launcher (see the repo README).
2. Start the modded game once so MelonLoader generates `Il2CppAssemblies/` and `UserLibs/`.
3. Extract the matching developer SDK and use its `samples/HelloAnomalyMod` project.
4. From that sample directory, run `dotnet build -c Release -p:AnomalyGameRoot="X:\Games\Anomaly" -p:DeployLocal=false`.
   The sample finds its enclosing SDK automatically. When copying it elsewhere,
   retain the shared `Anomaly.Sample.props` and supply `AnomalySdkRoot` as well.
5. Reference `Anomaly.Client.Api.dll` and `Anomaly.Shared.dll` from the same SDK;
   their XML documentation is beside each DLL. You do not reference
   `Anomaly.Client.dll` directly. The extracted starter does not require a source checkout.

## Load order

MelonLoader calls `OnInitializeMelon` in dependency order. If you call any Anomaly API during `OnInitializeMelon`, declare the dependency explicitly:

```csharp
using MelonLoader;
using Anomaly.Client.Api.Compatibility;

[assembly: MelonInfo(typeof(MyMod.Core), "MyMod", "1.0.0", "YourName", null)]
[assembly: MelonID("mymod")]
[assembly: MelonGame("Northwood", "SCPSL")]
[assembly: SupportedGameVersion("14.2.7^")]
[assembly: MelonAdditionalDependencies("Anomaly.Client")]
```

The string `"Anomaly.Client"` is the runtime assembly's simple name. MelonLoader's
dependency graph uses assembly names, not the `MelonInfo` display name
`"Anomaly"`. This string declares initialization order; it does not require a
compile reference to the runtime DLL. Without the attribute, load order is
undefined and your mod may call the API before Anomaly has installed its hooks
and initialized its services.

Mods that only call Anomaly APIs from event handlers (i.e. not during
`OnInitializeMelon`) do not strictly need `MelonAdditionalDependencies`, but
declaring it avoids a future load-order footgun.

Anomaly's `SupportedGameVersion` is enforced by the launcher before it
installs or materializes an assembly. `14.2.7^` means
`>=14.2.7 && <14.3.0`; an expression without `^` matches exactly that
three-component version. A missing, malformed, or incompatible declaration
blocks the affected official-core/modded launch path. Do not add or rely on
MelonLoader's `MelonGameVersion` attribute.

## Anomaly-managed modules

Managed modules live under
`<modded-game>/UserData/Anomaly/Modules`, implement `IAnomalyMod`, and have
a same-name `.dll.anomaly.json` sidecar. The manifest declares a stable id,
API/version bounds, at least one `SupportedGameVersions` expression,
dependencies, conflicts, requested capabilities, publisher, and the DLL's
SHA-256. Anomaly checks game compatibility and the hash before
`Assembly.LoadFrom`, validates the complete dependency graph before loading
any module, and tears down context-owned commands, scheduled work,
cancellation, subscriptions, and tracked resources on disable.

Anomaly loads local mods without a separate hash-approval file or automatic
mod quarantine. A managed module that fails is disabled for that process and
remains in its installation directory so it can be retried on the next launch.
`Anomaly.Bootstrap` checks runtime protection before the Mods phase and records
the loaded mod inventory for server admission. Managed code and ordinary
MelonMods run with the authority of the game process; they are not sandboxed.

## Picking an id

Your mod picks a lowercase dotted namespace (regex: `^[a-z][a-z0-9_]{0,31}(\.[a-z][a-z0-9_]{0,31}){0,39}$`). Examples: `mymod`, `acme.wargames`, `team_alpha.ui`.

Use it as the prefix on every Anomaly registration that takes a `modId` or a namespaced key:

```csharp
CommandRegistry.Register("mymod", new MyCommand());
// Retain the handle and dispose it during OnDeinitializeMelon.
var binding = InputRegistry.Register(new InputBindingDefinition(
    "mymod.jump", "mymod.input.jump", new InputKey(KeyCode.F)));
ClientConfig.For("mymod").Set("volume", 0.75f);        // compatibility facade
ClientPersistence.For("mymod").Save("loadout", myData); // compatibility facade
Scheduler.StartCooldown("mymod.ability", TimeSpan.FromSeconds(5));
Tr.RegisterMod("mymod");
```

There's no "register my mod" call. The registries enforce that ids look like a namespace — they don't maintain a global mod list. The reserved `anomaly` / `anomaly.*` prefix is used by framework built-ins and is rejected if your mod uses it.

Dedicated Anomaly servers can require a companion client mod during connection.
MelonLoader's optional `MelonID` is the reported ID for an ordinary MelonMod;
without it, Anomaly reports the `MelonInfo` display name instead. Declare a
stable `MelonID` matching your Anomaly namespace so server authors can use it
in their requirement. The report also contains the MelonInfo version. Anomaly
managed modules use their manifest ID and version. This connection inventory
is separate from the feature registration namespaces above.

Raw MelonPreferences category IDs are a separate storage identifier. Keep them
free of dots and use underscores: for Anomaly mod ID `acme.wargames`, use
`MelonPreferences.CreateCategory("acme_wargames", "Wargames")`. The supported
loader treats dots as TOML paths, which can lose saved entries. Pass the same raw
category ID to `ModSettings.RegisterCategory` when adding category metadata.
`ClientConfig.For` and `ClientPersistence.For` still take the dotted Anomaly ID;
their storage adapters encode it internally.

## Lifecycle

Anomaly's own init happens inside its `OnInitializeMelon`: localization service, Harmony patches, auth handler, `ClientRuntime`, diagnostics, `UiBootstrap`. By the time your `OnInitializeMelon` runs (assuming you declared the dependency), everything is ready to subscribe to.

| Phase | Safe to do | Not safe |
|---|---|---|
| Your `OnInitializeMelon` | Register commands/input/cooldowns; create MelonPreferences categories or `ClientConfig.For` / `ClientPersistence.For`; `Tr.RegisterMod`; subscribe to any `*Events` | Touch `LocalPlayer.Instance` (local hub not spawned); read `Client.Players` (empty); instantiate UI (canvas doesn't exist yet) |
| `ClientEvents.CanvasReady` | `UiCanvas.GetLayer(...)`, instantiate UI prefabs under a layer | Local player still not ready |
| `ClientEvents.Connected` | Observe the transport connection and reset connection-specific state | Do not assume the local hub, world objects, or authenticated enrollment are available |
| `ClientEvents.Ready` | Observe the client scene-ready signal; start work that checks its own prerequisites | `LocalPlayer.Instance` may still be null; player/world collections may still be incomplete; `Client.EnrollmentId` may still be null |
| `ClientEvents.Disconnected` | Idempotently clear your own state and cancel owned work, including work from failed connection attempts | Do not assume a prior `Ready` or exactly one event per gameplay session. Client persistence is flushed before framework-owned scheduled state is reset; do not rely on tasks or cooldowns surviving |

`Ready` is raised from the network manager's `OnClientSceneChanged` callback;
`Client.IsClientReady` records that lifecycle signal. Neither guarantees that
the local player has spawned, all world wrappers are available, or the server
has published enrollment. Null-check `Client.LocalPlayer` and any related state
at the point of use, and defer work until its actual prerequisites are present.
`Ready` may occur again after a scene transition. `Disconnected` can also fire
for handshake/retry transport attempts, including ones that never became ready,
so cancel-and-clear cleanup must be safe to repeat.

**No replay-on-subscribe.** If you subscribe to `CanvasReady`, `Connected`, or `Ready` after it has already fired, you won't get a late-dispatch call. Subscribe in `OnInitializeMelon`.

**No assembly hot-unload.** A managed module can be disabled immediately at Anomaly-owned boundaries: its cancellation token is cancelled, commands and scheduled work are removed, tracked subscriptions/resources are disposed, and future owned callbacks are rejected. Its assembly remains loaded in the IL2CPP process, so complete removal takes effect on the next launch. Ordinary MelonMods remain governed by MelonLoader's process-lifetime behavior.

## Patch lifetime

Install process-owned Harmony patches once and reuse them across connection,
scene, round, and session restarts. Make initialization idempotent using state owned
by your mod. Keep per-session enablement and resources separate from the installed
patches so a restart does not add another registration or detour chain.

Choose one installation path. MelonLoader automatically applies discovered Harmony
patches for an ordinary MelonMod; do not also call `HarmonyInstance.PatchAll()` for
the same patch classes. If your mod requires manual installation, declare
`[assembly: HarmonyDontPatchAll]` from `MelonLoader` and own the installation and
cleanup explicitly. An intentional reinstall should remove the exact prior
registration using the same owner and retain synchronization around its lifecycle.
Only remove patches and detours that your mod owns.

Bootstrap logs an advisory warning when successful supported Harmony operations
leave multiple active registrations for the exact target, patch method, kind, and
owner. It also warns after observing three successful installations of that tuple
within five minutes with removals between activations. A deliberate reinstall can
therefore produce a warning; use its owner and assembly attribution to investigate
restart churn. These warnings do not deduplicate, remove, or block your patches and
do not change authentication audit flags.

Duplicate warnings are emitted once per tuple per process, and reinstall warnings
at most once per tuple per five-minute interval. Tracking is bounded to 4,096 tuples
and 128 queued records, with at most four warnings emitted per frame and suppressed
diagnostics summarized. An absent warning does not prove idempotence. Anonymous
MonoMod/native detours cannot always be attributed to a mod; retain their handles,
reuse process-owned installations, and dispose only the resources you own.

## Subsystems

All public APIs live under `Anomaly.Client.Api.*`. Quick pointers:

- **`CommandRegistry`** — in-game console commands. Implement `ICommand`, register with `CommandRegistry.Register(modId, command)`. Canonical invocation is `modId.commandname`; aliases work globally. See `Anomaly.Client/Commands/NameCommand.cs` for a minimal built-in example.
- **`InputRegistry`** — named key bindings with user rebinding via `bindings.json`. Register with `new InputBindingDefinition("mymod.action", "mymod.input.action", new InputKey(KeyCode.F))` and retain the returned `IDisposable`. Dispose during deinitialization after removing named event handlers. Disposal removes the registration without a synthetic `Released` event and keeps the saved override. Poll `WasPressedThisFrame`/`IsPressed` or subscribe to `Pressed`/`Released` on the game thread.
- **`ClientConfig`** — MelonPreferences-backed compatibility layer for typed per-mod settings. New mods should prefer direct MelonPreferences categories for user-facing options.
- **`ClientPersistence`** — MelonPreferences-backed compatibility layer for typed per-mod state. Debounced writes; call `Flush()` if you need the file to be current immediately.
- **`Scheduler`** — delayed and recurring callbacks plus named cooldowns. `Scheduler.In`, `Scheduler.Every`, `Scheduler.StartCooldown`, `Scheduler.GetCooldownRemaining`. Tick-accurate; advances on `Time.unscaledDeltaTime` so pauses don't skip work. Retain and cancel your own `ScheduledTask` handles. Global reset/pause/resume and owner-scoped cleanup are internal framework boundaries.
- **`UiCanvas`** and **`UiLayer`** — the shared Anomaly screen-space canvas. `UiCanvas.GetLayer(UiLayer.HUD)` returns a `RectTransform` parent; instantiate your prefabs under it. Helpers: `UiAnchorExtensions`, `SafeArea`, `UiParameterBinder`, `UiPrefabValidator`.
- **`AnomalyMessaging`** and **`AnomalyMessageRegistry`** — custom client⇆server messages over the Anomaly encrypted channel. See [NETWORKING.md](Networking/NETWORKING.md) for message authoring, wire format, and transport channels.
- **`PlayerPoseRigs`** — discover built-in stable bone IDs and capture model-root-relative final poses after a local solver. Base Anomaly renders server-owned poses without requiring the producing mod; see [the pose contract](../Anomaly.Server/PLAYER_POSES.md).
- **`Tr`** — translations. `Tr.RegisterMod(namespace)` loads `UserData/i18n/<namespace>/`; `Tr.RegisterMod(namespace, dir)` can point at a custom per-locale YAML directory. `Tr.Get(key, args...)` uses exact locale, language-prefix, then English fallback. Missing English keys return the raw key and log a warning. Locale changes require restart in v1. See [LOCALIZATION.md](Localization/LOCALIZATION.md).
- **`AnomalyResources`** — typed native assets from validated indexed Unity bundles. Use `TryGetTexture(AssetAddress, ...)`, `TryGetSprite`, `TryGetAudioClip` and `TryGetMesh`; returned objects are borrowed and remote references expire on disconnect. Author through descriptor-selected `Assets.Index` under `Paths.ContentPacksPath` or server `Config.ContentPackDirectory`. See [ASSETBUNDLES.md](../ASSETBUNDLES.md) for supported native types, validation and lifetimes.
- **`Coroutines`** — `Coroutines.Start(IEnumerator)` / `Coroutines.Stop` / `Coroutines.CallDelayed(float, Action)` wrappers over `MelonCoroutines`.
- **`FeatureHealth`** — metadata-only snapshots of core and managed-module health. Fragile hook records include the expected upstream target, the target actually resolved, current state, and operator/developer remediation where available.
- **Feature wrappers** — `Client.Players`, `Client.LocalPlayer`, `Client.Rooms`, `Client.Doors`, `Client.Pickups`, `Client.Projectiles`, `Client.AdminToys`, plus wrappers for `Item`, `Role`, `PlayerEffect`, `PlayerStatsWrapper`, etc. Read-only views over the game state.

## Local storage

`Paths.AnomalyPath` and `Paths.AnomalyUserDataPath` both resolve to
`Path.Combine(MelonEnvironment.UserDataDirectory, "Anomaly")`, normally
`<game>/UserData/Anomaly`. Use these properties instead of constructing an
AppData path. `Paths.ApplicationDataPath` remains available as informational OS
path data; it is not Anomaly's storage root.

This root contains `user.json` (identity, nickname, and rotation history),
`server-trust.json` (accepted server identities), `content-consent.json`,
`FileCache/` (downloaded server assets), and `CrashReports/` (reports, optional
dumps, and the shutdown marker). Existing bindings, overrides, managed modules,
and their config/data directories retain their locations beneath it. Completed
local asset/script packs, including skins, use `Paths.ContentPacksPath` under
`UserData/Anomaly/ContentPacks`. Translations stay in `UserData/i18n`;
`ClientConfig` and `ClientPersistence` use MelonPreferences.

Each game instance starts a new identity if its local profile is missing and
reuses that profile on later launches. Anomaly does not import, fall back to, or
delete old `%APPDATA%/Anomaly` data. This also means prior server trust and
content approvals are not inherited from that directory.

## Server-local enrollment

`Client.EnrollmentId` exposes the authenticated enrollment id issued by the
current server, or `null` before enrollment disclosure and after disconnect.
It can remain null at `ClientEvents.Ready`; wait for the server's disclosure
before performing operations that require it.
It is deliberately server-local: never treat it as a global account id, derive
it from public identity material, or carry another server's value into a
moderation record. The built-in `anomaly.whoami` command (alias `whoami`)
shows this value and copies it to the clipboard.

The non-secret `identity_public` profile field is for fingerprint/health
display. It is not the enrollment id. Private profile material in
`<MelonLoader UserData>/Anomaly/user.json` remains protected by Windows-user
DPAPI (and by the game's Wine prefix under Proton), so profile operations must
fail closed when that protection context is unavailable. The local location
does not make protected identity material portable across Windows users or Wine
prefixes.

## Server item definitions

Custom items may be declared through a content pack's typed `Data.Items` schema
or registered by a LabAPI companion during `Enable()`:

```csharp
using Anomaly.Server.Api.Content;
using Anomaly.Shared.Assets;

CustomItems.Register(new CustomItemDefinition
{
    Key = "mymod:scanner",
    BaseTemplate = ItemType.Coin,
    DisplayName = "Scanner",
    Description = "Reports nearby activity.",
    IconAddress = AssetAddress.Parse("mods/mymod/scanner")
});
```

The stable key uses the lowercase `modid:name` grammar. Anomaly assigns the
wire item id above the current stock maximum through 127 and clones the stock
`InventoryItemLoader` template. The item therefore inherits the base model,
pickup behavior, and weight. Handle item behavior through ordinary LabAPI
item events and identify the item using `CustomItems.TryGetItemKey`.
Unsubscribe handlers in the companion plugin's `Disable()`.

Recognition and readiness APIs are:

- `CustomItems.TryGetItemId`
- `CustomItems.TryGetItemKey`
- `CustomItems.IsCustomItem`
- `CustomItems.GetPlayerReadiness`
- `Anomaly.Server.Api.Events.ClientEvents.ContentReady`, once per successful
  player catalog activation

`CustomItems.TryGiveItem(player, key, out item)` takes a LabAPI `Player` and
returns the resulting LabAPI item wrapper. It succeeds only after the exact
catalog has activated for that player and server item registration is confirmed.
`ContentReadiness` distinguishes `Unknown`, `Offered`, `Approved`,
`Transferring`, `Declined`, `Activated`, and `Failed`.

The authenticated flow is offer, explicit consent decision, requests for
approved missing files, staged materialization, then an activation ACK for the
exact catalog digest. Consent is bound to the server fingerprint, catalog
SHA-256, and manifest SHA-256. Reusing the exact approved tuple is allowed;
changing any member requires consent again. A decline requests zero files and
activates nothing.

Remote manifests bind validated typed asset indexes/bundles and generated script
indexes/sources, not raw media filenames. Item icons use indexed Texture2D/Sprite
addresses. Limits remain 64 MiB per encoded file and 256 MiB per offered session,
with bounded native validation before activation. Blobs use the instance's
content-addressed `<MelonLoader UserData>/Anomaly/FileCache/`; consent/provenance
remains separate in `Anomaly/content-consent.json` under that UserData directory.

Registration copies each definition. Register during consumer `Enable()`;
after definitions freeze on first use, catalog changes require a server restart.
Catalog offers are immutable for an active player session. Descriptor-declared
file changes rebuild offers for future connections, not destructive hot reload.
Remote custom roles, effects, item models, weights and arbitrary prefabs/components
are unsupported. Restricted static meshes and decorative descriptions are
supported only within the [validated asset subset](../ASSETBUNDLES.md).

Required content activation and declared Lua initialization precede native
Ready/AddPlayer and public `ClientReady`; Lua additionally requires separate
execution approval. Remote Lua requires actual qualified Windows AppContainer/job
isolation; failed/unsupported isolation (including Wine) cannot be overridden by
consent and has no in-process fallback. Local accounting remains unqualified;
local scripts require verified nondedicated in-process host proof. Pending consent
reserves no transfer slot;
admission retains its fixed 120-second deadline. See the
[server API guide](../Anomaly.Server/MODDING.md) and
[descriptor/data schema](../Anomaly.Client.Scripting/SCRIPT-PACKS.md).

## Remote native query bounds

The remote capability bridge and remote event capture enter an internal,
thread-local `ScriptHostAccess` operation. Ordinary trusted C# API use and local
owner/development scripts do not enter this scope and retain their existing API
semantics. No gameplay setter or additional method is exposed by these checks.
Nested scopes share counters rather than resetting the allowance. Exhaustion is
a terminal `ScriptHostAccessException`, not an empty result or a swallowed failure.

The reviewed shipped surface comprises the 21 directly annotated getter families
below, their annotated inherited subtype members, the annotated event argument
records, and the nine `Game` query methods. Unannotated collection/scene-discovery
APIs such as `Room.Players`, `Locker.Chambers`, `List`, `FindObjectsOfType`, and raw
Unity objects are not remotely callable.

| Reviewed family | Parent work and boundary |
|---|---|
| `Player` / inherited `LocalPlayer`; `Role`; `PlayerStat`; `PlayerEffect` | Scalar native fields, fixed role/movement lookups, enum/type names and policy checks. Local-player resolution, fallback identity scans and promotion/remapping have a shared 512-entry operation budget; remote ID lookup uses an explicit bounded hub scan instead of an opaque native ID search. Scoped failures propagate through legacy availability catches. Stat lookup preflights native module array length. |
| `Item` and its inherited item subtypes; `Pickup` and its inherited pickup/projectile subtypes; `FirearmState` | Scalar fields and readonly state snapshots. Modular item permission/weight reads and firearm module queries preflight the native subcomponent array length. Subtype dispatch uses fixed factory lists, not assembly/type discovery. Serial-index growth is checked before dictionary insertion. |
| `Room`; `Generator`; `Locker`; `Elevator`; `Tesla`; `Hazard` and inherited hazards; `Workstation` | Room identifiers and scalar state. Generator/locker ancestry walks charge before each component lookup and fail beyond 64 total steps. Position-to-room resolution preflights the native room registry size before calling the game lookup. No chamber/room/player collection snapshot is exposed. |
| `Door` and inherited doors; `AdminToy` and inherited toys; `FacilityCamera`; `Ragdoll`; `AnnouncerWord` | Scalar state and native names/labels. All IL2CPP-to-managed string copies passing through the guarded conversion preflight their native UTF-16 length. Cached native object identities are reused; cache misses are bounded before wrapper construction and potential dictionary growth. |
| `MotionDiscontinuity`; `VoiceMessagePacket`; annotated event argument records | Stored readonly scalar/wrapper references and fixed arithmetic. Computed names (`ClipName`, `SceneName`, announcer names) use the same native string guard. Interaction-target getters can create door/locker/generator/workstation wrappers and therefore share the capture operation's wrapper/cache allowance. Event construction performed by the game before publication is not charged as a remote query. |
| Nine `Game` methods: `GetPlayer`, `GetPlayerAt`, `GetCurrentItem`, `GetRoom`, `GetFirearmState`, `GetHealth`, `GetHumeShield`, `GetStamina`, `GetPosition` | ID resolution uses the bounded registry path; indexed access and cached player count are constant work. Item/inventory, room, stat and role cache misses share the same allowance. Position and firearm results have fixed-size fields; stat synchronization/visibility policy is unchanged. Round flags are cached scalar reads. |

One operation permits at most four new API wrappers, 64 hierarchy steps and
512 aggregate registry/module/cache-growth entries. Existing cache hits do not
recreate wrappers. These are operation limits, not new global cache sizes or
per-session heap measurements. Existing trusted game lifecycle eviction remains
responsible for the shared caches.

The Client patches the real
`Il2CppInterop.Runtime.IL2CPP.Il2CppStringToManaged(IntPtr)` entry point. Its prefix
calls `il2cpp_string_length` and rejects lengths outside 0..4096 **before** the
interop method constructs a managed string. Null pointers retain the original
null behavior. Outside a remote scope the prefix does not perform a native length
read. The exact prefix/target tuple is a mandatory startup hook registered with
Bootstrap's existing verifier; a missing hook fails startup rather than enabling
unbounded remote string conversion. Protocol UTF-8/result-size bounds are separate.

These checks do not intercept native allocation, IL2CPP array/proxy allocation,
or arbitrary C#/native getters. They assume the shipped game/Unity/interop calls
used for scalar reads, casts, collection iteration, native string-length reads,
room lookup, module lookup, stat visibility and spectator tracking are trusted and
return. Native virtual getters may construct native temporary strings or other
state before conversion; the managed-copy guard does not bound that native work.
The local exported game stubs establish signatures/collection fields but contain
`AnalysisFailedException` bodies for several native queries, so they are not
evidence of native instruction counts or allocation ceilings. Harmony installation
and the real oversized-string path must also be exercised in the supported game;
source/unit checks alone do not establish runtime detouring or absence of native
inlining. The external worker's hard process limits do not interrupt a native
operation already executing in the game process.

## Events

All handlers are under `Anomaly.Client.Api.Events.Handlers.*`. Ordinary
MelonMods normally subscribe in `OnInitializeMelon` for the process lifetime.
Managed modules should use the typed `IModContext.Subscribe` overload so the
wrapped, owner-contained delegate is unregistered automatically on disposal.

| Handler | Fires when |
|---|---|
| `ClientEvents` | `SceneLoaded`, `Connected`, `Disconnected`, `Ready` (client scene-ready signal), `CanvasReady` |
| `PlayerEvents` | `Added`, `Removed` |
| `RoleEvents` | `Changed` |
| `StatsEvents` | `Healed`, `StaminaChanged`, `HumeShieldChanged` |
| `EffectEvents` | `Enabled`, `Disabled`, `IntensityChanged` |
| `InventoryEvents` | `ItemsChanged`, `CurrentItemChanged` |
| `PickupEvents` | `Added`, `Removed` |
| `ProjectileEvents` | `Spawned`, `Removed` |
| `FirearmEvents` | `Shot` |
| `AdminToyEvents` | `Added`, `Removed` |
| `RoundEvents` | `PhaseChanged` + per-phase convenience events |
| `VoiceEvents` | `Sending`, `Sent`, `Receiving`, `Received` |
| `NetworkMessageEvents` | `AnomalyMessageReceived` (any custom Anomaly message) |
| `FileTransferEvents` | `Progress`, `Complete` |
| `AssetEvents` | `OverrideApplied`, `OverrideCleared` |
| `AnnouncerEvents` | `LineScheduled`, `LineStarted` |
| `WarheadEvents` | `ProgressChanged`, `Detonated` |
| `GeneratorEvents` | `Engaged` |
| `TeslaEvents` | `Added`, `Removed`, `Bursted` |
| `ElevatorEvents` | `Spawned`, `Removed`, `Moved` |
| `FacilityCameraEvents` | `Created`, `Removed`, `StateChanged` |
| `RagdollEvents` | `Spawned`, `Removed` |

Handlers run on the Unity main thread. Exceptions in your handler are caught and logged by `EventInvoker`; they don't propagate to other subscribers. You're still responsible for not corrupting your own state.

## Character model

These helpers operate on an already-present vanilla character model from a
trusted local mod. They do not register or transfer remote custom models,
meshes, prefabs, roles, or effects.

If your mod manipulates a player's character model — IK overrides, custom bones, third-person camera attachments, body scaling, finger pose overrides — you'll hit several non-obvious behaviours in SCP:SL's animation pipeline. Use the `CharacterModels` static helpers and the `IModelHook` interface; both encode patterns that the SecretLabVR mod had to discover the hard way.

**The animator runs from `CullingSubcontroller`, not Unity's standard timing.** SCP:SL drives the animator manually from `CullingSubcontroller.LateUpdate` via per-instance `OnBeforeAnimatorUpdated` and `OnAnimatorUpdated` events. Mods that write bones from a plain `MonoBehaviour.LateUpdate` either get clobbered by the next animator pass (writes too early) or fight the cycle. Implement `IModelHook` and call `CharacterModels.RegisterLocalHook(hook)`; the framework auto-attaches and re-attaches across role changes.

**Call `CharacterModels.SyncSecondaryRigs(model)` after writing bones.** SCP:SL's `SecondaryRigsSubcontroller` mirrors helper bones; if you don't call `MatchAll()` after your bone writes, you get asymmetric flicker on the body.

**Suppress the competing `LookatSubcontroller`** if your mod is driving head/torso. `CharacterModels.SuppressLookAt(model, true)` disables the native solver so it doesn't add chest/neck motion on top of yours.

**Use the existing animator layers for hand poses.** The avatar already has `"Left Hand Pose - (RefId=44175)"` and `"Right Hand Pose - (RefId=25842)"` layers driven by `LeftHandPose` / `RightHandPose` time parameters and the `hand/PoseLib` clip. Override their layer weights instead of fighting the raw finger bones — it's cheaper and survives locomotion animation. Cache layer indices via `CharacterModels.GetAnimatorLayerIndex(animator, layerName)`.

**`HandPoseSubcontroller` is event-driven, not Update-driven.** Disabling the component does NOT stop its writes (it fires from `CullingSubcontroller.OnBeforeAnimatorUpdated`). Override the hand pose layer weights afterward instead of trying to disable the subcontroller.

**`FpcMouseLook.CurrentVertical` uses opposite sign from world-space camera X pitch.** Negate when reading or writing pitch through `MouseLook`. Confusing, but consistent across the codebase.

**Don't recenter from the animated head bone.** Walk animation moves the head bone forward/back; if you recenter from it every frame, the whole model drags behind the camera. Cache a stable head anchor at `Animator.GetBoneTransform(HumanBodyBones.Hips).parent` (the primary armature root) instead of the animated `Head` bone.

**For visual-only crouch, keep root correction in XZ only.** Spread residual height delta through `Spine` / `Chest` / `UpperChest` / `Neck`. Moving the root down breaks leg animation.

## IL2CPP gotchas

- **Coroutines:** `MelonCoroutines.Start(enumerator)`, not `StartCoroutine`. Or use the `Coroutines` helper in Anomaly.
- **Cross-scene state:** `UnityEngine.Object.DontDestroyOnLoad(go)` on any GameObject you want to survive scene loads.
- **Custom `MonoBehaviour`s:** decorate the class with `[RegisterTypeInIl2Cpp]` so IL2CPP registers the type; add the two-constructor pattern (`(IntPtr)` + parameterless) like `AuthStatusOverrideComponent`.
- **Managed collections on a registered `MonoBehaviour`:** mark the method with `[HideFromIl2Cpp]` so IL2CPP skips trying to rewrite it. Same pattern applies to any method using `Dictionary<K,V>`, `List<T>`, etc. on a `[RegisterTypeInIl2Cpp]` class.
- **Event handler delegates on IL2CPP events:** use `DelegateSupport.ConvertDelegate<T>` when binding a managed method to an IL2CPP-declared event. See `ClientRuntime.Initialize` for the pattern with `ShotEventManager.OnShot`.

## Buildable example and verification

Use [HelloAnomalyMod](../samples/HelloAnomalyMod/README.md) as the starting point.
Its source owns a command, disposable F6 binding, named event handlers, one
MelonPreferences entry, and a recurring task started at `ClientEvents.Ready`.
Disconnect cancels the task; reconnect creates one replacement. Deinitialization
unsubscribes events, cancels work, disposes the input handle, and unregisters commands.

After installing it, run `example.hello.hello` or press F6, then change its greeting
setting under **Settings > Mods > Hello Anomaly**. Changes apply immediately and
save after a short debounce. Reset restores one entry's default. Invalid edits
stay visible with an explanation and do not overwrite the previous value; save
failures expose a retry action. Descriptions and current/default values are shown
beside each setting. Metadata is copied at registration, so re-register it to
apply later metadata changes.

Custom numeric choices store zero-based indices, string choices store labels,
and enum choices use exact enum names. Do not combine choices with numeric
bounds. Bounds must be a finite increasing pair; long and decimal entries use
text fields to preserve precision.

Use `anomaly.status` or `anomaly.status --copy` when testing connection, readiness,
and feature availability. Its report excludes identities, server addresses, and
arbitrary module health details. Include the summary and reproduction steps with
an early access bug report. Builds and automated tests establish API compatibility;
test the actual mod in the selected game runtime before distributing it.

## Server-side companion

If your mod needs a server component, build a LabAPI plugin that references
`Anomaly.Server.dll` and `Anomaly.Shared.dll`. The released server package
places those assemblies at `plugins/global/Anomaly.Server.dll` and
`dependencies/global/Anomaly.Shared.dll` relative to the LabAPI root.
EXILED compatibility is not a public-v1 contract.

The shared `AnomalyMessageRegistry` and `IAnomalyMessage` contracts are
identical on both sides. Every message must use an authenticated directional
base and declare a payload ceiling; see
[NETWORKING.md](Networking/NETWORKING.md).

Public remote password authentication is disabled. A server with
`RequirePassword=true` rejects remote clients until vetted OPAQUE support is
available; the legacy password path is restricted to explicit loopback or
migration use.

## Bundle resources

Use typed catalog addresses with AnomalyResources.TryGetTexture, TryGetSprite, TryGetAudioClip and TryGetMesh. Objects are borrowed and remote bindings must be released on disconnect. The coordinated protocol 6 migration requires dependent mods to rebuild; [ASSETBUNDLES.md](../ASSETBUNDLES.md) describes authoring, replacements, budgets and the no-script policy.
