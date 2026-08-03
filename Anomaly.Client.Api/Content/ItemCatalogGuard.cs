using System;
using System.Security.Cryptography;

namespace Anomaly.Client.Api.Content;

internal static class ItemCatalogGuard
{
    public static void ValidateAuthenticatedCatalog(
        ItemCatalog catalog,
        string authenticatedServerFingerprint,
        ItemCatalogConsentRecord consent = null)
    {
        if (catalog == null) throw new ArgumentNullException(nameof(catalog));
        if (!HashesEqual(catalog.ServerFingerprint, authenticatedServerFingerprint))
            throw new InvalidOperationException("Catalog server fingerprint does not match the authenticated server.");

        var errors = ItemCatalogValidator.Validate(catalog);
        if (errors.Count != 0)
            throw new InvalidOperationException("Item catalog is invalid: " + string.Join(" ", errors));

        if (consent != null && !consent.Allows(authenticatedServerFingerprint, catalog.CatalogSha256))
            throw new InvalidOperationException("This authenticated server catalog has not been approved.");
    }

    private static bool HashesEqual(string first, string second)
    {
        if (!ItemCatalogCanonicalizer.TryDecodeSha256(first, out var firstBytes) ||
            !ItemCatalogCanonicalizer.TryDecodeSha256(second, out var secondBytes))
            return false;
        return CryptographicOperations.FixedTimeEquals(firstBytes, secondBytes);
    }
}
