using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Handlers;

public static class InventoryEvents
{
    [ScriptEvent]
    public static event AnomalyEventHandler<InventoryChangedEventArgs> ItemsChanged;
    [ScriptEvent]
    public static event AnomalyEventHandler<CurrentItemChangedEventArgs> CurrentItemChanged;

    /// <summary>Fires when the local player initiates a drop via <c>ClientDropItem</c>, before <c>CmdDropItem</c>. Cancellable.</summary>
    [ScriptEvent]
    public static event AnomalyEventHandler<PlayerDroppingItemEventArgs> DroppingItem;

    /// <summary>Fires when the local player initiates a held-item switch via <c>ClientSelectItem</c>, before <c>CmdSelectItem</c>. Server-forced switches do not pass through this path. Cancellable.</summary>
    [ScriptEvent]
    public static event AnomalyEventHandler<CurrentItemChangingEventArgs> CurrentItemChanging;

    /// <summary>Fires when the local inventory UI panel is about to become visible. Cancellable.</summary>
    [ScriptEvent]
    public static event AnomalyEventHandler<InventoryUiOpeningEventArgs> UiOpening;

    /// <summary>Fires when the local inventory UI panel is about to be hidden. Cancellable.</summary>
    [ScriptEvent]
    public static event AnomalyEventHandler<InventoryUiClosingEventArgs> UiClosing;

    internal static void OnItemsChanged(InventoryChangedEventArgs args) => EventInvoker.Invoke(ItemsChanged, args);
    internal static void OnCurrentItemChanged(CurrentItemChangedEventArgs args) => EventInvoker.Invoke(CurrentItemChanged, args);
    internal static void OnDroppingItem(PlayerDroppingItemEventArgs args) => EventInvoker.Invoke(DroppingItem, args);
    internal static void OnCurrentItemChanging(CurrentItemChangingEventArgs args) => EventInvoker.Invoke(CurrentItemChanging, args);
    internal static void OnUiOpening(InventoryUiOpeningEventArgs args) => EventInvoker.Invoke(UiOpening, args);
    internal static void OnUiClosing(InventoryUiClosingEventArgs args) => EventInvoker.Invoke(UiClosing, args);
}
