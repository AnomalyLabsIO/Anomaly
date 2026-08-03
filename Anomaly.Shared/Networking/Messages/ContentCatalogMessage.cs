using System.Collections.Generic;
using System.IO;
using System.Text;
using Anomaly.Shared.Content;

namespace Anomaly.Shared.Networking.Messages
{
    /// <summary>
    /// Server → client. Carries the frozen custom-content catalog (id map + presentation refs +
    /// integrity hash). Sent on <c>ClientReady</c> and replayed to late joiners. ReliableOrdered
    /// so a snapshot of back-to-back entries arrives intact and in order.
    /// </summary>
    public class ContentCatalogMessage : AuthenticatedServerToClientMessage, IAnomalyMessage
    {
        public const int MaxEntries = 254;
        public const int MaxKeyBytes = 128;
        public const int MaxNameBytes = 512;
        public const int MaxDescriptionBytes = 4096;
        public const int MaxAssetNameBytes = 512;
        public override int MaxPayloadBytes => 256 * 1024;
        public string MessageName => "anomaly:content:catalog";
        public MessageChannel TransportChannel => MessageChannel.ReliableOrdered;

        public ContentCatalog Catalog { get; set; } = new ContentCatalog();

        public void Serialize(BinaryWriter writer)
        {
            if (Catalog == null || Catalog.Entries == null || Catalog.Entries.Count > MaxEntries)
                throw new InvalidDataException($"Content catalog count exceeded {MaxEntries}.");

            writer.Write(Catalog.CatalogHash);
            writer.Write((ushort)Catalog.Entries.Count);
            foreach (var entry in Catalog.Entries)
            {
                ValidateEntry(entry);
                entry.WriteTo(writer);
            }
        }

        public void Deserialize(BinaryReader reader)
        {
            var catalog = new ContentCatalog { CatalogHash = reader.ReadUInt32() };
            int count = reader.ReadUInt16();
            if (count > MaxEntries)
                throw new InvalidDataException($"Content catalog count exceeded {MaxEntries}.");
            catalog.Entries = new List<CatalogEntry>(count);
            for (int i = 0; i < count; i++)
            {
                var entry = CatalogEntry.ReadFrom(reader);
                ValidateEntry(entry);
                catalog.Entries.Add(entry);
            }
            if (CatalogBuilder.ComputeCatalogHash(catalog.Entries) != catalog.CatalogHash)
                throw new InvalidDataException("Content catalog hash did not match its entries.");
            Catalog = catalog;
        }

        private static void ValidateEntry(CatalogEntry entry)
        {
            if (entry == null)
                throw new InvalidDataException("Content catalog contained a null entry.");
            if (entry.Kind != CatalogEntryKind.Item)
                throw new InvalidDataException("Content catalog v1 is items-only.");
            if (!ModNamespace.IsValidContentKey(entry.Key) || Utf8Length(entry.Key) > MaxKeyBytes)
                throw new InvalidDataException("Content catalog key was invalid or too long.");
            if (Utf8Length(entry.DisplayName) > MaxNameBytes ||
                Utf8Length(entry.Description) > MaxDescriptionBytes ||
                Utf8Length(entry.ModelAssetName) > MaxAssetNameBytes)
                throw new InvalidDataException("Content catalog text exceeded its field limit.");
            if ((entry.ModelBundleHash != null && entry.ModelBundleHash.Length != 32) ||
                (entry.IconHash != null && entry.IconHash.Length != 32))
                throw new InvalidDataException("Content catalog asset hashes must be 32 bytes.");
        }

        private static int Utf8Length(string value) => Encoding.UTF8.GetByteCount(value ?? string.Empty);
    }
}
