using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

[ScriptType]
public sealed class DoorLocksChangedEventArgs : DoorEventArgs
{
    public DoorLocksChangedEventArgs(Door door, ushort previousLocks, ushort activeLocks) : base(door)
    {
        PreviousLocks = previousLocks;
        ActiveLocks = activeLocks;
    }

    [ScriptVisible]
    public ushort PreviousLocks { get; }
    [ScriptVisible]
    public ushort ActiveLocks { get; }
}
