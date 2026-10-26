using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

/// <summary>An observed target-state change, not a cancellable interaction or proof that the door finished moving.</summary>
[ScriptType]
public sealed class DoorStateChangedEventArgs : DoorEventArgs
{
    public DoorStateChangedEventArgs(Door door, bool previousIsOpen, bool isOpen) : base(door)
    {
        PreviousIsOpen = previousIsOpen;
        IsOpen = isOpen;
    }

    [ScriptVisible]
    public bool PreviousIsOpen { get; }
    [ScriptVisible]
    public bool IsOpen { get; }
}
