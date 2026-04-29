using Anomaly.Client.Api.Features;

namespace Anomaly.Client.Api.Events.Args;

/// <summary>Wraps an <see cref="Elevator"/> reference for chamber lifecycle / movement events.</summary>
public class ElevatorEventArgs : EventArgs
{
    public ElevatorEventArgs(Elevator elevator) { Elevator = elevator; }
    public Elevator Elevator { get; }
}
