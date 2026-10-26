using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

[ScriptType]
public class PickupAddedEventArgs : EventArgs
{
    public PickupAddedEventArgs(Pickup pickup)
    {
        Pickup = pickup;
    }

    [ScriptVisible]
    public Pickup Pickup { get; }
}
