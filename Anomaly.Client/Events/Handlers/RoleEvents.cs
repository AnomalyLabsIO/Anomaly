using Anomaly.Client.Events.Args;

namespace Anomaly.Client.Events.Handlers;

public static class RoleEvents
{
    public static event AnEventHandler<PlayerRoleChangedEventArgs> Changed;

    internal static void OnChanged(PlayerRoleChangedEventArgs args) => EventInvoker.Invoke(Changed, args);
}
