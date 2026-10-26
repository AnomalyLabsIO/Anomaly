using Anomaly.Client.Api.Events.Args;
using Anomaly.Shared.Networking.Messages;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Handlers;

/// <summary>
/// Client-side round lifecycle events. Events fire when the server broadcasts a
/// <see cref="RoundStateMessage"/> (see <c>RoundStateBroadcaster</c> in Anomaly.Server).
/// </summary>
/// <remarks>
/// <see cref="PhaseChanged"/> fires on every transition and carries both the
/// previous and new phase. Per-phase events are provided as a convenience for
/// handlers that only care about a specific transition.
/// </remarks>
public static class RoundEvents
{
    [ScriptEvent]
    public static event AnomalyEventHandler<RoundPhaseChangedEventArgs> PhaseChanged;
    [ScriptEvent]
    public static event AnomalyEventHandler<RoundPhaseChangedEventArgs> WaitingForPlayers;
    [ScriptEvent]
    public static event AnomalyEventHandler<RoundPhaseChangedEventArgs> Starting;
    [ScriptEvent]
    public static event AnomalyEventHandler<RoundPhaseChangedEventArgs> Started;
    [ScriptEvent]
    public static event AnomalyEventHandler<RoundPhaseChangedEventArgs> Ending;
    [ScriptEvent]
    public static event AnomalyEventHandler<RoundPhaseChangedEventArgs> Ended;
    [ScriptEvent]
    public static event AnomalyEventHandler<RoundPhaseChangedEventArgs> Restarted;

    /// <summary>
    /// The most recently received phase from the server. Defaults to
    /// <see cref="RoundPhase.WaitingForPlayers"/> before the first message arrives.
    /// </summary>
    public static RoundPhase CurrentPhase { get; internal set; } = RoundPhase.WaitingForPlayers;

    internal static void OnPhaseChanged(RoundPhaseChangedEventArgs args) => EventInvoker.Invoke(PhaseChanged, args);

    internal static void OnWaitingForPlayers(RoundPhaseChangedEventArgs args) => EventInvoker.Invoke(WaitingForPlayers, args);

    internal static void OnStarting(RoundPhaseChangedEventArgs args) => EventInvoker.Invoke(Starting, args);

    internal static void OnStarted(RoundPhaseChangedEventArgs args) => EventInvoker.Invoke(Started, args);

    internal static void OnEnding(RoundPhaseChangedEventArgs args) => EventInvoker.Invoke(Ending, args);

    internal static void OnEnded(RoundPhaseChangedEventArgs args) => EventInvoker.Invoke(Ended, args);

    internal static void OnRestarted(RoundPhaseChangedEventArgs args) => EventInvoker.Invoke(Restarted, args);
}
