using System;
using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Internal;
using Anomaly.Client.Api.Input;
using Anomaly.Client.Api.Modding;
using Anomaly.Client.Api.Networking;
using Anomaly.Client.Api.Persistence;
using Anomaly.Client.Api.Scheduling;
using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Client.Internal.Effects;
using Anomaly.Client.Internal.Round;
using Anomaly.Client.Api.Internal.Input;
using Anomaly.Client.Api.Internal.Scheduling;
using Anomaly.Client.Internal.Stats;
using Anomaly.Client.Internal.Players;
using Anomaly.Client.Internal.Items;
using Anomaly.Client.Internal.Map;
using Anomaly.Client.Api.Internal.Wrappers;
using Anomaly.Client.Patches.Menu.ModSettings;
using Il2CppAdminToys;
using Il2CppCassie;
using Il2CppCustomPlayerEffects;
using Il2Cpp;
using Il2CppInteractables.Interobjects;
using Il2CppInteractables;
using Il2CppInventorySystem;
using Il2CppInventorySystem.Items;
using Il2CppInventorySystem.Items.Firearms.ShotEvents;
using Il2CppInventorySystem.Items.Pickups;
using Il2CppInventorySystem.Items.ThrowableProjectiles;
using Il2CppMapGeneration.Distributors;
using Il2CppMapGeneration;
using Il2CppMirror;
using Il2CppPlayerRoles;
using Il2CppPlayerRoles.PlayableScps.Scp079.Cameras;
using Il2CppInterop.Runtime;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;
using ClientApi = global::Anomaly.Client.Api.Features.Client;
using PlayerWrapper = global::Anomaly.Client.Api.Features.Player;

namespace Anomaly.Client.Internal.Bootstrap;

internal static class ClientRuntime
{
    private static readonly TransactionalInitializer Initializer = new();
    private static readonly ClientLifecycleGate LifecycleGate = new();

    internal static bool IsInitialized => Initializer.IsInitialized;

