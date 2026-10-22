using Anomaly.Client.Api.Events.Args;

namespace Anomaly.Client.Api.Events.Handlers;

/// <summary>Observer events for received usable-item state. These do not expose a server use-completion decision.</summary>
public static class UsableItemEvents
{
    /// <summary>A start notification received for a serial; the item/owner can be unavailable locally.</summary>
    public static event AnomalyEventHandler<UsableItemStatusEventArgs> Started;

    /// <summary>An explicit cancel notification, not normal completion or removal.</summary>
    public static event AnomalyEventHandler<UsableItemStatusEventArgs> Cancelled;

    /// <summary>A received cooldown applied to a usable item in the local inventory.</summary>
    public static event AnomalyEventHandler<UsableItemCooldownEventArgs> CooldownChanged;

    internal static void OnStarted(UsableItemStatusEventArgs args) => EventInvoker.Invoke(Started, args);
    internal static void OnCancelled(UsableItemStatusEventArgs args) => EventInvoker.Invoke(Cancelled, args);
    internal static void OnCooldownChanged(UsableItemCooldownEventArgs args) => EventInvoker.Invoke(CooldownChanged, args);
}
