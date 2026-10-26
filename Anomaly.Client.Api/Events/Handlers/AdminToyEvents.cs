using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Handlers;

public static class AdminToyEvents
{
    [ScriptEvent]
    public static event AnomalyEventHandler<AdminToyAddedEventArgs> Added;
    [ScriptEvent]
    public static event AnomalyEventHandler<AdminToyRemovedEventArgs> Removed;

    internal static void OnAdded(AdminToyAddedEventArgs args) => EventInvoker.Invoke(Added, args);

    internal static void OnRemoved(AdminToyRemovedEventArgs args) => EventInvoker.Invoke(Removed, args);
}
