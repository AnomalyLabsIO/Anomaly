using System;
using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Input;
using Anomaly.Client.Api.Scheduling;
using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Client.Internal.Effects;
using Anomaly.Client.Api.Internal.Input;
using Anomaly.Client.Api.Internal.Scheduling;
using Anomaly.Client.Internal.Stats;
using Anomaly.Client.Api.Internal.Wrappers;
using Il2CppAdminToys;
using Il2CppCassie;
using Il2CppCustomPlayerEffects;
using Il2Cpp;
using Il2CppInteractables.Interobjects;
using Il2CppInventorySystem;
using Il2CppInventorySystem.Items;
using Il2CppInventorySystem.Items.Firearms.ShotEvents;
using Il2CppInventorySystem.Items.Pickups;
using Il2CppInventorySystem.Items.ThrowableProjectiles;
using Il2CppMapGeneration.Distributors;
using Il2CppPlayerRoles;
using Il2CppPlayerRoles.PlayableScps.Scp079.Cameras;
using Il2CppPlayerStatsSystem;
using Il2CppInterop.Runtime;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;
using ClientApi = global::Anomaly.Client.Api.Features.Client;
using PlayerWrapper = global::Anomaly.Client.Api.Features.Player;

namespace Anomaly.Client.Internal.Bootstrap;

internal static class ClientRuntime
{
    private static bool _initialized;

    internal static bool IsInitialized => _initialized;

    internal static void Initialize()
    {
        if (_initialized)
            return;

        _initialized = true;

        ShotEventManager.OnShot += DelegateSupport.ConvertDelegate<ShotEventManager.Shot>(
            new Action<ShotEvent>(HandleFirearmShot));

        try
        {
            CassieLineSource.OnStartedPlaying += DelegateSupport.ConvertDelegate<Il2CppSystem.Action<CassieLineSource>>(
                new Action<CassieLineSource>(HandleAnnouncerLineStarted));
        }
        catch (Exception ex)
        {
            MelonLogger.Warning($"ClientRuntime.Initialize: failed to hook CassieLineSource.OnStartedPlaying: {ex.Message}");
        }

        TryHookStaticEvent("AlphaWarheadController.OnProgressChanged", () =>
            AlphaWarheadController.OnProgressChanged += DelegateSupport.ConvertDelegate<Il2CppSystem.Action<bool>>(
                new Action<bool>(HandleWarheadProgressChanged)));

        TryHookStaticEvent("AlphaWarheadController.OnDetonated", () =>
            AlphaWarheadController.OnDetonated += DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(
                new Action(HandleWarheadDetonated)));

        TryHookStaticEvent("TeslaGate.OnAdded", () =>
            TeslaGate.OnAdded += DelegateSupport.ConvertDelegate<Il2CppSystem.Action<TeslaGate>>(
                new Action<TeslaGate>(HandleTeslaAdded)));

        TryHookStaticEvent("TeslaGate.OnRemoved", () =>
            TeslaGate.OnRemoved += DelegateSupport.ConvertDelegate<Il2CppSystem.Action<TeslaGate>>(
                new Action<TeslaGate>(HandleTeslaRemoved)));

        TryHookStaticEvent("TeslaGate.OnBursted", () =>
            TeslaGate.OnBursted += DelegateSupport.ConvertDelegate<Il2CppSystem.Action<TeslaGate>>(
                new Action<TeslaGate>(HandleTeslaBursted)));

        TryHookStaticEvent("ElevatorChamber.OnElevatorSpawned", () =>
            ElevatorChamber.OnElevatorSpawned += DelegateSupport.ConvertDelegate<Il2CppSystem.Action<ElevatorChamber>>(
                new Action<ElevatorChamber>(HandleElevatorSpawned)));

        TryHookStaticEvent("ElevatorChamber.OnElevatorRemoved", () =>
            ElevatorChamber.OnElevatorRemoved += DelegateSupport.ConvertDelegate<Il2CppSystem.Action<ElevatorChamber>>(
                new Action<ElevatorChamber>(HandleElevatorRemoved)));

        TryHookStaticEvent("ElevatorChamber.OnElevatorMoved", () =>
            ElevatorChamber.OnElevatorMoved += DelegateSupport.ConvertDelegate<ElevatorChamber.ElevatorMoved>(
                new Action<Bounds, ElevatorChamber, Vector3, Quaternion>(HandleElevatorMoved)));

        TryHookStaticEvent("Scp079Camera.OnInstanceCreated", () =>
            Scp079Camera.OnInstanceCreated += DelegateSupport.ConvertDelegate<Il2CppSystem.Action<Scp079Camera>>(
                new Action<Scp079Camera>(HandleCameraCreated)));

        TryHookStaticEvent("Scp079Camera.OnInstanceRemoved", () =>
            Scp079Camera.OnInstanceRemoved += DelegateSupport.ConvertDelegate<Il2CppSystem.Action<Scp079Camera>>(
                new Action<Scp079Camera>(HandleCameraRemoved)));

        TryHookStaticEvent("Scp079Camera.OnAnyCameraStateChanged", () =>
            Scp079Camera.OnAnyCameraStateChanged += DelegateSupport.ConvertDelegate<Il2CppSystem.Action<Scp079Camera>>(
                new Action<Scp079Camera>(HandleCameraStateChanged)));

        PlayerRegistry.Resync();
    }

