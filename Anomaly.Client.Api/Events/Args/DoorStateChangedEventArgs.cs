using Anomaly.Client.Api.Features;

namespace Anomaly.Client.Api.Events.Args;

/// <summary>An observed target-state change, not a cancellable interaction or proof that the door finished moving.</summary>
public sealed class DoorStateChangedEventArgs : DoorEventArgs
{
    public DoorStateChangedEventArgs(Door door, bool previousIsOpen, bool isOpen) : base(door)
    {
        PreviousIsOpen = previousIsOpen;
        IsOpen = isOpen;
    }

    public bool PreviousIsOpen { get; }
    public bool IsOpen { get; }
}
