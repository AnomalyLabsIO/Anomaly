using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Anomaly.Client.Api.Modding;

public static class FeatureHealth
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, FeatureHealthRecord> Records = new(StringComparer.Ordinal);

    public static IReadOnlyDictionary<string, FeatureHealthRecord> Snapshot()
    {
        lock (Sync)
            return new ReadOnlyDictionary<string, FeatureHealthRecord>(
                new Dictionary<string, FeatureHealthRecord>(Records, StringComparer.Ordinal));
    }

    internal static void Report(string ownerId, string featureId, FeatureHealthStatus status, string detail)
    {
        Report(ownerId, featureId, status, detail, string.Empty, string.Empty, string.Empty);
    }

    internal static void Report(
        string ownerId,
        string featureId,
        FeatureHealthStatus status,
        string detail,
        string expectedTarget,
        string resolvedTarget,
        string remediation)
    {
        if (!ModManifestValidator.IsValidId(ownerId) && !ModManifestValidator.IsReservedId(ownerId))
            throw new ArgumentException("Owner id is invalid.", nameof(ownerId));
        if (string.IsNullOrWhiteSpace(featureId))
            throw new ArgumentException("Feature id is required.", nameof(featureId));
        lock (Sync)
            Records[ownerId + ":" + featureId] = new FeatureHealthRecord(
                ownerId,
                featureId,
                status,
                detail,
                expectedTarget,
                resolvedTarget,
                remediation);
    }

    internal static void ClearOwner(string ownerId)
    {
        lock (Sync)
        {
            var prefix = ownerId + ":";
            var remove = new List<string>();
            foreach (var key in Records.Keys)
                if (key.StartsWith(prefix, StringComparison.Ordinal)) remove.Add(key);
            foreach (var key in remove) Records.Remove(key);
        }
    }
}