    private static void TryHookStaticEvent(string description, Action subscribe)
    {
        try { subscribe(); }
        catch (Exception ex) { MelonLogger.Warning($"ClientRuntime: failed to hook {description}: {ex.Message}"); }
    }

    internal static void HandleClientConnected()
    {
        RunSafely(() =>
        {
            ClientApi.IsClientReady = false;
            EffectStateTracker.Clear();
            HealthStateTracker.Clear();
            PlayerRegistry.Resync();

            (string serverIp, ushort serverPort) = GetServerEndpoint();
            ClientEvents.OnConnected(new ClientConnectedEventArgs(serverIp, serverPort));
        }, nameof(HandleClientConnected));
    }

    internal static void HandleClientDisconnected()
    {
        RunSafely(() =>
        {
            (string serverIp, ushort serverPort) = GetServerEndpoint();

            ClientApi.IsClientReady = false;
            // Dispatch disconnect before clearing so subscribers see the final state.
            // Scheduler.ClearAll runs last so tasks scheduled during disconnect
            // handlers (short-lived server-session work) still get cancelled.
            ClientEvents.OnDisconnected(new ClientDisconnectedEventArgs(serverIp, serverPort));
            EffectStateTracker.Clear();
            HealthStateTracker.Clear();
            PlayerRegistry.Clear();
            Item.ClearAll();
            Projectile.ClearAllProjectiles();
            Pickup.ClearAll();
            Door.ClearAll();
            Generator.ClearAll();
            Tesla.ClearAll();
            Elevator.ClearAll();
            FacilityCamera.ClearAll();
            Ragdoll.ClearAll();
            global::Anomaly.Client.Api.Features.Locker.ClearAll();
            CharacterModels.Reset();
            CameraManager.Reset();
            ClientVisibility.ReleaseAll();
            LocalPlayerMotion.ResetSequence();
            Hud.ResetAll();
            ScreenEffects.Reset();
            VirtualInputs.Reset();
            WorldObjects.Reset();
            Scheduler.ClearAll();
        }, nameof(HandleClientDisconnected));
    }

    internal static void HandleClientStarted()
    {
        RunSafely(() =>
        {
            ClientApi.IsClientReady = false;
            EffectStateTracker.Clear();
            HealthStateTracker.Clear();
            PlayerRegistry.Resync();
        }, nameof(HandleClientStarted));
    }

    internal static void HandleClientReady()
    {
        RunSafely(() =>
        {
            ClientApi.IsClientReady = true;
            PlayerRegistry.Resync();
            HealthStateTracker.Update();
            ClientEvents.OnReady(new ClientReadyEventArgs(ClientApi.CurrentSceneName));
        }, nameof(HandleClientReady));
    }

    internal static void HandleLocalClientStarted()
    {
        RunSafely(PlayerRegistry.Resync, nameof(HandleLocalClientStarted));
    }

    internal static void HandleSceneLoaded(Scene scene, LoadSceneMode sceneMode)
    {
        RunSafely(() =>
        {
            EffectStateTracker.Clear();
            HealthStateTracker.Clear();
            PlayerRegistry.Resync();
            ClientEvents.OnSceneLoaded(new SceneLoadedEventArgs(scene, sceneMode));
        }, nameof(HandleSceneLoaded));
    }

    internal static void Update()
    {
        if (!_initialized)
            return;

        RunSafely(() =>
        {
            SchedulerTick.Update();
            InputPoller.Update();
            VirtualInputs.TickProviders();
            EffectStateTracker.Update();
            HealthStateTracker.Update();
        }, nameof(Update));
    }

    internal static void HandlePlayerAdded(ReferenceHub hub)
    {
        RunSafely(() =>
        {
            PlayerWrapper player = PlayerRegistry.Get(hub);
            if (player != null)
                PlayerEvents.OnAdded(new PlayerAddedEventArgs(player));

            HealthStateTracker.Reset(hub);
        }, nameof(HandlePlayerAdded));
    }

    internal static void HandlePlayerRemoved(ReferenceHub hub)
    {
        RunSafely(() =>
        {
            PlayerWrapper player = PlayerRegistry.Get(hub);
            if (player != null)
                PlayerEvents.OnRemoved(new PlayerRemovedEventArgs(player));

            HealthStateTracker.Reset(hub);
            PlayerRegistry.Remove(hub);
        }, nameof(HandlePlayerRemoved));
    }

