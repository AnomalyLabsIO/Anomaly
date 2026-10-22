using System;
using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Internal;
using Anomaly.Client.Api.Internal.Items;
using Il2CppInventorySystem.Items.Firearms.Modules;
using Il2CppInventorySystem.Items.Usables;
using Il2CppInterop.Runtime;
using MelonLoader;
using Item = Anomaly.Client.Api.Features.Item;
using UsableItem = Anomaly.Client.Api.Features.UsableItem;

namespace Anomaly.Client.Internal.Items;

/// <summary>Received item notifications and local equipped-firearm observations. Owns no gameplay state.</summary>
internal static class ItemStateRuntime
{
    private static readonly TransactionalInitializer Initializer = new();
    private static readonly FirearmObservationTracker<FirearmItem> Firearms = new();
    private static bool _observationWarningLogged;

    internal static void Initialize()
    {
        if (Initializer.IsInitialized)
            return;

        var status = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<StatusMessage>>(
            new Action<StatusMessage>(HandleStatus));
        var magazine = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<ushort>>(
            new Action<ushort>(HandleMagazine));

        // Rollback closures retain the exact delegate instances until shutdown.
        Initializer.Initialize(
            new InitializationStep(() => UsableItemsController.OnClientStatusReceived += status,
                () => UsableItemsController.OnClientStatusReceived -= status),
            new InitializationStep(() => MagazineModule.OnDataReceived += magazine,
                () => MagazineModule.OnDataReceived -= magazine));
    }

    internal static void Shutdown()
    {
        Initializer.Reset();
        ResetSession();
    }

    internal static void ResetSession()
    {
        Firearms.Clear();
        _observationWarningLogged = false;
    }

    internal static void Tick()
    {
        if (!Initializer.IsInitialized)
            return;

        RunSafely(() => Observe(LocalPlayer.Instance?.CurrentItem as FirearmItem));
    }

    internal static void HandleCooldown(ItemCooldownMessage message)
    {
        if (!Initializer.IsInitialized)
            return;

        RunSafely(() =>
        {
            if (ResolveItem(message.ItemSerial) is UsableItem item && item.HasLocalState)
                UsableItemEvents.OnCooldownChanged(new UsableItemCooldownEventArgs(item, message.RemainingTime));
        });
    }

    internal static void ForgetItem(Il2CppInventorySystem.Items.ItemBase itemBase)
    {
        if (!Initializer.IsInitialized)
            return;

        RunSafely(() =>
        {
            foreach (var firearm in Firearms.Keys)
            {
                if (firearm.Base != itemBase) continue;
                Firearms.Remove(firearm);
                break;
            }
        });
    }

    private static void HandleStatus(StatusMessage message)
    {
        if (!Initializer.IsInitialized)
            return;

        RunSafely(() =>
        {
            var args = new UsableItemStatusEventArgs(message.ItemSerial, ResolveItem(message.ItemSerial));
            if (message.Status == StatusMessage.StatusType.Start)
                UsableItemEvents.OnStarted(args);
            else if (message.Status == StatusMessage.StatusType.Cancel)
                UsableItemEvents.OnCancelled(args);
        });
    }

    private static void HandleMagazine(ushort serial)
    {
        if (Initializer.IsInitialized)
            RunSafely(() => Observe(ResolveItem(serial) as FirearmItem));
    }

    private static Item ResolveItem(ushort serial)
    {
        if (serial == 0)
            return null;

        Item cached = Item.Get(serial);
        if (cached != null && !cached.IsDestroyed)
            return cached;

        var local = LocalPlayer.Instance;
        if (local?.InventoryBase != null && local.InventoryBase.UserInventory.Items.TryGetValue(serial, out var itemBase))
            return Item.Get(itemBase);

        // An equipped remote item may not have been wrapped yet when its module sync arrives.
        foreach (var player in Player.PlayerList)
            if (player?.ReferenceHub != null && player.CurrentItemIdentifier.SerialNumber == serial)
                return player.CurrentItem;

        return null;
    }

    private static void Observe(FirearmItem firearm)
    {
        if (firearm == null || firearm.IsDestroyed)
            return;

        FirearmState current = firearm.GetState();
        if (!Firearms.TryObserve(firearm, current, out FirearmState? previousSnapshot, out long revision))
            return;

        FirearmState previous = previousSnapshot.GetValueOrDefault();
        var args = new FirearmStateChangedEventArgs(firearm, previousSnapshot, current);
        bool ammoChanged = previous.MagazineAmmo != current.MagazineAmmo ||
            previous.MagazineCapacity != current.MagazineCapacity || previous.MagazineInserted != current.MagazineInserted;
        bool localEquipped = firearm.IsLocalPlayerItem && firearm.IsEquipped;
        bool aimingChanged = localEquipped && previous.IsAiming != current.IsAiming;
        bool reloadChanged = localEquipped &&
            (previous.IsReloading != current.IsReloading || previous.IsUnloading != current.IsUnloading);

        FirearmEvents.OnStateChanged(args);
        // Subscribers can disconnect, destroy the item or reenter with a newer snapshot.
        // Never publish the remaining detail events after this observation was invalidated.
        if (!IsCurrentObservation(firearm, revision)) return;
        if (ammoChanged) FirearmEvents.OnAmmoChanged(args);
        if (!IsCurrentObservation(firearm, revision)) return;
        if (aimingChanged) FirearmEvents.OnAimingChanged(args);
        if (!IsCurrentObservation(firearm, revision)) return;
        if (reloadChanged) FirearmEvents.OnReloadStateChanged(args);
    }

    private static bool IsCurrentObservation(FirearmItem firearm, long revision) =>
        Initializer.IsInitialized && Firearms.IsCurrent(firearm, revision);

    private static void RunSafely(Action action)
    {
        try { action(); }
        catch (Exception ex)
        {
            if (_observationWarningLogged) return;
            _observationWarningLogged = true;
            MelonLogger.Warning($"Item state observation failed; gameplay is unchanged. Further failures are suppressed until reconnect: {ex}");
        }
    }
}