    internal static void Initialize()
    {
        if (Initializer.IsInitialized)
            return;

        var shot = DelegateSupport.ConvertDelegate<ShotEventManager.Shot>(
            new Action<ShotEvent>(HandleFirearmShot));
        var cassie = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<CassieLineSource>>(
            new Action<CassieLineSource>(HandleAnnouncerLineStarted));
        var warheadProgress = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<bool>>(
            new Action<bool>(HandleWarheadProgressChanged));
        var warheadDetonated = DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(
            new Action(HandleWarheadDetonated));
        var teslaAdded = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<TeslaGate>>(
            new Action<TeslaGate>(HandleTeslaAdded));
        var teslaRemoved = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<TeslaGate>>(
            new Action<TeslaGate>(HandleTeslaRemoved));
        var teslaBursted = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<TeslaGate>>(
            new Action<TeslaGate>(HandleTeslaBursted));
        var elevatorSpawned = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<ElevatorChamber>>(
            new Action<ElevatorChamber>(HandleElevatorSpawned));
        var elevatorRemoved = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<ElevatorChamber>>(
            new Action<ElevatorChamber>(HandleElevatorRemoved));
        var elevatorMoved = DelegateSupport.ConvertDelegate<ElevatorChamber.ElevatorMoved>(
            new Action<Bounds, ElevatorChamber, Vector3, Quaternion>(HandleElevatorMoved));
        var cameraCreated = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<Scp079Camera>>(
            new Action<Scp079Camera>(HandleCameraCreated));
        var cameraRemoved = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<Scp079Camera>>(
            new Action<Scp079Camera>(HandleCameraRemoved));
        var cameraStateChanged = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<Scp079Camera>>(
            new Action<Scp079Camera>(HandleCameraStateChanged));
        var roomRemoved = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<RoomIdentifier>>(
            new Action<RoomIdentifier>(HandleRoomRemoved));
        var interacted = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<InteractableCollider>>(
            new Action<InteractableCollider>(HandleInteracted));

        Initializer.Initialize(
            RequiredHook("ShotEventManager.OnShot",
                () => ShotEventManager.OnShot += shot,
                () => ShotEventManager.OnShot -= shot),
            RequiredHook("CassieLineSource.OnStartedPlaying",
                () => CassieLineSource.OnStartedPlaying += cassie,
                () => CassieLineSource.OnStartedPlaying -= cassie),
            RequiredHook("AlphaWarheadController.OnProgressChanged",
                () => AlphaWarheadController.OnProgressChanged += warheadProgress,
                () => AlphaWarheadController.OnProgressChanged -= warheadProgress),
            RequiredHook("AlphaWarheadController.OnDetonated",
                () => AlphaWarheadController.OnDetonated += warheadDetonated,
                () => AlphaWarheadController.OnDetonated -= warheadDetonated),
            RequiredHook("TeslaGate.OnAdded",
                () => TeslaGate.OnAdded += teslaAdded,
                () => TeslaGate.OnAdded -= teslaAdded),
            RequiredHook("TeslaGate.OnRemoved",
                () => TeslaGate.OnRemoved += teslaRemoved,
                () => TeslaGate.OnRemoved -= teslaRemoved),
            RequiredHook("TeslaGate.OnBursted",
                () => TeslaGate.OnBursted += teslaBursted,
                () => TeslaGate.OnBursted -= teslaBursted),
            RequiredHook("ElevatorChamber.OnElevatorSpawned",
                () => ElevatorChamber.OnElevatorSpawned += elevatorSpawned,
                () => ElevatorChamber.OnElevatorSpawned -= elevatorSpawned),
            RequiredHook("ElevatorChamber.OnElevatorRemoved",
                () => ElevatorChamber.OnElevatorRemoved += elevatorRemoved,
                () => ElevatorChamber.OnElevatorRemoved -= elevatorRemoved),
            RequiredHook("ElevatorChamber.OnElevatorMoved",
                () => ElevatorChamber.OnElevatorMoved += elevatorMoved,
                () => ElevatorChamber.OnElevatorMoved -= elevatorMoved),
            RequiredHook("Scp079Camera.OnInstanceCreated",
                () => Scp079Camera.OnInstanceCreated += cameraCreated,
                () => Scp079Camera.OnInstanceCreated -= cameraCreated),
            RequiredHook("Scp079Camera.OnInstanceRemoved",
                () => Scp079Camera.OnInstanceRemoved += cameraRemoved,
                () => Scp079Camera.OnInstanceRemoved -= cameraRemoved),
            RequiredHook("Scp079Camera.OnAnyCameraStateChanged",
                () => Scp079Camera.OnAnyCameraStateChanged += cameraStateChanged,
                () => Scp079Camera.OnAnyCameraStateChanged -= cameraStateChanged),
            RequiredHook("RoomIdentifier.OnRemoved",
                () => RoomIdentifier.OnRemoved += roomRemoved,
                () => RoomIdentifier.OnRemoved -= roomRemoved),
            RequiredHook("InteractionCoordinator.OnClientInteracted",
                () => InteractionCoordinator.OnClientInteracted += interacted,
                () => InteractionCoordinator.OnClientInteracted -= interacted),
            new InitializationStep(PlayerRegistry.Resync, PlayerRegistry.Clear),
            new InitializationStep(PlayerObservationBridge.Initialize, PlayerObservationBridge.Shutdown),
            new InitializationStep(ItemStateRuntime.Initialize, ItemStateRuntime.Shutdown),
            new InitializationStep(FacilityEventsRuntime.Init, FacilityEventsRuntime.Shutdown));
    }

    internal static void Shutdown()
    {
        ClientApi.IsClientReady = false;
        Initializer.Reset();
        LifecycleGate.Reset();
        ClearRuntimeCaches();
    }

