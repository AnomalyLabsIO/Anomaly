using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Anomaly.Shared.Content
{
    /// <summary>Deterministic assignment and SHA-256 canonicalization for item catalogs.</summary>
    public static class CatalogBuilder
    {
        private static readonly byte[] Domain = Encoding.ASCII.GetBytes("ANOMALY-ITEM-CATALOG-V1");

        public static int ComputeIdBase(int maxDefinedEnumValue)
        {
            if (maxDefinedEnumValue == int.MaxValue)
                throw new InvalidOperationException("No custom item id can be allocated above the stock maximum.");
            return maxDefinedEnumValue + 1;
        }

        /// <summary>
        /// Sorts by stable key, rejects duplicate keys, assigns the complete
        /// range transactionally, and binds the resulting catalog to a file manifest.
        /// </summary>
        public static ContentCatalog Build(
            IEnumerable<CatalogEntry> entries,
            int itemIdBase,
            int itemIdCeiling,
            byte[] manifestSha256)
        {
            if (entries == null)
                throw new ArgumentNullException(nameof(entries));
            if (itemIdBase < 0 || itemIdCeiling > 127 || itemIdBase > itemIdCeiling + 1)
                throw new ArgumentOutOfRangeException(nameof(itemIdBase), "Custom item ids must fit the game's signed-byte wire range.");
            RequireSha256(manifestSha256, nameof(manifestSha256));

            var sorted = entries.OrderBy(entry => entry?.Key, StringComparer.Ordinal).ToList();
            if (sorted.Any(entry => entry == null))
                throw new InvalidDataException("Catalog contained a null item.");
            if (sorted.Select(entry => entry.Key).Distinct(StringComparer.Ordinal).Count() != sorted.Count)
                throw new InvalidDataException("Catalog contained duplicate item keys.");
            if (sorted.Count > Math.Max(0, itemIdCeiling - itemIdBase + 1))
                throw new InvalidOperationException(
                    $"Custom item id space exhausted (base {itemIdBase}, ceiling {itemIdCeiling}).");

            for (int index = 0; index < sorted.Count; index++)
                sorted[index].AssignedId = itemIdBase + index;

            var catalog = new ContentCatalog
            {
                ManifestSha256 = (byte[])manifestSha256.Clone(),
                Entries = sorted,
            };
            catalog.CatalogSha256 = ComputeCatalogHash(catalog);
            return catalog;
        }

        public static byte[] ComputeCatalogHash(ContentCatalog catalog)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));
            RequireSha256(catalog.ManifestSha256, nameof(catalog.ManifestSha256));

            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            using (var sha256 = SHA256.Create())
            {
                writer.Write(Domain);
                writer.Write(catalog.SchemaVersion);
                writer.Write(catalog.ManifestSha256);
                var sorted = (catalog.Entries ?? new List<CatalogEntry>())
                    .OrderBy(entry => entry?.Key, StringComparer.Ordinal)
                    .ToList();
                writer.Write(sorted.Count);
                foreach (var entry in sorted)
                {
                    if (entry == null)
                        throw new InvalidDataException("Catalog contained a null item.");
                    entry.WriteTo(writer);
                }
                writer.Flush();
                stream.Position = 0;
                return sha256.ComputeHash(stream);
            }
        }

        public static bool HashesEqual(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != ContentCatalog.Sha256Bytes ||
                right.Length != ContentCatalog.Sha256Bytes)
                return false;

            int difference = 0;
            for (int index = 0; index < left.Length; index++)
                difference |= left[index] ^ right[index];
            return difference == 0;
        }

        private static void RequireSha256(byte[] hash, string parameter)
        {
            if (hash == null || hash.Length != ContentCatalog.Sha256Bytes)
                throw new ArgumentException("A 32-byte SHA-256 value is required.", parameter);
        }
    }
}
