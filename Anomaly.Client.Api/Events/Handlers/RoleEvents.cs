using Anomaly.Client.Api.Events.Args;

namespace Anomaly.Client.Api.Events.Handlers;

public static class RoleEvents
{
    public static event AnomalyEventHandler<PlayerRoleChangedEventArgs> Changed;

    internal static void OnChanged(PlayerRoleChangedEventArgs args) => EventInvoker.Invoke(Changed, args);
}
