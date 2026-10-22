using Anomaly.Client.Api.Features;

namespace Anomaly.Client.Api.Events.Args;

/// <summary>An updated replicated chamber mask. Local door animations may finish later.</summary>
public sealed class LockerStateChangedEventArgs : EventArgs
{
    public LockerStateChangedEventArgs(Locker locker, ushort previousOpenedChambers, ushort openedChambers)
    {
        Locker = locker;
        PreviousOpenedChambers = previousOpenedChambers;
        OpenedChambers = openedChambers;
    }

    public Locker Locker { get; }
    public ushort PreviousOpenedChambers { get; }
    public ushort OpenedChambers { get; }
    public ushort ChangedChambers => (ushort)(PreviousOpenedChambers ^ OpenedChambers);
}
