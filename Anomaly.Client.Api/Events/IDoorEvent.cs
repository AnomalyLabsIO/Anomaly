using Anomaly.Client.Api.Features;

namespace Anomaly.Client.Api.Events;

public interface IDoorEvent
{
    Door Door { get; }
}
