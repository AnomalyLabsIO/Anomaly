using Anomaly.Client.Events.Args;

namespace Anomaly.Client.Events.Handlers;

public static class StatsEvents
{
    public static event AnEventHandler<PlayerDamagedEventArgs> Damaged;
    public static event AnEventHandler<PlayerDiedEventArgs> Died;
    public static event AnEventHandler<PlayerHealedEventArgs> Healed;

    internal static void OnDamaged(PlayerDamagedEventArgs args) => EventInvoker.Invoke(Damaged, args);

    internal static void OnDied(PlayerDiedEventArgs args) => EventInvoker.Invoke(Died, args);

    internal static void OnHealed(PlayerHealedEventArgs args) => EventInvoker.Invoke(Healed, args);
}
