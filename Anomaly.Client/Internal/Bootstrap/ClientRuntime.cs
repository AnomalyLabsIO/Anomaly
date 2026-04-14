using System;
using Anomaly.Client.API.Features;
using Anomaly.Client.Events.Args;
using Anomaly.Client.Events.Handlers;
using Anomaly.Client.Internal.Effects;
using Anomaly.Client.Internal.Stats;
using Anomaly.Client.Internal.Wrappers;
using Il2CppAdminToys;
using Il2CppCustomPlayerEffects;
using Il2Cpp;
using Il2CppInventorySystem;
using Il2CppInventorySystem.Items;
using Il2CppInventorySystem.Items.Firearms.ShotEvents;
using Il2CppInventorySystem.Items.Pickups;
using Il2CppInventorySystem.Items.ThrowableProjectiles;
using Il2CppPlayerRoles;
using Il2CppPlayerStatsSystem;
using Il2CppInterop.Runtime;
using MelonLoader;
using UnityEngine.SceneManagement;
using ClientApi = global::Anomaly.Client.API.Features.Client;
using PlayerWrapper = global::Anomaly.Client.API.Features.Player;

namespace Anomaly.Client.Internal.Bootstrap;

internal static class ClientRuntime
{
    private static bool _initialized;

    internal static bool IsInitialized => _initialized;
    internal static bool IsClientReady { get; private set; }

    internal static void Initialize()
    {
        if (_initialized)
            return;

        _initialized = true;

        ShotEventManager.OnShot += DelegateSupport.ConvertDelegate<ShotEventManager.Shot>(
            new Action<ShotEvent>(HandleFirearmShot));

        PlayerRegistry.Resync();
    }

    internal static void HandleClientConnected()
    {
        RunSafely(() =>
        {
            IsClientReady = false;
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

            IsClientReady = false;
            ClientEvents.OnDisconnected(new ClientDisconnectedEventArgs(serverIp, serverPort));
            EffectStateTracker.Clear();
            HealthStateTracker.Clear();
            PlayerRegistry.Clear();
        }, nameof(HandleClientDisconnected));
    }

    internal static void HandleClientStarted()
    {
        RunSafely(() =>
        {
            IsClientReady = false;
            EffectStateTracker.Clear();
            HealthStateTracker.Clear();
            PlayerRegistry.Resync();
        }, nameof(HandleClientStarted));
    }

    internal static void HandleClientReady()
    {
        RunSafely(() =>
        {
            IsClientReady = true;
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
