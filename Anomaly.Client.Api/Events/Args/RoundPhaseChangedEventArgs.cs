using System;
using Anomaly.Shared.Networking.Messages;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

/// <summary>
/// Fired when the server-reported <see cref="RoundPhase"/> changes. Carries both
/// the previous and new phases so handlers can react to specific transitions
/// (e.g. only reset state when transitioning to <see cref="RoundPhase.Restarted"/>).
/// </summary>
[ScriptType]
public sealed class RoundPhaseChangedEventArgs : EventArgs
{
    [ScriptVisible]
    public RoundPhase PreviousPhase { get; }
    [ScriptVisible]
    public RoundPhase NewPhase { get; }

    public RoundPhaseChangedEventArgs(RoundPhase previous, RoundPhase next)
    {
        PreviousPhase = previous;
        NewPhase = next;
    }
}
