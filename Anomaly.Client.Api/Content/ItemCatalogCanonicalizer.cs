using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Anomaly.Client.Api.Content;

/// <summary>
/// Produces the public-v1 canonical catalog representation. Collection order
/// is not significant. <see cref="ItemCatalog.CatalogSha256"/> is deliberately
/// excluded so a catalog can carry the hash of its own canonical payload.
/// </summary>
public static class ItemCatalogCanonicalizer
{
    private static readonly byte[] Domain = Encoding.ASCII.GetBytes("ANOMALY-ITEM-CATALOG-V1");

    public static byte[] Serialize(ItemCatalog catalog)
    {
        if (catalog == null) throw new ArgumentNullException(nameof(catalog));

        var assets = catalog.Assets ?? throw new ArgumentException("Catalog assets are null.", nameof(catalog));
        var items = catalog.Items ?? throw new ArgumentException("Catalog items are null.", nameof(catalog));
        if (assets.Count > ItemContentLimits.MaxAssets || items.Count > ItemContentLimits.MaxItems)
            throw new ArgumentException("Catalog collections exceed public-v1 limits.", nameof(catalog));
        RequireBoundedText(catalog.ServerFingerprint, 64, "server fingerprint", catalog);

        using var stream = new MemoryStream();
        WriteBytes(stream, Domain);
        WriteInt32(stream, catalog.SchemaVersion);
        WriteString(stream, (catalog.ServerFingerprint ?? string.Empty).ToLowerInvariant());

        WriteInt32(stream, assets.Count);
        foreach (var asset in assets.OrderBy(asset => asset?.Key, StringComparer.Ordinal))
        {
            if (asset == null) throw new ArgumentException("Catalog contains a null asset.", nameof(catalog));
            RequireBoundedText(asset.Key, ItemContentLimits.MaxKeyLength, "asset key", catalog);
            RequireBoundedText(asset.Sha256, 64, "asset hash", catalog);
            WriteString(stream, asset.Key ?? string.Empty);
            WriteInt32(stream, (int)asset.Kind);
            WriteString(stream, (asset.Sha256 ?? string.Empty).ToLowerInvariant());
            WriteInt64(stream, asset.SizeBytes);
        }

        WriteInt32(stream, items.Count);
        foreach (var item in items.OrderBy(item => item?.Key, StringComparer.Ordinal))
        {
            if (item == null) throw new ArgumentException("Catalog contains a null item.", nameof(catalog));
            RequireBoundedText(item.Key, ItemContentLimits.MaxKeyLength, "item key", catalog);
            RequireBoundedText(item.DisplayName, ItemContentLimits.MaxTextLength, "item display name", catalog);
            RequireBoundedText(item.BuiltInPrefab, 64, "built-in prefab id", catalog);
            WriteString(stream, item.Key ?? string.Empty);
            WriteString(stream, item.DisplayName ?? string.Empty);
            WriteString(stream, item.BuiltInPrefab ?? string.Empty);

            var assetKeys = item.AssetKeys ?? throw new ArgumentException(
                $"Item '{item.Key}' has null asset references.", nameof(catalog));
            if (assetKeys.Count > ItemContentLimits.MaxAssetsPerItem)
                throw new ArgumentException($"Item '{item.Key}' has too many asset references.", nameof(catalog));
            WriteInt32(stream, assetKeys.Count);
            foreach (var assetKey in assetKeys.OrderBy(value => value, StringComparer.Ordinal))
            {
                RequireBoundedText(assetKey, ItemContentLimits.MaxKeyLength, "item asset reference", catalog);
                WriteString(stream, assetKey ?? string.Empty);
            }
        }

        return stream.ToArray();
    }

    public static string ComputeSha256(ItemCatalog catalog)
    {
        using var sha256 = SHA256.Create();
        return Convert.ToHexString(sha256.ComputeHash(Serialize(catalog))).ToLowerInvariant();
    }

    public static bool MatchesDeclaredHash(ItemCatalog catalog)
    {
        if (catalog == null || !TryDecodeSha256(catalog.CatalogSha256, out var declared))
            return false;

        var computedHex = ComputeSha256(catalog);
        TryDecodeSha256(computedHex, out var computed);
        return CryptographicOperations.FixedTimeEquals(declared, computed);
    }

    internal static bool TryDecodeSha256(string value, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (value == null || value.Length != 64) return false;

        try
        {
            bytes = Convert.FromHexString(value);
            return bytes.Length == 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static void WriteString(Stream stream, string value) =>
        WriteBytes(stream, Encoding.UTF8.GetBytes(value));

    private static void RequireBoundedText(string value, int maxLength, string field, ItemCatalog catalog)
    {
        if (value == null || value.Length > maxLength)
            throw new ArgumentException($"Catalog {field} exceeds canonicalization limits.", nameof(catalog));
    }

    private static void WriteBytes(Stream stream, IReadOnlyCollection<byte> value)
    {
        WriteInt32(stream, value.Count);
        foreach (var item in value) stream.WriteByte(item);
    }

    private static void WriteInt32(Stream stream, int value)
    {
        unchecked
        {
            stream.WriteByte((byte)(value >> 24));
            stream.WriteByte((byte)(value >> 16));
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)value);
        }
    }

    private static void WriteInt64(Stream stream, long value)
    {
        unchecked
        {
            for (var shift = 56; shift >= 0; shift -= 8)
                stream.WriteByte((byte)(value >> shift));
        }
    }
}