    internal static void HandleRoleChanged(ReferenceHub hub, PlayerRoleBase previousRole, PlayerRoleBase newRole)
    {
        RunSafely(() =>
        {
            PlayerWrapper player = PlayerRegistry.Get(hub);
            if (player != null)
                RoleEvents.OnChanged(new PlayerRoleChangedEventArgs(player, previousRole, newRole));

            HealthStateTracker.Reset(hub);
        }, nameof(HandleRoleChanged));
    }

    internal static void HandleItemsModified(ReferenceHub hub)
    {
        RunSafely(() =>
        {
            PlayerWrapper player = PlayerRegistry.Get(hub);
            if (player != null)
                InventoryEvents.OnItemsChanged(new InventoryChangedEventArgs(player));
        }, nameof(HandleItemsModified));
    }

    internal static void HandleCurrentItemChanged(ReferenceHub hub, ItemIdentifier previousItem, ItemIdentifier currentItem)
    {
        RunSafely(() =>
        {
            PlayerWrapper player = PlayerRegistry.Get(hub);
            if (player != null)
                InventoryEvents.OnCurrentItemChanged(new CurrentItemChangedEventArgs(player, previousItem, currentItem));
        }, nameof(HandleCurrentItemChanged));
    }

    internal static void HandlePlayerDamaged(ReferenceHub hub, DamageHandlerBase damageHandler)
    {
        RunSafely(() =>
        {
            PlayerWrapper player = PlayerRegistry.Get(hub);
            if (player != null)
                StatsEvents.OnDamaged(new PlayerDamagedEventArgs(player, damageHandler));
        }, nameof(HandlePlayerDamaged));
    }

    internal static void HandlePlayerDied(ReferenceHub hub, DamageHandlerBase damageHandler)
    {
        RunSafely(() =>
        {
            PlayerWrapper player = PlayerRegistry.Get(hub);
            if (player != null)
                StatsEvents.OnDied(new PlayerDiedEventArgs(player, damageHandler));
        }, nameof(HandlePlayerDied));
    }

    internal static void HandlePickupAdded(ItemPickupBase pickupBase)
    {
        RunSafely(() =>
        {
            Pickup pickup = Pickup.Get(pickupBase);
            if (pickup != null)
                PickupEvents.OnAdded(new PickupAddedEventArgs(pickup));
        }, nameof(HandlePickupAdded));
    }

    internal static void HandlePickupRemoved(ItemPickupBase pickupBase)
    {
        RunSafely(() =>
        {
            Pickup pickup = Pickup.Get(pickupBase);
            if (pickup != null)
                PickupEvents.OnRemoved(new PickupRemovedEventArgs(pickup));

            if (pickup is Projectile projectile)
                ProjectileEvents.OnRemoved(new ProjectileRemovedEventArgs(projectile));

            Pickup.Unregister(pickupBase);
        }, nameof(HandlePickupRemoved));
    }

    internal static void HandleProjectileSpawned(ThrownProjectile projectileBase)
    {
        RunSafely(() =>
        {
            Projectile projectile = Projectile.Get(projectileBase);
            if (projectile != null)
                ProjectileEvents.OnSpawned(new ProjectileSpawnedEventArgs(projectile));
        }, nameof(HandleProjectileSpawned));
    }

    internal static void HandleFirearmShot(ShotEvent shotEvent)
    {
        RunSafely(() =>
        {
            FirearmEvents.OnShot(new FirearmShotEventArgs(shotEvent));
        }, nameof(HandleFirearmShot));
    }

    internal static void HandleAnnouncerLineStarted(CassieLineSource source)
    {
        RunSafely(() =>
        {
            if (source == null)
                return;
            AnnouncerEvents.OnLineStarted(new AnnouncerLineStartedEventArgs(source));
        }, nameof(HandleAnnouncerLineStarted));
    }

    internal static void HandleWarheadProgressChanged(bool inProgress)
    {
        RunSafely(() => WarheadEvents.OnProgressChanged(new WarheadProgressChangedEventArgs(inProgress)),
            nameof(HandleWarheadProgressChanged));
    }

    internal static void HandleWarheadDetonated()
    {
        RunSafely(WarheadEvents.OnDetonated, nameof(HandleWarheadDetonated));
    }

    internal static void HandleGeneratorEngaged(Scp079Generator generatorBase)
    {
        RunSafely(() =>
        {
            if (generatorBase == null) return;
            Generator wrapper = Generator.Get(generatorBase);
            if (wrapper != null)
                GeneratorEvents.OnEngaged(new GeneratorEngagedEventArgs(wrapper));
        }, nameof(HandleGeneratorEngaged));
    }

