using Anomaly.Client.API.Features;

namespace Anomaly.Client.Events.Args;

public class PickupAddedEventArgs : EventArgs
{
    public PickupAddedEventArgs(Pickup pickup)
    {
        Pickup = pickup;
    }

    public Pickup Pickup { get; }
}
