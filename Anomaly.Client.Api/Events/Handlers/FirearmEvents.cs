using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Handlers;

public static class FirearmEvents
{
    /// <summary>Fires on the rising edge of the local player's trigger press. Cancellable.</summary>
    [ScriptEvent]
    public static event AnomalyEventHandler<PlayerShootingEventArgs> Shooting;

    /// <summary>Fires after a shot resolves. Observer-only.</summary>
    [ScriptEvent]
    public static event AnomalyEventHandler<FirearmShotEventArgs> Shot;

    /// <summary>
    /// Observer snapshot after magazine sync or a local equipped-firearm update.
    /// Includes first observation; intermediate changes between local updates can be coalesced.
    /// This is not proof that the server accepted a reload or input action.
    /// </summary>
    [ScriptEvent]
    public static event AnomalyEventHandler<FirearmStateChangedEventArgs> StateChanged;

    /// <summary>Magazine ammo, capacity or insertion state changed in an observed snapshot.</summary>
    [ScriptEvent]
    public static event AnomalyEventHandler<FirearmStateChangedEventArgs> AmmoChanged;

    /// <summary>Local equipped ADS state changed in an observed snapshot. Remote ADS-only updates are not tracked.</summary>
    [ScriptEvent]
    public static event AnomalyEventHandler<FirearmStateChangedEventArgs> AimingChanged;

    /// <summary>Local equipped reload/unload prediction changed in an observed snapshot.</summary>
    [ScriptEvent]
    public static event AnomalyEventHandler<FirearmStateChangedEventArgs> ReloadStateChanged;

    internal static void OnShooting(PlayerShootingEventArgs args) => EventInvoker.Invoke(Shooting, args);

    internal static void OnShot(FirearmShotEventArgs args) => EventInvoker.Invoke(Shot, args);

    internal static void OnStateChanged(FirearmStateChangedEventArgs args) => EventInvoker.Invoke(StateChanged, args);
    internal static void OnAmmoChanged(FirearmStateChangedEventArgs args) => EventInvoker.Invoke(AmmoChanged, args);
    internal static void OnAimingChanged(FirearmStateChangedEventArgs args) => EventInvoker.Invoke(AimingChanged, args);
    internal static void OnReloadStateChanged(FirearmStateChangedEventArgs args) => EventInvoker.Invoke(ReloadStateChanged, args);
}
