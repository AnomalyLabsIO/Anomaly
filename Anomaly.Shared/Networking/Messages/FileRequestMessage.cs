using System;
using System.Collections.Generic;
using System.IO;

namespace Anomaly.Shared.Networking.Messages
{
    public class FileRequestMessage : AuthenticatedClientToServerMessage, IAnomalyMessage
    {
        public const int MaxRequestedHashes = FileManifestMessage.MaxFiles;
        public override int MaxPayloadBytes => 8 + 2 + (MaxRequestedHashes * 32);

        public string MessageName => "anomaly:filerequest";
        // Consent decisions and their resulting requests must be observed in order.
        public MessageChannel TransportChannel => MessageChannel.ReliableOrdered;

        public ulong SessionId { get; set; }
        public List<byte[]> RequestedHashes { get; set; } = new List<byte[]>();

        public void Serialize(BinaryWriter writer)
        {
            if (SessionId == 0)
                throw new InvalidDataException("File-transfer session id cannot be zero.");
            if (RequestedHashes == null || RequestedHashes.Count > MaxRequestedHashes)
                throw new InvalidDataException($"Requested hash count exceeded {MaxRequestedHashes}.");

            writer.Write(SessionId);
            writer.Write((ushort)RequestedHashes.Count);
            var hashes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var hash in RequestedHashes)
            {
                if (hash == null || hash.Length != 32)
                    throw new InvalidDataException("Requested hashes must be 32 bytes.");
                if (!hashes.Add(BitConverter.ToString(hash)))
                    throw new InvalidDataException("File request contained a duplicate hash.");
                writer.Write(hash);
            }
        }

        public void Deserialize(BinaryReader reader)
        {
            SessionId = reader.ReadUInt64();
            if (SessionId == 0)
                throw new InvalidDataException("File-transfer session id cannot be zero.");

            int count = reader.ReadUInt16();
            if (count > MaxRequestedHashes)
                throw new InvalidDataException($"Requested hash count exceeded {MaxRequestedHashes}.");
            RequestedHashes = new List<byte[]>(count);
            var hashes = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < count; i++)
            {
                byte[] hash = reader.ReadBytes(32);
                if (hash.Length != 32)
                    throw new EndOfStreamException("Requested hash was truncated.");
                if (!hashes.Add(BitConverter.ToString(hash)))
                    throw new InvalidDataException("File request contained a duplicate hash.");
                RequestedHashes.Add(hash);
            }
        }
    }
}
