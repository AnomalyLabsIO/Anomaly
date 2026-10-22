using Anomaly.Client.Api.Features;

namespace Anomaly.Client.Api.Events;

/// <summary>An event associated with an item. The item may be unavailable after removal.</summary>
public interface IItemEvent
{
    Item Item { get; }
}
