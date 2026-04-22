# Modding against Anomaly.Client.Api

## What this library is

`Anomaly.Client.Api` is the public **peer library** that exposes event buses, feature wrappers, registries, and helpers to other MelonLoader mods targeting the Anomaly-enabled SCP:SL client. `Anomaly.Client` is the runtime mod that owns the MelonMod lifecycle — Harmony patches, auth, diagnostics, network handlers — and raises events on the Api surface. Your mod links against `Anomaly.Client.Api.dll` (plus `Anomaly.Shared.dll`) and never needs to touch `Anomaly.Client.dll`.

No interface to implement. No attribute to decorate. You build a normal IL2CPP MelonMod and use Anomaly's static helpers directly.

## Prerequisites

1. Install the modded client through the Anomaly Launcher (see the repo README).
2. Start the modded game once so MelonLoader generates `Il2CppAssemblies/` and `UserLibs/`.
3. Set up a MelonLoader IL2CPP project. The fastest path is MelonLoader's Visual Studio template (see [melonwiki.xyz](https://melonwiki.xyz)) pointed at the modded `SCPSL.exe`.
4. Add references to `Anomaly.Client.Api.dll` and `Anomaly.Shared.dll` (both from `UserLibs/`). You do not reference `Anomaly.Client.dll` directly — it loads as a peer mod at runtime.

## Load order

MelonLoader calls `OnInitializeMelon` in dependency order. If you call any Anomaly API during `OnInitializeMelon`, declare the dependency explicitly:

```csharp
using MelonLoader;

[assembly: MelonInfo(typeof(MyMod.Core), "MyMod", "1.0.0", "YourName", null)]
[assembly: MelonGame("Northwood", "SCPSL")]
[assembly: MelonAdditionalDependencies("Anomaly")]
```

The string `"Anomaly"` must match Anomaly.Client's own `MelonInfo` name. Without the attribute, load order is undefined and your mod may hit Anomaly's APIs before its own `OnInitializeMelon` has finished running — Harmony patches won't be applied, message registry won't be populated, event handlers won't be wired.

Mods that only call Anomaly APIs from event handlers (i.e. not during `OnInitializeMelon`) don't strictly need the attribute, but declaring it costs nothing and avoids a future footgun.

## Picking an id

Your mod picks a lowercase dotted namespace (regex: `^[a-z][a-z0-9_]{0,31}(\.[a-z][a-z0-9_]{0,31}){0,39}$`). Examples: `mymod`, `acme.wargames`, `team_alpha.ui`.

Use it as the prefix on every Anomaly registration that takes a `modId` or a namespaced key:

```csharp
CommandRegistry.Register("mymod", new MyCommand());
InputRegistry.Register(new InputBindingDefinition { Id = "mymod.jump", ... });
ClientConfig.For("mymod").Set("volume", 0.75f);
ClientPersistence.For("mymod").Save("loadout", myData);
Scheduler.StartCooldown("mymod.ability", TimeSpan.FromSeconds(5));
Tr.RegisterMod("mymod", myI18nDir);
```

There's no "register my mod" call. The registries enforce that ids look like a namespace — they don't maintain a global mod list. The reserved `anomaly` / `anomaly.*` prefix is used by framework built-ins and is rejected if your mod uses it.

## Lifecycle

Anomaly's own init happens inside its `OnInitializeMelon`: localization service, Harmony patches, auth handler, `ClientRuntime`, diagnostics, `UiBootstrap`. By the time your `OnInitializeMelon` runs (assuming you declared the dependency), everything is ready to subscribe to.

