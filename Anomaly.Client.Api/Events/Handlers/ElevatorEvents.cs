using Anomaly.Client.Api.Events.Args;

namespace Anomaly.Client.Api.Events.Handlers;

/// <summary>
/// Elevator chamber lifecycle and movement events. Hooked from
/// <see cref="Il2CppInteractables.Interobjects.ElevatorChamber"/>'s static
/// events.
/// </summary>
public static class ElevatorEvents
{
    /// <summary>Fires when a new elevator chamber spawns into the scene.</summary>
    public static event AnomalyEventHandler<ElevatorEventArgs> Spawned;

    /// <summary>Fires when an elevator chamber is removed from the scene.</summary>
    public static event AnomalyEventHandler<ElevatorEventArgs> Removed;

    /// <summary>
    /// Fires when an elevator chamber starts a movement sequence (door close →
    /// travel → door open at destination).
    /// </summary>
    public static event AnomalyEventHandler<ElevatorEventArgs> Moved;

    internal static void OnSpawned(ElevatorEventArgs args) => EventInvoker.Invoke(Spawned, args);
    internal static void OnRemoved(ElevatorEventArgs args) => EventInvoker.Invoke(Removed, args);
    internal static void OnMoved(ElevatorEventArgs args) => EventInvoker.Invoke(Moved, args);
}
