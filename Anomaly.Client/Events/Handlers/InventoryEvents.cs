using Anomaly.Client.Events.Args;

namespace Anomaly.Client.Events.Handlers;

public static class InventoryEvents
{
    public static event AnEventHandler<InventoryChangedEventArgs> ItemsChanged;
    public static event AnEventHandler<CurrentItemChangedEventArgs> CurrentItemChanged;

    internal static void OnItemsChanged(InventoryChangedEventArgs args) => EventInvoker.Invoke(ItemsChanged, args);

    internal static void OnCurrentItemChanged(CurrentItemChangedEventArgs args) => EventInvoker.Invoke(CurrentItemChanged, args);
}
