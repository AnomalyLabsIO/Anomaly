using Anomaly.Client.Api.Events.Args;

namespace Anomaly.Client.Api.Events.Handlers;

public static class PlayerEvents
{
    public static event AnomalyEventHandler<PlayerAddedEventArgs> Added;
    public static event AnomalyEventHandler<PlayerRemovedEventArgs> Removed;

    /// <summary>Fires when the local player presses the interact key, before the raycast/RPC run. Cancellable.</summary>
    public static event AnomalyEventHandler<PlayerInteractingEventArgs> Interacting;

    /// <summary>Fires when the local player transitions into the sprinting movement state. Cancellable.</summary>
    public static event AnomalyEventHandler<PlayerSprintingEventArgs> Sprinting;

    /// <summary>Fires when the local player's jump is about to be applied. Cancellable.</summary>
    public static event AnomalyEventHandler<PlayerJumpingEventArgs> Jumping;

    /// <summary>Fires when the local player presses the noclip toggle key, before the toggle message is sent. Cancellable.</summary>
    public static event AnomalyEventHandler<PlayerNoclipTogglingEventArgs> NoclipToggling;

    internal static void OnAdded(PlayerAddedEventArgs args) => EventInvoker.Invoke(Added, args);
    internal static void OnRemoved(PlayerRemovedEventArgs args) => EventInvoker.Invoke(Removed, args);
    internal static void OnInteracting(PlayerInteractingEventArgs args) => EventInvoker.Invoke(Interacting, args);
    internal static void OnSprinting(PlayerSprintingEventArgs args) => EventInvoker.Invoke(Sprinting, args);
    internal static void OnJumping(PlayerJumpingEventArgs args) => EventInvoker.Invoke(Jumping, args);
    internal static void OnNoclipToggling(PlayerNoclipTogglingEventArgs args) => EventInvoker.Invoke(NoclipToggling, args);
}
