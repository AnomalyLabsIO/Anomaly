using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Anomaly.Shared.Content;

namespace Anomaly.Shared.Networking.Messages
{
    /// <summary>Authenticated server offer for one immutable items-only catalog.</summary>
    public class ContentCatalogMessage : AuthenticatedServerToClientMessage, IAnomalyMessage
    {
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        private const int FixedPayloadBytes = 1 + ContentCatalog.Sha256Bytes + ContentCatalog.Sha256Bytes + 2;

        public const int MaxEntries = 127;
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
            ValidateCatalog(catalog, validateDeclaredHash: true);
            Catalog = catalog;
        }

        public static void ValidateCatalog(ContentCatalog catalog, bool validateDeclaredHash)
        {
            if (catalog == null || catalog.Entries == null)
                throw new InvalidDataException("Content catalog was null.");
            if (catalog.SchemaVersion != ContentCatalog.CurrentSchemaVersion)
                throw new InvalidDataException($"Unsupported content catalog schema {catalog.SchemaVersion}.");
            if (catalog.CatalogSha256 == null || catalog.CatalogSha256.Length != ContentCatalog.Sha256Bytes ||
                catalog.ManifestSha256 == null || catalog.ManifestSha256.Length != ContentCatalog.Sha256Bytes)
                throw new InvalidDataException("Content catalog hashes must be 32-byte SHA-256 values.");
            if (catalog.Entries.Count > MaxEntries)
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
                bool hasIcon = entry.IconHash != null && entry.IconHash.Length != 0;
                if (hasIcon != (entry.IconSize != 0) ||
                    (hasIcon && (entry.IconHash.Length != ContentCatalog.Sha256Bytes ||
                                 entry.IconSize > FileManifestMessage.MaxFileBytes)))
                    throw new InvalidDataException("Content catalog icon hash and size were inconsistent.");

                checked
                {
                    encodedPayloadBytes +=
                        sizeof(int) + sizeof(int) +
                        sizeof(ushort) + keyBytes +
                        sizeof(ushort) + nameBytes +
                        sizeof(ushort) + descriptionBytes +
                        sizeof(byte) + (hasIcon ? ContentCatalog.Sha256Bytes : 0) +
                        sizeof(uint);
                }
                if (encodedPayloadBytes > MaxCatalogPayloadBytes)
                    throw new InvalidDataException(
                        "Content catalog exceeded its declared authenticated payload ceiling.");
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
