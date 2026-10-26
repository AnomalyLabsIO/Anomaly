using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

/// <summary>
/// Fires when <see cref="Il2Cpp.AlphaWarheadController"/>'s <c>InProgress</c>
/// state transitions. <see cref="InProgress"/> reflects the new state.
/// </summary>
[ScriptType]
public class WarheadProgressChangedEventArgs : EventArgs
{
    public WarheadProgressChangedEventArgs(bool inProgress)
    {
        InProgress = inProgress;
    }

    /// <summary>True when the countdown is now running, false when it stopped (cancelled or detonated).</summary>
    [ScriptVisible]
    public bool InProgress { get; }
}
