using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

[ScriptType]
public class DoorEventArgs : EventArgs, IDoorEvent
{
    public DoorEventArgs(Door door) => Door = door;
    [ScriptVisible]
    public Door Door { get; }
}
