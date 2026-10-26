using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Handlers;

public static class RoleEvents
{
    [ScriptEvent]
    public static event AnomalyEventHandler<PlayerRoleChangedEventArgs> Changed;

    internal static void OnChanged(PlayerRoleChangedEventArgs args) => EventInvoker.Invoke(Changed, args);
}
