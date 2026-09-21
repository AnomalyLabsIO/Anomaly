using Anomaly.Client.Api.Events.Args;

namespace Anomaly.Client.Api.Events.Handlers;

public static class StatsEvents
{
    public static event AnomalyEventHandler<PlayerHealedEventArgs> Healed;

    /// <summary>
    /// Fires when a player's stamina value changes. Driven by the polling
    /// tracker in the loader, so deltas reflect the per-tick change rather
    /// than the underlying continuous stamina drain.
    /// </summary>
    public static event AnomalyEventHandler<PlayerStaminaChangedEventArgs> StaminaChanged;

    /// <summary>
    /// Fires when a player's Hume shield value changes. Use the convenience
    /// <see cref="PlayerHumeShieldChangedEventArgs.Broke"/> flag to detect
    /// the shield-broken transition specifically.
    /// </summary>
    public static event AnomalyEventHandler<PlayerHumeShieldChangedEventArgs> HumeShieldChanged;

    internal static void OnHealed(PlayerHealedEventArgs args) => EventInvoker.Invoke(Healed, args);

    internal static void OnStaminaChanged(PlayerStaminaChangedEventArgs args) =>
        EventInvoker.Invoke(StaminaChanged, args);

    internal static void OnHumeShieldChanged(PlayerHumeShieldChangedEventArgs args) =>
        EventInvoker.Invoke(HumeShieldChanged, args);
}
