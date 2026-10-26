using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

/// <summary>A change in the client's observed firearm state, including first observation of available fields.</summary>
[ScriptType]
public class FirearmStateChangedEventArgs : EventArgs, IItemEvent, IPlayerEvent
{
    public FirearmStateChangedEventArgs(FirearmItem firearm, FirearmState? previous, FirearmState current)
    {
        Firearm = firearm;
        Player = firearm?.CurrentOwner;
        Previous = previous;
        Current = current;
    }

    [ScriptVisible]
    public FirearmItem Firearm { get; }
    public Item Item => Firearm;
    [ScriptVisible]
    public Player Player { get; }

    /// <summary>Null when this is the first observation of this item in the session.</summary>
    [ScriptVisible]
    public FirearmState? Previous { get; }
    [ScriptVisible]
    public FirearmState Current { get; }
}
