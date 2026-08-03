using System;
using System.IO;

namespace Anomaly.Client.Api.Content;

/// <summary>
/// Cache provenance for passive content. Identical bytes received under a
/// different server identity or catalog do not share an activation namespace.
/// </summary>
public sealed class ItemContentCacheKey : IEquatable<ItemContentCacheKey>
{
    public string ServerFingerprint { get; init; } = string.Empty;
    public string CatalogSha256 { get; init; } = string.Empty;
    public string ContentSha256 { get; init; } = string.Empty;

    public static ItemContentCacheKey Create(
        string serverFingerprint,
        string catalogSha256,
        string contentSha256)
    {
        RequireHash(serverFingerprint, nameof(serverFingerprint));
        RequireHash(catalogSha256, nameof(catalogSha256));
        RequireHash(contentSha256, nameof(contentSha256));
        return new ItemContentCacheKey
        {
            ServerFingerprint = serverFingerprint.ToLowerInvariant(),
            CatalogSha256 = catalogSha256.ToLowerInvariant(),
            ContentSha256 = contentSha256.ToLowerInvariant(),
        };
    }

    public string GetRelativePath()
    {
        RequireHash(ServerFingerprint, nameof(ServerFingerprint));
        RequireHash(CatalogSha256, nameof(CatalogSha256));
        RequireHash(ContentSha256, nameof(ContentSha256));
        return Path.Combine(ServerFingerprint, CatalogSha256, ContentSha256 + ".blob");
    }

    public bool Equals(ItemContentCacheKey other)
    {
        if (other == null) return false;
        return string.Equals(ServerFingerprint, other.ServerFingerprint, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(CatalogSha256, other.CatalogSha256, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(ContentSha256, other.ContentSha256, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object obj) => Equals(obj as ItemContentCacheKey);

    public override int GetHashCode() => HashCode.Combine(
        (ServerFingerprint ?? string.Empty).ToLowerInvariant(),
        (CatalogSha256 ?? string.Empty).ToLowerInvariant(),
        (ContentSha256 ?? string.Empty).ToLowerInvariant());

    private static void RequireHash(string value, string parameterName)
    {
        if (!ItemCatalogCanonicalizer.TryDecodeSha256(value, out _))
            throw new ArgumentException("Value must be a SHA-256 hex string.", parameterName);
    }
}
