using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Handlers;

public static class LockerEvents
{
    /// <summary>Fires for changed replicated chamber masks after the initial spawn snapshot.</summary>
    [ScriptEvent] public static event AnomalyEventHandler<LockerStateChangedEventArgs> StateChanged;
    internal static void OnStateChanged(LockerStateChangedEventArgs args) => EventInvoker.Invoke(StateChanged, args);
}
