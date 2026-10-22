using Anomaly.Client.Api.Features;

namespace Anomaly.Client.Api.Events.Args;

public class DoorEventArgs : EventArgs, IDoorEvent
{
    public DoorEventArgs(Door door) => Door = door;
    public Door Door { get; }
}
