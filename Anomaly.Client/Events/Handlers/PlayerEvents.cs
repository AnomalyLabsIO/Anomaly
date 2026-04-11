using Anomaly.Client.Events.Args;

namespace Anomaly.Client.Events.Handlers;

public static class PlayerEvents
{
    public static event AnEventHandler<PlayerAddedEventArgs> Added;
    public static event AnEventHandler<PlayerRemovedEventArgs> Removed;

    internal static void OnAdded(PlayerAddedEventArgs args) => EventInvoker.Invoke(Added, args);

    internal static void OnRemoved(PlayerRemovedEventArgs args) => EventInvoker.Invoke(Removed, args);
}
