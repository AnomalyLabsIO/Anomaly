using Anomaly.Client.Api.Events.Args;

namespace Anomaly.Client.Api.Events.Handlers;

public static class InventoryEvents
{
    public static event AnomalyEventHandler<InventoryChangedEventArgs> ItemsChanged;
    public static event AnomalyEventHandler<CurrentItemChangedEventArgs> CurrentItemChanged;

    internal static void OnItemsChanged(InventoryChangedEventArgs args) => EventInvoker.Invoke(ItemsChanged, args);

    internal static void OnCurrentItemChanged(CurrentItemChangedEventArgs args) => EventInvoker.Invoke(CurrentItemChanged, args);
}
