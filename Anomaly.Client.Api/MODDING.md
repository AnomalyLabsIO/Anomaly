# Modding against Anomaly.Client.Api

## What this library is

`Anomaly.Client.Api` is the public **peer library** that exposes event buses, feature wrappers, registries, and helpers to other MelonLoader mods targeting the Anomaly-enabled SCP:SL client. `Anomaly.Client` is the runtime mod that owns the MelonMod lifecycle — Harmony patches, auth, diagnostics, network handlers — and raises events on the Api surface. Your mod links against `Anomaly.Client.Api.dll` (plus `Anomaly.Shared.dll`) and never needs to touch `Anomaly.Client.dll`.

Existing mods can remain normal IL2CPP MelonMods and use Anomaly's static helpers directly. Public-v1 modules can instead implement `IAnomalyMod`; Anomaly then owns their initialization, cleanup, dependency ordering, and fault attribution.

## Prerequisites

1. Install the modded client through the Anomaly Launcher (see the repo README).
2. Start the modded game once so MelonLoader generates `Il2CppAssemblies/` and `UserLibs/`.
3. Set up a MelonLoader IL2CPP project. The fastest path is MelonLoader's Visual Studio template (see [melonwiki.xyz](https://melonwiki.xyz)) pointed at the modded `SCPSL.exe`.
4. Add references to `Anomaly.Client.Api.dll` and `Anomaly.Shared.dll` (both from `UserLibs/`). You do not reference `Anomaly.Client.dll` directly — it loads as a peer mod at runtime.

## Load order

MelonLoader calls `OnInitializeMelon` in dependency order. If you call any Anomaly API during `OnInitializeMelon`, declare the dependency explicitly:

```csharp
using MelonLoader;
using Anomaly.Client.Api.Compatibility;

[assembly: MelonInfo(typeof(MyMod.Core), "MyMod", "1.0.0", "YourName", null)]
[assembly: MelonGame("Northwood", "SCPSL")]
[assembly: SupportedGameVersion("14.2.7^")]
[assembly: MelonAdditionalDependencies("Anomaly")]
```

The string `"Anomaly"` must match Anomaly.Client's own `MelonInfo` name. Without the attribute, load order is undefined and your mod may hit Anomaly's APIs before its own `OnInitializeMelon` has finished running — Harmony patches won't be applied, message registry won't be populated, event handlers won't be wired.

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

The launcher verifies signed official-core packages and approves third-party
artifacts by SHA-256. Before process creation it stages or quarantines every
MelonLoader-scanned DLL, including `UserLibs`, `Plugins`, `Mods`, nested
folders that MelonLoader reclassifies, and managed modules.
`Anomaly.Bootstrap` repeats the check for ordinary Mods and managed modules
before the Mods phase; it is defense in depth, not the authority for
earlier-loading `UserLibs` or `Plugins`. A changed third-party binary
requires renewed consent.

Trust state lives at
`<modded-game>/UserData/Anomaly/mod-trust.json`; quarantine is under
`<modded-game>/UserData/Anomaly/Quarantine/`. Repeated managed-module startup
failures move to
`Quarantine/managed-runtime/repeated-startup-failure/`. Approval is a
full-process trust decision, not a sandbox: approved managed code and ordinary
MelonMods have the authority of the game process.

## Picking an id

Your mod picks a lowercase dotted namespace (regex: `^[a-z][a-z0-9_]{0,31}(\.[a-z][a-z0-9_]{0,31}){0,39}$`). Examples: `mymod`, `acme.wargames`, `team_alpha.ui`.

Use it as the prefix on every Anomaly registration that takes a `modId` or a namespaced key:

```csharp
CommandRegistry.Register("mymod", new MyCommand());
InputRegistry.Register(new InputBindingDefinition { Id = "mymod.jump", ... });
MelonPreferences.CreateCategory("mymod", "MyMod");
ClientConfig.For("mymod").Set("volume", 0.75f);        // compatibility facade
ClientPersistence.For("mymod").Save("loadout", myData); // compatibility facade
Scheduler.StartCooldown("mymod.ability", TimeSpan.FromSeconds(5));
Tr.RegisterMod("mymod");
```

There's no "register my mod" call. The registries enforce that ids look like a namespace — they don't maintain a global mod list. The reserved `anomaly` / `anomaly.*` prefix is used by framework built-ins and is rejected if your mod uses it.

## Lifecycle

Anomaly's own init happens inside its `OnInitializeMelon`: localization service, Harmony patches, auth handler, `ClientRuntime`, diagnostics, `UiBootstrap`. By the time your `OnInitializeMelon` runs (assuming you declared the dependency), everything is ready to subscribe to.

