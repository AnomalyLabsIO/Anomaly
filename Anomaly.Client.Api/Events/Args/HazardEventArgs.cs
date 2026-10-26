using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

[ScriptType]
public sealed class HazardEventArgs : EventArgs, IHazardEvent
{
    public HazardEventArgs(Hazard hazard) => Hazard = hazard;
    [ScriptVisible]
    public Hazard Hazard { get; }
}
