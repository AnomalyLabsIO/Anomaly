using Anomaly.Client.Api.Features;

namespace Anomaly.Client.Api.Events.Args;

public sealed class HazardEventArgs : EventArgs, IHazardEvent
{
    public HazardEventArgs(Hazard hazard) => Hazard = hazard;
    public Hazard Hazard { get; }
}
