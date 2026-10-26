using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

/// <summary>Wraps an <see cref="Elevator"/> reference for chamber lifecycle / movement events.</summary>
[ScriptType]
public class ElevatorEventArgs : EventArgs
{
    public ElevatorEventArgs(Elevator elevator) { Elevator = elevator; }
    [ScriptVisible]
    public Elevator Elevator { get; }
}
