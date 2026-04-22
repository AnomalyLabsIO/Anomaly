using Anomaly.Client.Api.Features;

namespace Anomaly.Client.Api.Events.Args;

public class PickupRemovedEventArgs : EventArgs
{
    public PickupRemovedEventArgs(Pickup pickup)
    {
        Pickup = pickup;
    }

    public Pickup Pickup { get; }
}
