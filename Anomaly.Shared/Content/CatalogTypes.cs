using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Anomaly.Shared.Content
{
    /// <summary>
    /// Strict wire helpers for the items-only content catalog. All text is
    /// length-prefixed UTF-8 and all hashes are fixed-width SHA-256 values.
    /// </summary>
    internal static class CatalogIo
    {
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

        public static void WriteStr(BinaryWriter writer, string value)
        {
            byte[] bytes = Utf8.GetBytes(value ?? string.Empty);
            if (bytes.Length > ushort.MaxValue)
                throw new InvalidDataException("Catalog text exceeded the wire length limit.");
            writer.Write((ushort)bytes.Length);
            writer.Write(bytes);
        }

        public static string ReadStr(BinaryReader reader, int maximumBytes = ushort.MaxValue)
        {
            int length = reader.ReadUInt16();
            if (length > maximumBytes) throw new InvalidDataException("Catalog string exceeded its field limit.");
            return Utf8.GetString(ReadExactly(reader, length, "Catalog text was truncated."));
        }

        public static void WriteRequiredHash(BinaryWriter writer, byte[] hash)
        {
            if (hash == null || hash.Length != ContentCatalog.Sha256Bytes)
                throw new InvalidDataException("Catalog hashes must be 32-byte SHA-256 values.");
            writer.Write(hash);
        }

        public static byte[] ReadRequiredHash(BinaryReader reader) =>
            ReadExactly(reader, ContentCatalog.Sha256Bytes, "Catalog hash was truncated.");

        public static void WriteOptionalHash(BinaryWriter writer, byte[] hash)
        {
            if (hash == null || hash.Length == 0)
            {
                writer.Write((byte)0);
                return;
            }

            if (hash.Length != ContentCatalog.Sha256Bytes)
                throw new InvalidDataException("Catalog asset hashes must be 32-byte SHA-256 values.");
            writer.Write((byte)hash.Length);
            writer.Write(hash);
        }

        public static byte[] ReadOptionalHash(BinaryReader reader)
        {
            int length = reader.ReadByte();
            if (length == 0)
                return null;
            if (length != ContentCatalog.Sha256Bytes)
                throw new InvalidDataException("Catalog asset hash length was invalid.");
            return ReadExactly(reader, length, "Catalog asset hash was truncated.");
        }

        private static byte[] ReadExactly(BinaryReader reader, int length, string message)
        {
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length)
                throw new EndOfStreamException(message);
            return bytes;
        }
    }

    /// <summary>
    /// One custom item in the public-v1 catalog. The client clones the stock
    /// base template, so model and weight are deliberately not configurable.
    /// </summary>
    public sealed class CatalogEntry
    {
        /// <summary>Namespaced stable key, e.g. <c>mymod:magic_coin</c>.</summary>
        public string Key = string.Empty;

        /// <summary>Server-assigned ItemType value. Public v1 is limited to 0-127.</summary>
        public int AssignedId;

        /// <summary>Stock ItemType whose runtime template, model, and weight are inherited.</summary>
        public int BaseTemplateId;

        public string DisplayName = string.Empty;
        public string Description = string.Empty;

        /// <summary>Optional Texture2D address in an offered, validated pack.</summary>
        public string IconAddress;

        public void WriteTo(BinaryWriter writer)
        {
            writer.Write(AssignedId);
            writer.Write(BaseTemplateId);
            CatalogIo.WriteStr(writer, Key);
            CatalogIo.WriteStr(writer, DisplayName);
            CatalogIo.WriteStr(writer, Description);
            CatalogIo.WriteStr(writer, IconAddress);
        }

        public static CatalogEntry ReadFrom(BinaryReader reader)
        {
            return new CatalogEntry
            {
                AssignedId = reader.ReadInt32(),
                BaseTemplateId = reader.ReadInt32(),
                Key = CatalogIo.ReadStr(reader, 128),
                DisplayName = CatalogIo.ReadStr(reader, 512),
                Description = CatalogIo.ReadStr(reader, 4096),
                IconAddress = CatalogIo.ReadStr(reader, Assets.AssetAddress.MaxLength),
            };
        }
    }

    /// <summary>
    /// Canonical items-only catalog offered by an authenticated server. The
    /// server identity is authenticated out-of-band and is intentionally not
    /// duplicated in this payload.
    /// </summary>
    public sealed class ContentCatalog
    {
        public const byte CurrentSchemaVersion = 2;
        public const int Sha256Bytes = 32;

        public byte SchemaVersion = CurrentSchemaVersion;
        public byte[] CatalogSha256 = new byte[Sha256Bytes];
        public byte[] ManifestSha256 = new byte[Sha256Bytes];
        public List<CatalogEntry> Entries = new List<CatalogEntry>();
        public List<CatalogPack> Packs = new List<CatalogPack>();
    }

    /// <summary>Both files are bound into the authenticated catalog and transfer manifest.</summary>
    public sealed class CatalogPack
    {
        public string PackId = string.Empty;
        public byte[] IndexHash;
        public uint IndexSize;
        public byte[] BundleHash;
        public uint BundleSize;
        public void WriteTo(BinaryWriter writer)
        {
            CatalogIo.WriteStr(writer, PackId);
            CatalogIo.WriteRequiredHash(writer, IndexHash); writer.Write(IndexSize);
            CatalogIo.WriteRequiredHash(writer, BundleHash); writer.Write(BundleSize);
        }
        public static CatalogPack ReadFrom(BinaryReader reader) => new CatalogPack
        {
            PackId = CatalogIo.ReadStr(reader, 128), IndexHash = CatalogIo.ReadRequiredHash(reader), IndexSize = reader.ReadUInt32(),
            BundleHash = CatalogIo.ReadRequiredHash(reader), BundleSize = reader.ReadUInt32(),
        };
    }
}
