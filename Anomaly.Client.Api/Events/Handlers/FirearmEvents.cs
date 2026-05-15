using Anomaly.Client.Api.Events.Args;

namespace Anomaly.Client.Api.Events.Handlers;

public static class FirearmEvents
{
    /// <summary>Fires on the rising edge of the local player's trigger press. Cancellable.</summary>
    public static event AnomalyEventHandler<PlayerShootingEventArgs> Shooting;

    /// <summary>Fires after a shot resolves. Observer-only.</summary>
    public static event AnomalyEventHandler<FirearmShotEventArgs> Shot;

    internal static void OnShooting(PlayerShootingEventArgs args) => EventInvoker.Invoke(Shooting, args);

    internal static void OnShot(FirearmShotEventArgs args) => EventInvoker.Invoke(Shot, args);
}
