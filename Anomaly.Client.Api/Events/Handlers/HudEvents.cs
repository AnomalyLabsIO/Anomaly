using Anomaly.Client.Api.Events.Args;

namespace Anomaly.Client.Api.Events.Handlers;

/// <summary>
/// Local HUD render events. All are cancellable and affect only this client's
/// display; the server's view of state is never touched by cancelling here.
/// </summary>
public static class HudEvents
{
    /// <summary>Fires before a server-sent hint is rendered on the local HUD. Mutate text/duration or cancel to suppress.</summary>
    public static event AnomalyEventHandler<HintDisplayingEventArgs> HintDisplaying;

    /// <summary>Fires before a server-sent broadcast is rendered on the local HUD. Cancel to suppress.</summary>
    public static event AnomalyEventHandler<BroadcastShowingEventArgs> BroadcastShowing;

    internal static void OnHintDisplaying(HintDisplayingEventArgs args) => EventInvoker.Invoke(HintDisplaying, args);

    internal static void OnBroadcastShowing(BroadcastShowingEventArgs args) => EventInvoker.Invoke(BroadcastShowing, args);
}
