using System;
using System.Security.Cryptography;

namespace Anomaly.Client.Api.Content;

/// <summary>
/// User approval for one catalog presented by one authenticated server
/// identity. Any server identity or canonical catalog change requires new
/// consent.
/// </summary>
public sealed class ItemCatalogConsentRecord
{
    public string ServerFingerprint { get; set; } = string.Empty;
    public string CatalogSha256 { get; set; } = string.Empty;
    public DateTimeOffset ApprovedAtUtc { get; set; }

    public static ItemCatalogConsentRecord Create(
        string authenticatedServerFingerprint,
        ItemCatalog catalog,
        DateTimeOffset approvedAtUtc)
    {
        ItemCatalogGuard.ValidateAuthenticatedCatalog(catalog, authenticatedServerFingerprint);
        return new ItemCatalogConsentRecord
        {
            ServerFingerprint = authenticatedServerFingerprint.ToLowerInvariant(),
            CatalogSha256 = catalog.CatalogSha256.ToLowerInvariant(),
            ApprovedAtUtc = approvedAtUtc.ToUniversalTime(),
        };
    }

    public bool Allows(string authenticatedServerFingerprint, string catalogSha256)
    {
        return FixedTimeHashEquals(ServerFingerprint, authenticatedServerFingerprint) &&
               FixedTimeHashEquals(CatalogSha256, catalogSha256);
    }

    private static bool FixedTimeHashEquals(string expected, string actual)
    {
        if (!ItemCatalogCanonicalizer.TryDecodeSha256(expected, out var expectedBytes) ||
            !ItemCatalogCanonicalizer.TryDecodeSha256(actual, out var actualBytes))
            return false;
        return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }
}
