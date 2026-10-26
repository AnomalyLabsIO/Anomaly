using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

/// <summary>
/// Fires when the local inventory panel is about to become visible
/// (<c>InventoryGuiController.IsEnabled</c> transitioning false → true).
/// Cancelling keeps the panel hidden; no network state is touched.
/// </summary>
[ScriptType]
public class InventoryUiOpeningEventArgs : EventArgs, ICancellableEvent
{
    public InventoryUiOpeningEventArgs()
    {
    }

    [ScriptVisible]
    public bool IsAllowed { get; set; } = true;
}