| Phase | Safe to do | Not safe |
|---|---|---|
| Your `OnInitializeMelon` | Register commands/input/cooldowns; create MelonPreferences categories or `ClientConfig.For` / `ClientPersistence.For`; `Tr.RegisterMod`; subscribe to any `*Events` | Touch `LocalPlayer.Instance` (local hub not spawned); read `Client.Players` (empty); instantiate UI (canvas doesn't exist yet) |
| `ClientEvents.CanvasReady` | `UiCanvas.GetLayer(...)`, instantiate UI prefabs under a layer | Local player still not ready |
| `ClientEvents.Connected` | `AnomalyMessaging.Send` is valid once encryption is negotiated | Don't assume the local hub is fully initialized yet |
| `ClientEvents.Ready` | Full use of `LocalPlayer.Instance`, `Client.Players`, `Client.Rooms`, `Client.Doors`, etc. | — |
| `ClientEvents.Disconnected` | Clear your own state and cancel external work | Client persistence is flushed before framework-owned scheduled state is reset; do not rely on tasks or cooldowns surviving |

**No replay-on-subscribe.** If you subscribe to `CanvasReady`, `Connected`, or `Ready` after it has already fired, you won't get a late-dispatch call. Subscribe in `OnInitializeMelon`.

**No assembly hot-unload.** A managed module can be disabled immediately at Anomaly-owned boundaries: its cancellation token is cancelled, commands and scheduled work are removed, tracked subscriptions/resources are disposed, and future owned callbacks are rejected. Its assembly remains loaded in the IL2CPP process, so complete removal takes effect on the next launch. Ordinary MelonMods remain governed by MelonLoader's process-lifetime behavior.

## Subsystems

All public APIs live under `Anomaly.Client.Api.*`. Quick pointers:

- **`CommandRegistry`** — in-game console commands. Implement `ICommand`, register with `CommandRegistry.Register(modId, command)`. Canonical invocation is `modId.commandname`; aliases work globally. See `Anomaly.Client/Commands/NameCommand.cs` for a minimal built-in example.
- **`InputRegistry`** — named key bindings with user rebinding via `bindings.json`. `InputRegistry.Register(new InputBindingDefinition { Id = "mymod.action", DefaultKey = new InputKey { PrimaryKey = KeyCode.F } })`; poll `WasPressedThisFrame`/`IsPressed` or subscribe to the `Pressed`/`Released` events.
- **`ClientConfig`** — MelonPreferences-backed compatibility layer for typed per-mod settings. New mods should prefer direct MelonPreferences categories for user-facing options.
- **`ClientPersistence`** — MelonPreferences-backed compatibility layer for typed per-mod state. Debounced writes; call `Flush()` if you need the file to be current immediately.
- **`Scheduler`** — delayed and recurring callbacks plus named cooldowns. `Scheduler.In`, `Scheduler.Every`, `Scheduler.StartCooldown`, `Scheduler.GetCooldownRemaining`. Tick-accurate; advances on `Time.unscaledDeltaTime` so pauses don't skip work. Retain and cancel your own `ScheduledTask` handles. Global reset/pause/resume and owner-scoped cleanup are internal framework boundaries.
- **`UiCanvas`** and **`UiLayer`** — the shared Anomaly screen-space canvas. `UiCanvas.GetLayer(UiLayer.HUD)` returns a `RectTransform` parent; instantiate your prefabs under it. Helpers: `UiAnchorExtensions`, `SafeArea`, `UiParameterBinder`, `UiPrefabValidator`.
- **`AnomalyMessaging`** and **`AnomalyMessageRegistry`** — custom client⇆server messages over the Anomaly encrypted channel. See [NETWORKING.md](Networking/NETWORKING.md) for message authoring, wire format, and transport channels.
- **`Tr`** — translations. `Tr.RegisterMod(namespace)` loads `UserData/i18n/<namespace>/`; `Tr.RegisterMod(namespace, dir)` can point at a custom per-locale YAML directory. `Tr.Get(key, args...)` uses exact locale, language-prefix, then English fallback. Missing English keys return the raw key and log a warning. Locale changes require restart in v1. See [LOCALIZATION.md](Localization/LOCALIZATION.md).
- **`AnomalyResources`** — content-addressed PNG texture and OGG audio loading, populated by the file-transfer layer. Use `TryGetTexture(hash, ...)` and `TryGetAudioClip(hash, ...)`. Remote AssetBundles are not part of public v1.
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
and their config/data directories retain their locations beneath it. Skins stay
in `UserData/Skins`, translations in `UserData/i18n`, and `ClientConfig` and
`ClientPersistence` continue to store values through MelonPreferences.

Each game instance starts a new identity if its local profile is missing and
reuses that profile on later launches. Anomaly does not import, fall back to, or
delete old `%APPDATA%/Anomaly` data. This also means prior server trust and
content approvals are not inherited from that directory.

## Server-local enrollment

`Client.EnrollmentId` exposes the authenticated enrollment id issued by the
current server, or `null` before enrollment disclosure and after disconnect.
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

## Items-only server content

Public v1 custom content is server-registered items. A LabAPI companion calls
`Anomaly.Server.Content.Content.Register` during `Enable()`:

```csharp
using Anomaly.Server.Content;
using InventorySystem;

Content.Register(new CustomItemDefinition
{
    Key = "mymod:scanner",
    BaseTemplate = ItemType.Coin,
    DisplayName = "Scanner",
    Description = "Reports nearby activity.",
    IconPath = "mymod/scanner.png"
});
```

The stable key uses the lowercase `modid:name` grammar. Anomaly assigns the
wire item id above the current stock maximum through 127 and clones the stock
`InventoryItemLoader` template. The item therefore inherits the base model,
pickup behavior, and weight. Optional `ItemLogicBase.OnUsed` runs from the
LabAPI used-item event.

Recognition and readiness APIs are:

- `Content.TryGetItemId`
- `Content.TryGetItemKey`
- `Content.IsCustomItem`
- `Content.GetPlayerReadiness`
- `Content.PlayerReady`, once per successful catalog activation

`Content.GiveItem` succeeds only after the exact catalog has activated for
that player and the server-side item registration is confirmed.
`ContentReadiness` distinguishes `Unknown`, `Offered`, `Approved`,
`Transferring`, `Declined`, `Activated`, and `Failed`.

The authenticated flow is offer, explicit consent decision, requests for
approved missing files, staged materialization, then an activation ACK for the
exact catalog digest. Consent is bound to the server fingerprint, catalog
SHA-256, and manifest SHA-256. Reusing the exact approved tuple is allowed;
changing any member requires consent again. A decline requests zero files and
activates nothing.

Only PNG and OGG files can appear in a remote v1 manifest. Item icons are PNG.
Limits are 64 MiB per encoded file and 256 MiB across the manifest, with decoded
allocation checks before activation. Blobs use the instance's content-addressed
`<MelonLoader UserData>/Anomaly/FileCache/`, while consent/provenance remains
separate in `Anomaly/content-consent.json` beneath the same UserData directory.

Catalogs are immutable for an active connection. File changes and deletions
rebuild the offer for future connections; public v1 does not destructively hot
reload connected clients. Remote custom roles, effects, models, weights,
AssetBundles, meshes, generic prefabs, and generic spawning are not supported.

## Events

All handlers are under `Anomaly.Client.Api.Events.Handlers.*`. Ordinary
MelonMods normally subscribe in `OnInitializeMelon` for the process lifetime.
Managed modules should use the typed `IModContext.Subscribe` overload so the
wrapped, owner-contained delegate is unregistered automatically on disposal.

| Handler | Fires when |
|---|---|
| `ClientEvents` | `SceneLoaded`, `Connected`, `Disconnected`, `Ready` (in-game ready), `CanvasReady` |
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

## Example

Minimal MelonMod that logs every role change and runs a recurring health tick while in-game:

```csharp
using System;
using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Compatibility;
using Anomaly.Client.Api.Scheduling;
using Anomaly.Client.Api.Events.Handlers;
using MelonLoader;

[assembly: MelonInfo(typeof(MyMod.Core), "MyMod", "0.1.0", "YourName", null)]
[assembly: MelonGame("Northwood", "SCPSL")]
[assembly: SupportedGameVersion("14.2.7^")]
[assembly: MelonAdditionalDependencies("Anomaly")]

namespace MyMod;

public class Core : MelonMod
{
    public override void OnInitializeMelon()
    {
        RoleEvents.Changed += args =>
            MelonLogger.Msg($"Role change: {args.Player?.Role?.RoleType} on {args.Player?.ReferenceHub?.nicknameSync?.MyNick}");

        ClientEvents.Ready += _ =>
        {
            Scheduler.Every(TimeSpan.FromSeconds(1), LogLocalHealth);
        };
    }

    private static void LogLocalHealth()
    {
        var lp = Client.LocalPlayer;
        if (lp == null) return;
        MelonLogger.Msg($"HP: {lp.Health}/{lp.MaxHealth}, HS: {lp.HumeShield}/{lp.MaxHumeShield}");
    }
}
```

The `Scheduler.Every` task above is cleared automatically when the client disconnects. If you want it across sessions, re-register it in the next `ClientEvents.Ready`.

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
