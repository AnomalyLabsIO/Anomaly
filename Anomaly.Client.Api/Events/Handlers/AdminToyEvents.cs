using Anomaly.Client.Api.Events.Args;

namespace Anomaly.Client.Api.Events.Handlers;

public static class AdminToyEvents
{
    public static event AnomalyEventHandler<AdminToyAddedEventArgs> Added;
    public static event AnomalyEventHandler<AdminToyRemovedEventArgs> Removed;

    internal static void OnAdded(AdminToyAddedEventArgs args) => EventInvoker.Invoke(Added, args);

    internal static void OnRemoved(AdminToyRemovedEventArgs args) => EventInvoker.Invoke(Removed, args);
}
