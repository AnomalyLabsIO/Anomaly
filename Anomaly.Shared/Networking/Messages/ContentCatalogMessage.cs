using System.Collections.Generic;
using System.IO;
using Anomaly.Shared.Content;

namespace Anomaly.Shared.Networking.Messages
{
    /// <summary>
    /// Server → client. Carries the frozen custom-content catalog (id map + presentation refs +
    /// integrity hash). Sent on <c>ClientReady</c> and replayed to late joiners. ReliableOrdered
    /// so a snapshot of back-to-back entries arrives intact and in order.
    /// </summary>
    public class ContentCatalogMessage : IAnomalyMessage
    {
        public string MessageName => "anomaly:content:catalog";
        public MessageChannel TransportChannel => MessageChannel.ReliableOrdered;

        public ContentCatalog Catalog { get; set; } = new ContentCatalog();

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(Catalog.CatalogHash);
            writer.Write((ushort)Catalog.Entries.Count);
            foreach (var entry in Catalog.Entries)
                entry.WriteTo(writer);
        }

        public void Deserialize(BinaryReader reader)
        {
            var catalog = new ContentCatalog { CatalogHash = reader.ReadUInt32() };
            int count = reader.ReadUInt16();
            catalog.Entries = new List<CatalogEntry>(count);
            for (int i = 0; i < count; i++)
                catalog.Entries.Add(CatalogEntry.ReadFrom(reader));
            Catalog = catalog;
        }
    }
}
