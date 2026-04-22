using System;
using Anomaly.Shared.Networking.Messages;

namespace Anomaly.Client.Api.Events.Args;

/// <summary>
/// Fired when the server-reported <see cref="RoundPhase"/> changes. Carries both
/// the previous and new phases so handlers can react to specific transitions
/// (e.g. only reset state when transitioning to <see cref="RoundPhase.Restarted"/>).
/// </summary>
public sealed class RoundPhaseChangedEventArgs : EventArgs
{
    public RoundPhase PreviousPhase { get; }
    public RoundPhase NewPhase { get; }

    public RoundPhaseChangedEventArgs(RoundPhase previous, RoundPhase next)
    {
        PreviousPhase = previous;
        NewPhase = next;
    }
}
