using Anomaly.Client.Api.Events.Args;

namespace Anomaly.Client.Api.Events.Handlers;

public static class StatsEvents
{
    public static event AnomalyEventHandler<PlayerDamagedEventArgs> Damaged;
    public static event AnomalyEventHandler<PlayerDiedEventArgs> Died;
    public static event AnomalyEventHandler<PlayerHealedEventArgs> Healed;

    internal static void OnDamaged(PlayerDamagedEventArgs args) => EventInvoker.Invoke(Damaged, args);

    internal static void OnDied(PlayerDiedEventArgs args) => EventInvoker.Invoke(Died, args);

    internal static void OnHealed(PlayerHealedEventArgs args) => EventInvoker.Invoke(Healed, args);
}