    internal static void HandleTeslaAdded(TeslaGate teslaBase)
    {
        RunSafely(() =>
        {
            if (teslaBase == null) return;
            Tesla wrapper = Tesla.Get(teslaBase);
            if (wrapper != null)
                TeslaEvents.OnAdded(new TeslaEventArgs(wrapper));
        }, nameof(HandleTeslaAdded));
    }

    internal static void HandleTeslaRemoved(TeslaGate teslaBase)
    {
        RunSafely(() =>
        {
            if (teslaBase == null) return;
            Tesla wrapper = Tesla.Get(teslaBase);
            if (wrapper != null)
                TeslaEvents.OnRemoved(new TeslaEventArgs(wrapper));
        }, nameof(HandleTeslaRemoved));
    }

    internal static void HandleTeslaBursted(TeslaGate teslaBase)
    {
        RunSafely(() =>
        {
            if (teslaBase == null) return;
            Tesla wrapper = Tesla.Get(teslaBase);
            if (wrapper != null)
                TeslaEvents.OnBursted(new TeslaEventArgs(wrapper));
        }, nameof(HandleTeslaBursted));
    }

    internal static void HandleElevatorSpawned(ElevatorChamber chamberBase)
    {
        RunSafely(() =>
        {
            if (chamberBase == null) return;
            Elevator wrapper = Elevator.Get(chamberBase);
            if (wrapper != null)
                ElevatorEvents.OnSpawned(new ElevatorEventArgs(wrapper));
        }, nameof(HandleElevatorSpawned));
    }

    internal static void HandleElevatorRemoved(ElevatorChamber chamberBase)
    {
        RunSafely(() =>
        {
            if (chamberBase == null) return;
            Elevator wrapper = Elevator.Get(chamberBase);
            if (wrapper != null)
                ElevatorEvents.OnRemoved(new ElevatorEventArgs(wrapper));
        }, nameof(HandleElevatorRemoved));
    }

    internal static void HandleElevatorMoved(Bounds bounds, ElevatorChamber chamberBase, Vector3 deltaPos, Quaternion deltaRot)
    {
        RunSafely(() =>
        {
            if (chamberBase == null) return;
            Elevator wrapper = Elevator.Get(chamberBase);
            if (wrapper != null)
                ElevatorEvents.OnMoved(new ElevatorEventArgs(wrapper));
        }, nameof(HandleElevatorMoved));
    }

    internal static void HandleCameraCreated(Scp079Camera cameraBase)
    {
        RunSafely(() =>
        {
            if (cameraBase == null) return;
            FacilityCamera wrapper = FacilityCamera.Get(cameraBase);
            if (wrapper != null)
                FacilityCameraEvents.OnCreated(new FacilityCameraEventArgs(wrapper));
        }, nameof(HandleCameraCreated));
    }

    internal static void HandleCameraRemoved(Scp079Camera cameraBase)
    {
        RunSafely(() =>
        {
            if (cameraBase == null) return;
            FacilityCamera wrapper = FacilityCamera.Get(cameraBase);
            if (wrapper != null)
                FacilityCameraEvents.OnRemoved(new FacilityCameraEventArgs(wrapper));
        }, nameof(HandleCameraRemoved));
    }

    internal static void HandleCameraStateChanged(Scp079Camera cameraBase)
    {
        RunSafely(() =>
        {
            if (cameraBase == null) return;
            FacilityCamera wrapper = FacilityCamera.Get(cameraBase);
            if (wrapper != null)
                FacilityCameraEvents.OnStateChanged(new FacilityCameraEventArgs(wrapper));
        }, nameof(HandleCameraStateChanged));
    }

    internal static void HandleAdminToyAdded(AdminToyBase adminToyBase)
    {
        RunSafely(() =>
        {
            AdminToy adminToy = AdminToy.Get(adminToyBase);
            if (adminToy != null)
                AdminToyEvents.OnAdded(new AdminToyAddedEventArgs(adminToy));
        }, nameof(HandleAdminToyAdded));
    }

    internal static void HandleAdminToyRemoved(AdminToyBase adminToyBase)
    {
        RunSafely(() =>
        {
            AdminToy adminToy = AdminToy.Get(adminToyBase);
            if (adminToy != null)
                AdminToyEvents.OnRemoved(new AdminToyRemovedEventArgs(adminToy));
        }, nameof(HandleAdminToyRemoved));
    }

    private static (string serverIp, ushort serverPort) GetServerEndpoint()
    {
        try
        {
            return (ClientApi.ServerIp, ClientApi.ServerPort);
        }
        catch
        {
            return (string.Empty, 0);
        }
    }

    private static void RunSafely(Action action, string context)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            MelonLogger.Error($"ClientRuntime.{context}: {ex}");
        }
    }
}
