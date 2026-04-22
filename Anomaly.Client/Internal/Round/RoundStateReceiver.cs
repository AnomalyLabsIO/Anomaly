using Anomaly.Client.Api.Networking;
using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;

namespace Anomaly.Client.Internal.Round;

/// <summary>
/// Subscribes to the Anomaly messaging pipeline and dispatches
/// <see cref="RoundStateMessage"/> into the <see cref="RoundEvents"/> events.
/// Registered once during Anomaly client initialization.
/// </summary>
internal static class RoundStateReceiver
{
    private static bool _hooked;

    internal static void Init()
    {
        if (_hooked) return;
        _hooked = true;
        AnomalyMessaging.MessageReceived += OnMessage;
    }

    private static void OnMessage(IAnomalyMessage msg)
    {
        if (msg is RoundStateMessage roundState)
            DispatchPhase(roundState.Phase);
    }

    private static void DispatchPhase(RoundPhase newPhase)
    {
        var previous = RoundEvents.CurrentPhase;
        if (newPhase == previous) return;

        var args = new RoundPhaseChangedEventArgs(previous, newPhase);
        RoundEvents.CurrentPhase = newPhase;

        RoundEvents.OnPhaseChanged(args);

        switch (newPhase)
        {
            case RoundPhase.WaitingForPlayers: RoundEvents.OnWaitingForPlayers(args); break;
            case RoundPhase.Starting:          RoundEvents.OnStarting(args); break;
            case RoundPhase.InProgress:        RoundEvents.OnStarted(args); break;
            case RoundPhase.Ending:            RoundEvents.OnEnding(args); break;
            case RoundPhase.Ended:             RoundEvents.OnEnded(args); break;
            case RoundPhase.Restarted:         RoundEvents.OnRestarted(args); break;
        }
    }
}
