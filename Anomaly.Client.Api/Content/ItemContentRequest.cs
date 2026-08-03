using System;

namespace Anomaly.Client.Api.Content;

public sealed class ItemContentRequest
{
    public Guid RequestId { get; init; }
    public string AssetKey { get; init; } = string.Empty;
    public long ResumeOffset { get; init; }
    public long ExpectedSize { get; init; }
    public bool IsAlreadyAvailable { get; init; }
}