    private static InitializationStep RequiredHook(
        string description,
        Action subscribe,
        Action unsubscribe)
    {
        return new InitializationStep(
            () =>
            {
                try { subscribe(); }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Required client runtime hook '{description}' could not be installed.", ex);
                }
            },
            unsubscribe);
    }

    internal static void HandleClientConnected(CustomNetworkManager manager)
    {
        if (!LifecycleGate.TryConnect(NetworkManagerId(manager)))
            return;

        RunRequiredLifecycleSafely(() =>
        {
            ClientApi.IsClientReady = false;
            EffectStateTracker.Clear();
            HealthStateTracker.Clear();
            PlayerRegistry.Resync();

            (string serverIp, ushort serverPort) = GetServerEndpoint();
            Anomaly.Client.Internal.Map.FacilityRecipeClient.BeginConnection();
            ClientEvents.OnConnected(new ClientConnectedEventArgs(serverIp, serverPort));
        }, nameof(HandleClientConnected));
    }

    internal static void HandleClientDisconnected(CustomNetworkManager manager)
    {
        if (!LifecycleGate.TryDisconnect(NetworkManagerId(manager)))
            return;

        RunRequiredLifecycleSafely(() =>
        {
            (string serverIp, ushort serverPort) = GetServerEndpoint();

            ClientApi.IsClientReady = false;
            // Dispatch disconnect before clearing so subscribers see the final state.
            ClientEvents.OnDisconnected(new ClientDisconnectedEventArgs(serverIp, serverPort));

            // Handlers may persist final session state. Flush it before the
            // scheduler boundary cancels debounce tasks.
            RuntimeTeardown.FlushBeforeCancel(
                FlushPendingWrites,
                () =>
                {
                    RoundStateReceiver.Reset();
                    ClearRuntimeCaches();
                    Scheduler.ClearAll();
                });
        }, nameof(HandleClientDisconnected), disconnectOnFailure: false);
    }

    internal static void HandleClientStarted(CustomNetworkManager manager)
    {
        if (!LifecycleGate.TryStart(NetworkManagerId(manager)))
            return;

        RunRequiredLifecycleSafely(() =>
        {
            ClientApi.IsClientReady = false;
            EffectStateTracker.Clear();
            HealthStateTracker.Clear();
            PlayerRegistry.Resync();
        }, nameof(HandleClientStarted));
    }

    internal static void HandleClientReady(CustomNetworkManager manager)
    {
        if (!LifecycleGate.TryReady(NetworkManagerId(manager)))
            return;

        RunRequiredLifecycleSafely(() =>
        {
            ClientApi.IsClientReady = true;
            PlayerRegistry.Resync();
            FacilityEventsRuntime.Resync();
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
        if (sceneMode == LoadSceneMode.Single)
            LifecycleGate.BeginSceneTransition();
        RunRequiredLifecycleSafely(() =>
        {
            if (sceneMode == LoadSceneMode.Single)
            {
                ClientApi.IsClientReady = false;
                ClearRuntimeCaches();
            }

            PlayerRegistry.Resync();
            FacilityEventsRuntime.Resync();
            ClientEvents.OnSceneLoaded(new SceneLoadedEventArgs(scene, sceneMode));

            if (sceneMode == LoadSceneMode.Single)
            {
                RuntimeTeardown.FlushBeforeCancel(FlushPendingWrites, Scheduler.ClearAll);
            }
        }, nameof(HandleSceneLoaded));
    }

    internal static void Update()
    {
        if (!Initializer.IsInitialized)
            return;

        // Let a required per-frame service failure reach Core.UpdateFeature so
        // feature health flips to Failed and the authenticated session is torn
        // down instead of continuing in a partially-wired state.
        SchedulerTick.Update();
        Anomaly.Client.Internal.Map.FacilityRecipeClient.Tick();
        InputPoller.Update();
        VirtualInputs.TickProviders();
        EffectStateTracker.Update();
        HealthStateTracker.Update();
        FacilityEventsRuntime.Tick();
        ItemStateRuntime.Tick();
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
            ClearPlayerCaches(hub);
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

            Role.Unregister(previousRole);
            PlayerAnchors.ClearAll();
            HealthStateTracker.Reset(hub);
        }, nameof(HandleRoleChanged));
    }

    internal static void HandleItemsModified(ReferenceHub hub)
    {
        RunSafely(() =>
        {
            Item.ReconcileInventory(hub?.inventory);
            PlayerWrapper player = PlayerRegistry.Get(hub);
            if (player != null)
                InventoryEvents.OnItemsChanged(new InventoryChangedEventArgs(player));
        }, nameof(HandleItemsModified));
    }

    internal static void HandleCurrentItemChanged(ReferenceHub hub, ItemIdentifier previousItem, ItemIdentifier currentItem)
    {
        RunSafely(() =>
        {
            if (hub != null && !hub.isLocalPlayer)
                Item.Unregister(previousItem.SerialNumber);

            PlayerWrapper player = PlayerRegistry.Get(hub);
            if (player != null)
                InventoryEvents.OnCurrentItemChanged(new CurrentItemChangedEventArgs(player, previousItem, currentItem));
        }, nameof(HandleCurrentItemChanged));
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
            Tesla.Unregister(teslaBase);
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
            Elevator.Unregister(chamberBase);
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
            FacilityCamera.Unregister(cameraBase);
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
            AdminToy.Unregister(adminToyBase);
        }, nameof(HandleAdminToyRemoved));
    }

    private static void HandleRoomRemoved(RoomIdentifier roomIdentifier)
    {
        RunSafely(() => Room.Unregister(roomIdentifier), nameof(HandleRoomRemoved));
    }

    private static void HandleInteracted(InteractableCollider collider)
    {
        RunSafely(() =>
        {
            if (LocalPlayer.Instance is { } player && collider != null)
                PlayerEvents.OnInteracted(new PlayerInteractedEventArgs(player, collider));
        }, nameof(HandleInteracted));
    }

    private static void FlushPendingWrites()
    {
        ModSettingsSaveScheduler.Flush();
        ClientPersistence.FlushAll();
    }

    private static void ClearRuntimeCaches()
    {
        PlayerObservationBridge.Reset();
        ItemStateRuntime.ResetSession();
        FacilityEventsRuntime.Clear();
        EffectStateTracker.Clear();
        HealthStateTracker.Clear();
        PlayerRegistry.Clear();
        Item.ClearAll();
        Projectile.ClearAllProjectiles();
        Pickup.ClearAll();
        AdminToy.ClearAll();
        Door.ClearAll();
        Generator.ClearAll();
        Tesla.ClearAll();
        Elevator.ClearAll();
        FacilityCamera.ClearAll();
        Ragdoll.ClearAll();
        global::Anomaly.Client.Api.Features.Locker.ClearAll();
        Role.ClearAll();
        Room.ClearAll();
        PlayerInventory.ClearAll();
        PlayerEffects.ClearAll();
        PlayerEffect.ClearAll();
        PlayerStatsWrapper.ClearAll();
        PlayerStat.ClearAll();
        PlayerAnchors.ClearAll();
        CharacterModels.Reset();
        CameraManager.Reset();
        ClientVisibility.ReleaseAll();
        LocalPlayerMotion.ResetSequence();
        Hud.ResetAll();
        ScreenEffects.Reset();
        VirtualInputs.Reset();
        InputPoller.Reset();
        WorldObjects.Reset();
    }

    private static void ClearPlayerCaches(ReferenceHub hub)
    {
        if (hub == null)
            return;

        try { Role.Unregister(hub.roleManager?.CurrentRole); } catch { }
        try { PlayerInventory.Unregister(hub.inventory); } catch { }
        try { PlayerEffects.Unregister(hub.playerEffectsController); } catch { }
        try { PlayerStatsWrapper.Unregister(hub.playerStats); } catch { }
        try
        {
            if (hub.inventory?.UserInventory?.Items != null)
            {
                foreach (var pair in hub.inventory.UserInventory.Items)
                    Item.Unregister(pair.Value);
            }
        }
        catch { }

        // Parent unregister operations evict their owned child effect/stat
        // wrappers without invalidating unrelated players' caches.
        try
        {
            PlayerWrapper player = PlayerRegistry.Get(hub);
            PlayerAnchors.Unregister(player?.CharacterModel);
        }
        catch { }
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

    private static long NetworkManagerId(CustomNetworkManager manager)
    {
        try { return manager?.Pointer.ToInt64() ?? 0; }
        catch { return 0; }
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

    private static void RunRequiredLifecycleSafely(
        Action action,
        string context,
        bool disconnectOnFailure = true)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Initializer.Reset();
            LifecycleGate.Reset();
            ClientApi.IsClientReady = false;
            if (disconnectOnFailure)
                AnomalyMessaging.FailClosedTransport($"Required lifecycle callback {context} failed.");
            try
            {
                FeatureHealth.Report(
                    "anomaly",
                    "runtime-hooks",
                    FeatureHealthStatus.Failed,
                    $"Required lifecycle callback {context} failed: {ex.GetType().Name}: {ex.Message}");
                MelonLogger.Error($"ClientRuntime.{context}: required lifecycle failure: {ex}");
            }
            catch { }
        }
    }
}
