using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

/// <summary>An updated replicated chamber mask. Local door animations may finish later.</summary>
[ScriptType]
public sealed class LockerStateChangedEventArgs : EventArgs
{
    public LockerStateChangedEventArgs(Locker locker, ushort previousOpenedChambers, ushort openedChambers)
    {
        Locker = locker;
        PreviousOpenedChambers = previousOpenedChambers;
        OpenedChambers = openedChambers;
    }

    [ScriptVisible]
    public Locker Locker { get; }
    [ScriptVisible]
    public ushort PreviousOpenedChambers { get; }
    [ScriptVisible]
    public ushort OpenedChambers { get; }
    [ScriptVisible]
    public ushort ChangedChambers => (ushort)(PreviousOpenedChambers ^ OpenedChambers);
}
