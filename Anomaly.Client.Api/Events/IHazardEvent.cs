using Anomaly.Client.Api.Features;

namespace Anomaly.Client.Api.Events;

public interface IHazardEvent
{
    Hazard Hazard { get; }
}
