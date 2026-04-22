using Anomaly.Client.Api.Features;

namespace Anomaly.Client.Api.Events.Args;

public class PickupAddedEventArgs : EventArgs
{
    public PickupAddedEventArgs(Pickup pickup)
    {
        Pickup = pickup;
    }

    public Pickup Pickup { get; }
}
