using System;

namespace Anomaly.Client.Api.Modding;

public sealed class FeatureHealthRecord
{
    internal FeatureHealthRecord(
        string ownerId,
        string featureId,
        FeatureHealthStatus status,
        string detail,
        string expectedTarget,
        string resolvedTarget,
        string remediation)
    {
        OwnerId = ownerId;
        FeatureId = featureId;
        Status = status;
        Detail = detail ?? string.Empty;
        ExpectedTarget = expectedTarget ?? string.Empty;
        ResolvedTarget = resolvedTarget ?? string.Empty;
        Remediation = remediation ?? string.Empty;
        UpdatedUtc = DateTimeOffset.UtcNow;
    }

    public string OwnerId { get; }
    public string FeatureId { get; }
    public FeatureHealthStatus Status { get; }
    public string Detail { get; }
    public string ExpectedTarget { get; }
    public string ResolvedTarget { get; }
    public string Remediation { get; }
    public DateTimeOffset UpdatedUtc { get; }
}
