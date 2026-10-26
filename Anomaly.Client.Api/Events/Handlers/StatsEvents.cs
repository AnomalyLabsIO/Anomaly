using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Handlers;

public static class StatsEvents
{
    /// <summary>Observed value or maximum changes. First samples and visibility changes rebase silently.</summary>
    [ScriptEvent]
    public static event AnomalyEventHandler<PlayerStatChangedEventArgs> Changed;

    [ScriptEvent]
    public static event AnomalyEventHandler<PlayerStatChangedEventArgs> HealthChanged;
    [ScriptEvent]
    public static event AnomalyEventHandler<PlayerStatChangedEventArgs> ArtificialHealthChanged;
    [ScriptEvent]
    public static event AnomalyEventHandler<PlayerStatChangedEventArgs> MaximumChanged;

    [ScriptEvent]
    public static event AnomalyEventHandler<PlayerHealedEventArgs> Healed;

    /// <summary>
    /// Fires when a player's stamina value changes. Driven by the polling
    /// tracker in the loader, so deltas reflect the per-tick change rather
    /// than the underlying continuous stamina drain.
    /// </summary>
    [ScriptEvent]
    public static event AnomalyEventHandler<PlayerStaminaChangedEventArgs> StaminaChanged;

    /// <summary>
    /// Fires when a player's Hume shield value changes. Use the convenience
    /// <see cref="PlayerHumeShieldChangedEventArgs.Broke"/> flag to detect
    /// the shield-broken transition specifically.
    /// </summary>
    [ScriptEvent]
    public static event AnomalyEventHandler<PlayerHumeShieldChangedEventArgs> HumeShieldChanged;

    internal static void OnHealed(PlayerHealedEventArgs args) => EventInvoker.Invoke(Healed, args);

    internal static void OnChanged(PlayerStatChangedEventArgs args) => EventInvoker.Invoke(Changed, args);
    internal static void OnHealthChanged(PlayerStatChangedEventArgs args) => EventInvoker.Invoke(HealthChanged, args);
    internal static void OnArtificialHealthChanged(PlayerStatChangedEventArgs args) => EventInvoker.Invoke(ArtificialHealthChanged, args);
    internal static void OnMaximumChanged(PlayerStatChangedEventArgs args) => EventInvoker.Invoke(MaximumChanged, args);

    internal static void OnStaminaChanged(PlayerStaminaChangedEventArgs args) =>
        EventInvoker.Invoke(StaminaChanged, args);

    internal static void OnHumeShieldChanged(PlayerHumeShieldChangedEventArgs args) =>
        EventInvoker.Invoke(HumeShieldChanged, args);
}
