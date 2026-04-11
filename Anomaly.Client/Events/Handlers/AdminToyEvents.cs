using Anomaly.Client.Events.Args;

namespace Anomaly.Client.Events.Handlers;

public static class AdminToyEvents
{
    public static event AnEventHandler<AdminToyAddedEventArgs> Added;
    public static event AnEventHandler<AdminToyRemovedEventArgs> Removed;

    internal static void OnAdded(AdminToyAddedEventArgs args) => EventInvoker.Invoke(Added, args);

    internal static void OnRemoved(AdminToyRemovedEventArgs args) => EventInvoker.Invoke(Removed, args);
}
