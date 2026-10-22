using Anomaly.Client.Api.Features;

namespace Anomaly.Client.Api.Events;

/// <summary>An event associated with a client-observed player.</summary>
public interface IPlayerEvent
{
    Player Player { get; }
}
