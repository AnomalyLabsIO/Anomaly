using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Anomaly.Shared.Content;
using Anomaly.Shared.Assets;

namespace Anomaly.Shared.Networking.Messages
{
    /// <summary>Authenticated server offer for one immutable items-only catalog.</summary>
    public class ContentCatalogMessage : AuthenticatedServerToClientMessage, IAnomalyMessage
    {
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        private const int FixedPayloadBytes = 1 + ContentCatalog.Sha256Bytes + ContentCatalog.Sha256Bytes + 2 + 2;

        public const int MaxEntries = 127;
        public const int MaxPacks = FileManifestMessage.MaxFiles / 2;
        public const int MaxKeyBytes = 128;
        public const int MaxNameBytes = 512;
        public const int MaxDescriptionBytes = 4096;
        public const int MaxCatalogPayloadBytes = 256 * 1024;
        public override int MaxPayloadBytes => MaxCatalogPayloadBytes;
        public string MessageName => "anomaly:content:catalog";
        public MessageChannel TransportChannel => MessageChannel.ReliableOrdered;

        public ContentCatalog Catalog { get; set; } = new ContentCatalog();

        public void Serialize(BinaryWriter writer)
        {
            ValidateCatalog(Catalog, validateDeclaredHash: true);
            writer.Write(Catalog.SchemaVersion);
            CatalogIo.WriteRequiredHash(writer, Catalog.CatalogSha256);
            CatalogIo.WriteRequiredHash(writer, Catalog.ManifestSha256);
            writer.Write((ushort)Catalog.Entries.Count);
            foreach (CatalogEntry entry in Catalog.Entries)
                entry.WriteTo(writer);
            writer.Write((ushort)Catalog.Packs.Count);
            foreach (CatalogPack pack in Catalog.Packs) pack.WriteTo(writer);
        }

        public void Deserialize(BinaryReader reader)
        {
            var catalog = new ContentCatalog
            {
                SchemaVersion = reader.ReadByte(),
                CatalogSha256 = CatalogIo.ReadRequiredHash(reader),
                ManifestSha256 = CatalogIo.ReadRequiredHash(reader),
            };
            int count = reader.ReadUInt16();
            if (count > MaxEntries)
                throw new InvalidDataException($"Content catalog count exceeded {MaxEntries}.");
            catalog.Entries = new List<CatalogEntry>(count);
            for (int index = 0; index < count; index++)
                catalog.Entries.Add(CatalogEntry.ReadFrom(reader));
            int packCount = reader.ReadUInt16();
            if (packCount > MaxPacks) throw new InvalidDataException("Catalog pack count exceeded its limit.");
            catalog.Packs = new List<CatalogPack>(packCount);
            for (int index = 0; index < packCount; index++) catalog.Packs.Add(CatalogPack.ReadFrom(reader));
            ValidateCatalog(catalog, validateDeclaredHash: true);
            Catalog = catalog;
        }

