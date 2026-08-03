using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Anomaly.Client.Api.Content;

public static class ItemCatalogValidator
{
    private static readonly Regex KeyPattern = new(
        @"^[a-z][a-z0-9_]{0,31}(\.[a-z][a-z0-9_]{0,31}){0,39}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex HashPattern = new(
        @"^[0-9a-f]{64}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex FingerprintPattern = new(
        @"^[0-9a-fA-F]{64}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IReadOnlyList<string> Validate(ItemCatalog catalog)
    {
        var errors = new List<string>();
        if (catalog == null)
        {
            errors.Add("Catalog is null.");
            return errors;
        }
        if (catalog.SchemaVersion != 1) errors.Add("Unsupported item catalog schema.");
        if (!HashPattern.IsMatch(catalog.CatalogSha256 ?? string.Empty))
            errors.Add("CatalogSha256 must be lowercase SHA-256.");
        if (!FingerprintPattern.IsMatch(catalog.ServerFingerprint ?? string.Empty))
            errors.Add("ServerFingerprint must be a SHA-256 identity fingerprint.");

        var assets = catalog.Assets ?? new List<PassiveAssetReference>();
        var items = catalog.Items ?? new List<ItemDefinition>();
        if (assets.Count > ItemContentLimits.MaxAssets) errors.Add("Asset count exceeds the public-v1 limit.");
        if (items.Count > ItemContentLimits.MaxItems) errors.Add("Item count exceeds the public-v1 limit.");

        var assetKeys = new HashSet<string>(StringComparer.Ordinal);
        long aggregate = 0;
        foreach (var asset in assets)
        {
            if (asset == null || !KeyPattern.IsMatch(asset.Key ?? string.Empty))
            {
                errors.Add("An asset key is invalid.");
                continue;
            }
            if (!assetKeys.Add(asset.Key)) errors.Add($"Duplicate asset key '{asset.Key}'.");
            if (!HashPattern.IsMatch(asset.Sha256 ?? string.Empty))
                errors.Add($"Asset '{asset.Key}' has an invalid SHA-256.");
            if (!System.Enum.IsDefined(typeof(PassiveAssetKind), asset.Kind))
                errors.Add($"Asset '{asset.Key}' has an unsupported passive format.");
            if (asset.SizeBytes <= 0 || asset.SizeBytes > ItemContentLimits.MaxAssetBytes)
                errors.Add($"Asset '{asset.Key}' has an invalid size.");
            if (aggregate > ItemContentLimits.MaxCatalogBytes - Math.Max(0, asset.SizeBytes))
                aggregate = ItemContentLimits.MaxCatalogBytes + 1;
            else
                aggregate += Math.Max(0, asset.SizeBytes);
        }
        if (aggregate > ItemContentLimits.MaxCatalogBytes)
            errors.Add("Aggregate asset bytes exceed the public-v1 limit.");

        var itemKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (item == null || !KeyPattern.IsMatch(item.Key ?? string.Empty))
            {
                errors.Add("An item key is invalid.");
                continue;
            }
            if (!itemKeys.Add(item.Key)) errors.Add($"Duplicate item key '{item.Key}'.");
            if (string.IsNullOrWhiteSpace(item.DisplayName) ||
                item.DisplayName.Length > ItemContentLimits.MaxTextLength)
                errors.Add($"Item '{item.Key}' has an invalid display name.");
            if (!ItemContentLimits.ApprovedBuiltInPrefabs.Contains(item.BuiltInPrefab ?? string.Empty))
                errors.Add($"Item '{item.Key}' must reference an approved built-in prefab id.");
            var itemAssets = item.AssetKeys ?? new List<string>();
            if (itemAssets.Count > ItemContentLimits.MaxAssetsPerItem)
                errors.Add($"Item '{item.Key}' references too many assets.");
            if (itemAssets.Count != itemAssets.Distinct(StringComparer.Ordinal).Count())
                errors.Add($"Item '{item.Key}' contains duplicate asset references.");
            foreach (var assetKey in itemAssets.Distinct(StringComparer.Ordinal))
                if (!assetKeys.Contains(assetKey))
                    errors.Add($"Item '{item.Key}' references unknown asset '{assetKey}'.");
        }

        if (errors.Count == 0 && HashPattern.IsMatch(catalog.CatalogSha256 ?? string.Empty))
        {
            try
            {
                if (!ItemCatalogCanonicalizer.MatchesDeclaredHash(catalog))
                    errors.Add("CatalogSha256 does not match the canonical catalog payload.");
            }
            catch (ArgumentException)
            {
                errors.Add("Catalog cannot be canonicalized.");
            }
        }

        return errors;
    }
}
