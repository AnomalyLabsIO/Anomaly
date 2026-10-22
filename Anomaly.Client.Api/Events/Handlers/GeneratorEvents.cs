using Anomaly.Client.Api.Events.Args;

namespace Anomaly.Client.Api.Events.Handlers;

/// <summary>
/// Generator state observed through client replication, after the initial spawn snapshot.
/// </summary>
public static class GeneratorEvents
{
    /// <summary>Fires once per generator the moment it transitions to <c>Engaged</c>.</summary>
    public static event AnomalyEventHandler<GeneratorEngagedEventArgs> Engaged;

    public static event AnomalyEventHandler<GeneratorStateChangedEventArgs> StateChanged;

    internal static void OnStateChanged(GeneratorStateChangedEventArgs args) => EventInvoker.Invoke(StateChanged, args);

    internal static void OnEngaged(GeneratorEngagedEventArgs args) =>
        EventInvoker.Invoke(Engaged, args);
}
