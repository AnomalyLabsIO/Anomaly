using System.Collections.Generic;
using System.IO;
using System.Text;
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
    public class ContentCatalogAckMessage : AuthenticatedClientToServerMessage, IAnomalyMessage
    {
        public const int MaxFailedKeys = ContentCatalogMessage.MaxEntries;
        public const int MaxFailedKeyBytes = ContentCatalogMessage.MaxKeyBytes;
        public override int MaxPayloadBytes => 64 * 1024;
        public string MessageName => "anomaly:content:catalogack";
        public MessageChannel TransportChannel => MessageChannel.ReliableOrdered;

        public uint CatalogHash { get; set; }
        public List<string> FailedKeys { get; set; } = new List<string>();

        public void Serialize(BinaryWriter writer)
        {
            if (FailedKeys == null || FailedKeys.Count > MaxFailedKeys)
                throw new InvalidDataException($"Failed-key count exceeded {MaxFailedKeys}.");

            writer.Write(CatalogHash);
            writer.Write((ushort)FailedKeys.Count);
            var seen = new HashSet<string>();
            foreach (var key in FailedKeys)
            {
                if (!ModNamespace.IsValidContentKey(key) || !seen.Add(key))
                    throw new InvalidDataException("Failed-key list contained an invalid or duplicate key.");
                var bytes = Encoding.UTF8.GetBytes(key);
                if (bytes.Length > MaxFailedKeyBytes)
                    throw new InvalidDataException($"Failed key exceeded {MaxFailedKeyBytes} bytes.");
                writer.Write((ushort)bytes.Length);
                writer.Write(bytes);
            }
        }

        public void Deserialize(BinaryReader reader)
        {
            CatalogHash = reader.ReadUInt32();
            int count = reader.ReadUInt16();
            if (count > MaxFailedKeys)
                throw new InvalidDataException($"Failed-key count exceeded {MaxFailedKeys}.");
            FailedKeys = new List<string>(count);
            var seen = new HashSet<string>();
            for (int i = 0; i < count; i++)
            {
                int length = reader.ReadUInt16();
                if (length == 0 || length > MaxFailedKeyBytes)
                    throw new InvalidDataException($"Failed key exceeded {MaxFailedKeyBytes} bytes.");
                byte[] bytes = reader.ReadBytes(length);
                if (bytes.Length != length)
                    throw new EndOfStreamException("Failed key was truncated.");
                string key = Encoding.UTF8.GetString(bytes);
                if (!ModNamespace.IsValidContentKey(key) || !seen.Add(key))
                    throw new InvalidDataException("Failed-key list contained an invalid or duplicate key.");
                FailedKeys.Add(key);
            }
        }
    }
}
