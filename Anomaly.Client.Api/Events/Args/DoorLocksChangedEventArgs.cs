using Anomaly.Client.Api.Features;

namespace Anomaly.Client.Api.Events.Args;

public sealed class DoorLocksChangedEventArgs : DoorEventArgs
{
    public DoorLocksChangedEventArgs(Door door, ushort previousLocks, ushort activeLocks) : base(door)
    {
        PreviousLocks = previousLocks;
        ActiveLocks = activeLocks;
    }

    public ushort PreviousLocks { get; }
    public ushort ActiveLocks { get; }
}
