using Anomaly.Client.Api.Features;

namespace Anomaly.Client.Api.Events.Args;

/// <summary>A change in the client's observed firearm state, including first observation of available fields.</summary>
public class FirearmStateChangedEventArgs : EventArgs, IItemEvent, IPlayerEvent
{
    public FirearmStateChangedEventArgs(FirearmItem firearm, FirearmState? previous, FirearmState current)
    {
        Firearm = firearm;
        Player = firearm?.CurrentOwner;
        Previous = previous;
        Current = current;
    }

    public FirearmItem Firearm { get; }
    public Item Item => Firearm;
    public Player Player { get; }

    /// <summary>Null when this is the first observation of this item in the session.</summary>
    public FirearmState? Previous { get; }
    public FirearmState Current { get; }
}
