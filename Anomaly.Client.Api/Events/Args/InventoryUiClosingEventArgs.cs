namespace Anomaly.Client.Api.Events.Args;

/// <summary>
/// Fires when the local inventory panel is about to be hidden
/// (<c>InventoryGuiController.IsEnabled</c> transitioning true → false).
/// Cancelling keeps the panel visible; no network state is touched.
/// </summary>
public class InventoryUiClosingEventArgs : EventArgs, ICancellableEvent
{
    public InventoryUiClosingEventArgs()
    {
    }

    public bool IsAllowed { get; set; } = true;
}
