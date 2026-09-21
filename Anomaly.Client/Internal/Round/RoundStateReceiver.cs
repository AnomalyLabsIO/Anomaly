using Anomaly.Client.Api.Networking;
using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Client.Api.Internal;
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
    private static readonly TransactionalInitializer Initializer = new();
    private static readonly RoundStateConnectionGate ConnectionGate = new();

    internal static void Init()
    {
        Reset();
        Initializer.Initialize(new InitializationStep(
            () => AnomalyMessaging.MessageReceived += OnMessage,
            () => AnomalyMessaging.MessageReceived -= OnMessage));
    }

    internal static void Reset()
    {
        ConnectionGate.Reset();
        RoundEvents.CurrentPhase = ConnectionGate.CurrentPhase;
    }

    private static void OnMessage(IAnomalyMessage msg)
    {
        if (msg is RoundStateMessage roundState)
            DispatchPhase(roundState.Phase);
    }

    private static void DispatchPhase(RoundPhase newPhase)
    {
        if (!ConnectionGate.TryAccept(newPhase, out RoundPhase previous))
            return;

        var args = new RoundPhaseChangedEventArgs(previous, newPhase);
        RoundEvents.CurrentPhase = ConnectionGate.CurrentPhase;

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
