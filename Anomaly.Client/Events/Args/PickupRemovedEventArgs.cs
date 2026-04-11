using Anomaly.Client.API.Features;

namespace Anomaly.Client.Events.Args;

public class PickupRemovedEventArgs : EventArgs
{
    public PickupRemovedEventArgs(Pickup pickup)
    {
        Pickup = pickup;
    }

    public Pickup Pickup { get; }
}