        public static void ValidateCatalog(ContentCatalog catalog, bool validateDeclaredHash)
        {
            if (catalog == null || catalog.Entries == null || catalog.Packs == null)
                throw new InvalidDataException("Content catalog was null.");
            if (catalog.SchemaVersion != ContentCatalog.CurrentSchemaVersion)
                throw new InvalidDataException($"Unsupported content catalog schema {catalog.SchemaVersion}.");
            if (catalog.CatalogSha256 == null || catalog.CatalogSha256.Length != ContentCatalog.Sha256Bytes ||
                catalog.ManifestSha256 == null || catalog.ManifestSha256.Length != ContentCatalog.Sha256Bytes)
                throw new InvalidDataException("Content catalog hashes must be 32-byte SHA-256 values.");
            if (catalog.Entries.Count > MaxEntries || catalog.Packs.Count > MaxPacks)
                throw new InvalidDataException($"Content catalog count exceeded {MaxEntries}.");

            long encodedPayloadBytes = FixedPayloadBytes;
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var ids = new HashSet<int>();
            string previousKey = null;
            int? previousId = null;
            foreach (CatalogEntry entry in catalog.Entries)
            {
                if (entry == null)
                    throw new InvalidDataException("Content catalog contained a null item.");
                int keyBytes = GetUtf8ByteCount(entry.Key, "item key");
                int nameBytes = GetUtf8ByteCount(entry.DisplayName, "display name");
                int descriptionBytes = GetUtf8ByteCount(entry.Description ?? string.Empty, "description");
                if (!ModNamespace.IsValidContentKey(entry.Key) || keyBytes > MaxKeyBytes ||
                    !keys.Add(entry.Key))
                    throw new InvalidDataException("Content catalog contained an invalid or duplicate item key.");
                if (previousKey != null && StringComparer.Ordinal.Compare(previousKey, entry.Key) >= 0)
                    throw new InvalidDataException("Content catalog entries were not in canonical key order.");
                previousKey = entry.Key;
                if (entry.AssignedId < 0 || entry.AssignedId > 127 || !ids.Add(entry.AssignedId))
                    throw new InvalidDataException("Content catalog contained an invalid or duplicate assigned item id.");
                if (previousId.HasValue && entry.AssignedId != previousId.Value + 1)
                    throw new InvalidDataException(
                        "Content catalog assigned item ids were not contiguous in canonical key order.");
                previousId = entry.AssignedId;
                if (entry.BaseTemplateId < 0 || entry.BaseTemplateId > 127)
                    throw new InvalidDataException("Content catalog base item id was outside the signed-byte wire range.");
                if (string.IsNullOrWhiteSpace(entry.DisplayName) ||
                    nameBytes > MaxNameBytes ||
                    descriptionBytes > MaxDescriptionBytes)
                    throw new InvalidDataException("Content catalog item text was invalid or too long.");
                bool hasIcon = !string.IsNullOrEmpty(entry.IconAddress);
                if (hasIcon && (!AssetAddress.TryParse(entry.IconAddress, out AssetAddress address) || address.IsGameReplacement))
                    throw new InvalidDataException("Catalog icons require a mods/ Texture2D address.");

                checked
                {
                    encodedPayloadBytes +=
                        sizeof(int) + sizeof(int) +
                        sizeof(ushort) + keyBytes +
                        sizeof(ushort) + nameBytes +
                        sizeof(ushort) + descriptionBytes +
                        sizeof(ushort) + (hasIcon ? entry.IconAddress.Length : 0);
                }
                if (encodedPayloadBytes > MaxCatalogPayloadBytes)
                    throw new InvalidDataException(
                        "Content catalog exceeded its declared authenticated payload ceiling.");
            }

            string previousPack = null;
            long transferBytes = 0;
            var hashes = new HashSet<string>(StringComparer.Ordinal);
            foreach (CatalogPack pack in catalog.Packs)
            {
                if (pack == null || !AssetPackIndexCodec.IsPackId(pack.PackId) ||
                    (previousPack != null && StringComparer.Ordinal.Compare(previousPack, pack.PackId) >= 0) ||
                    pack.IndexHash == null || pack.IndexHash.Length != 32 || pack.BundleHash == null || pack.BundleHash.Length != 32 ||
                    pack.IndexSize == 0 || pack.IndexSize > AssetPackIndexCodec.MaxIndexBytes ||
                    pack.BundleSize == 0 || pack.BundleSize > FileManifestMessage.MaxFileBytes ||
                    !hashes.Add(BitConverter.ToString(pack.IndexHash)) || !hashes.Add(BitConverter.ToString(pack.BundleHash)))
                    throw new InvalidDataException("Catalog contained an invalid, duplicate or noncanonical pack.");
                previousPack = pack.PackId;
                transferBytes += (long)pack.IndexSize + pack.BundleSize;
                encodedPayloadBytes += 2 + pack.PackId.Length + 32 + 4 + 32 + 4;
                if (transferBytes > FileManifestMessage.MaxAggregateBytes || encodedPayloadBytes > MaxCatalogPayloadBytes)
                    throw new InvalidDataException("Catalog exceeded its transfer or encoded metadata budget.");
            }

            if (validateDeclaredHash &&
                !CatalogBuilder.HashesEqual(catalog.CatalogSha256, CatalogBuilder.ComputeCatalogHash(catalog)))
                throw new InvalidDataException("Content catalog SHA-256 did not match its canonical entries.");
        }

        private static int GetUtf8ByteCount(string value, string field)
        {
            try
            {
                return Utf8.GetByteCount(value ?? string.Empty);
            }
            catch (EncoderFallbackException exception)
            {
                throw new InvalidDataException($"Content catalog {field} was not valid UTF-8 text.", exception);
            }
        }
    }
}
