using System.Collections.Generic;
using System.IO;
using Anomaly.Shared.Content;

namespace Anomaly.Shared.Networking.Messages
{
    /// <summary>
    /// Client → server. Confirms the client recomputed the same catalog hash and reports which
    /// entries (by key) it could NOT register (missing asset / build failure). The server gates
    /// custom-content grants on this: a custom id is grantable to a hub only when the hub's
    /// <see cref="CatalogHash"/> matches the server's and the entry's key is not in
    /// <see cref="FailedKeys"/>.
    /// </summary>
    public class ContentCatalogAckMessage : IAnomalyMessage
    {
        public string MessageName => "anomaly:content:catalogack";
        public MessageChannel TransportChannel => MessageChannel.ReliableOrdered;

        public uint CatalogHash { get; set; }
        public List<string> FailedKeys { get; set; } = new List<string>();

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(CatalogHash);
            writer.Write((ushort)FailedKeys.Count);
            foreach (var key in FailedKeys)
                CatalogIo.WriteStr(writer, key);
        }

        public void Deserialize(BinaryReader reader)
        {
            CatalogHash = reader.ReadUInt32();
            int count = reader.ReadUInt16();
            FailedKeys = new List<string>(count);
            for (int i = 0; i < count; i++)
                FailedKeys.Add(CatalogIo.ReadStr(reader));
        }
    }
}