| Phase | Safe to do | Not safe |
|---|---|---|
| Your `OnInitializeMelon` | Register commands/input/cooldowns; create `ClientConfig.For` / `ClientPersistence.For`; `Tr.RegisterMod`; subscribe to any `*Events` | Touch `LocalPlayer.Instance` (local hub not spawned); read `Client.Players` (empty); instantiate UI (canvas doesn't exist yet) |
| `ClientEvents.CanvasReady` | `UiCanvas.GetLayer(...)`, instantiate UI prefabs under a layer | Local player still not ready |
| `ClientEvents.Connected` | `AnomalyMessaging.Send` is valid once encryption is negotiated | Don't assume the local hub is fully initialized yet |
| `ClientEvents.Ready` | Full use of `LocalPlayer.Instance`, `Client.Players`, `Client.Rooms`, `Client.Doors`, etc. | — |
| `ClientEvents.Disconnected` | Clear your own state | Your `Scheduler` tasks and cooldowns have already been cleared by the framework — don't rely on them surviving |

**No replay-on-subscribe.** If you subscribe to `CanvasReady`, `Connected`, or `Ready` after it has already fired, you won't get a late-dispatch call. Subscribe in `OnInitializeMelon`.

**No hot-unload.** MelonLoader doesn't support unloading mods mid-session, so Anomaly doesn't either. Anything you register lives for the rest of the game session.

## Subsystems

All public APIs live under `Anomaly.Client.Api.*`. Quick pointers:

- **`CommandRegistry`** — in-game console commands. Implement `ICommand`, register with `CommandRegistry.Register(modId, command)`. Canonical invocation is `modId.commandname`; aliases work globally. See `Anomaly.Client/Commands/NameCommand.cs` for a minimal built-in example.
- **`InputRegistry`** — named key bindings with user rebinding via `bindings.json`. `InputRegistry.Register(new InputBindingDefinition { Id = "mymod.action", DefaultKey = new InputKey { PrimaryKey = KeyCode.F } })`; poll `WasPressedThisFrame`/`IsPressed` or subscribe to the `Pressed`/`Released` events.
- **`ClientConfig`** — per-mod JSON config at `<UserData>/Anomaly/<modId>/config.json`. Hot-reloaded via `FileSystemWatcher`; subscribe to `IConfigSource.KeyChanged`.
- **`ClientPersistence`** — per-mod JSON state at `<UserData>/Anomaly/<modId>/data.json`. Debounced writes; call `Flush()` if you need the disk to be current immediately.
- **`Scheduler`** — delayed and recurring callbacks plus named cooldowns. `Scheduler.In`, `Scheduler.Every`, `Scheduler.StartCooldown`, `Scheduler.GetCooldownRemaining`. Tick-accurate; advances on `Time.unscaledDeltaTime` so pauses don't skip work. `ClearAll` runs automatically on disconnect; `PauseAll`/`ResumeAll` are your own escape hatch if you need a transient freeze.
- **`UiCanvas`** and **`UiLayer`** — the shared Anomaly screen-space canvas. `UiCanvas.GetLayer(UiLayer.HUD)` returns a `RectTransform` parent; instantiate your prefabs under it. Helpers: `UiAnchorExtensions`, `SafeArea`, `UiParameterBinder`, `UiPrefabValidator`.
- **`AnomalyMessaging`** and **`AnomalyMessageRegistry`** — custom client⇆server messages over the Anomaly encrypted channel. See [NETWORKING.md](Networking/NETWORKING.md) for message authoring, wire format, and transport channels.
- **`Tr`** — translations. `Tr.RegisterMod(namespace, dir)` points the service at your per-locale JSON files; `Tr.Get(key, args...)` looks up translated strings with fallback to English. See [LOCALIZATION.md](Localization/LOCALIZATION.md).
- **`AnomalyResources`** — content-addressed texture and asset-bundle loading, populated by the file-transfer layer. `TryGetTexture(hash, ...)`, `TryLoadFromBundle<T>(bundleHash, assetName, ...)`.
- **`Coroutines`** — `Coroutines.Start(IEnumerator)` / `Coroutines.Stop` / `Coroutines.CallDelayed(float, Action)` wrappers over `MelonCoroutines`.
- **Feature wrappers** — `Client.Players`, `Client.LocalPlayer`, `Client.Rooms`, `Client.Doors`, `Client.Pickups`, `Client.Projectiles`, `Client.AdminToys`, plus wrappers for `Item`, `Role`, `PlayerEffect`, `PlayerStatsWrapper`, etc. Read-only views over the game state.

## Events

All under `Anomaly.Client.Api.Events.Handlers.*`. Subscribe in `OnInitializeMelon`, unsubscribe is optional (mods don't unload mid-session).

| Handler | Fires when |
|---|---|
| `ClientEvents` | `SceneLoaded`, `Connected`, `Disconnected`, `Ready` (in-game ready), `CanvasReady`, `CreditHook` |
| `PlayerEvents` | `Added`, `Removed` |
| `RoleEvents` | `Changed` |
| `StatsEvents` | `Damaged`, `Died`, `Healed` |
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
| `AssetEvents` | `Spawned`, `Despawned`, `Reloaded`, `OverrideApplied`, `OverrideCleared` |

Handlers run on the Unity main thread. Exceptions in your handler are caught and logged by `EventInvoker`; they don't propagate to other subscribers. You're still responsible for not corrupting your own state.

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
using Anomaly.Client.Api.Scheduling;
using Anomaly.Client.Api.Events.Handlers;
using MelonLoader;

[assembly: MelonInfo(typeof(MyMod.Core), "MyMod", "0.1.0", "YourName", null)]
[assembly: MelonGame("Northwood", "SCPSL")]
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
            Scheduler.Every(TimeSpan.FromSeconds(1), LogLocalHealth, ownerId: "mymod");
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

If your mod needs a server component, build a LabAPI plugin that references `Anomaly.Server.dll` and `Anomaly.Shared.dll`. The shared `AnomalyMessageRegistry` and `IAnomalyMessage` contracts are identical on both sides — see [NETWORKING.md](Networking/NETWORKING.md).
