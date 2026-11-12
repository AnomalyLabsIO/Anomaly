using System;
using System.Collections.Generic;
using System.IO;

namespace Anomaly.Shared.Networking.Messages
{
    public class FileRequestMessage : AuthenticatedClientToServerMessage, IAnomalyMessage
    {
        public const int MaxRequestedHashes = FileManifestMessage.MaxFiles;
        public override int MaxPayloadBytes => 8 + 1 + 8 + 4 + 2 + 2 + (MaxRequestedHashes * 32);

        public string MessageName => "anomaly:filerequest";
        // Consent decisions and their resulting requests must be observed in order.
        public MessageChannel TransportChannel => MessageChannel.ReliableOrdered;

        public ulong SessionId { get; set; }
        public byte BulkProfileVersion { get; set; } = BulkTransferProtocol.ProfileVersion;
        public ulong BulkLaneMask { get; set; }
        public uint ReceiveWindowBytes { get; set; } = BulkTransferProtocol.MaxWindowBytes;
        public ushort ReceiveWindowFrames { get; set; } = BulkTransferProtocol.MaxWindowFrames;
        public List<byte[]> RequestedHashes { get; set; } = new List<byte[]>();

        public void Serialize(BinaryWriter writer)
        {
            if (SessionId == 0)
                throw new InvalidDataException("File-transfer session id cannot be zero.");
            if (RequestedHashes == null || RequestedHashes.Count > MaxRequestedHashes)
                throw new InvalidDataException($"Requested hash count exceeded {MaxRequestedHashes}.");
            ValidateBulkProfile(RequestedHashes.Count);

            writer.Write(SessionId);
            writer.Write(BulkProfileVersion);
            writer.Write(BulkLaneMask);
            writer.Write(ReceiveWindowBytes);
            writer.Write(ReceiveWindowFrames);
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

            BulkProfileVersion = reader.ReadByte();
            BulkLaneMask = reader.ReadUInt64();
            ReceiveWindowBytes = reader.ReadUInt32();
            ReceiveWindowFrames = reader.ReadUInt16();

            int count = reader.ReadUInt16();
            if (count > MaxRequestedHashes)
                throw new InvalidDataException($"Requested hash count exceeded {MaxRequestedHashes}.");
            ValidateBulkProfile(count);
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

        private void ValidateBulkProfile(int requestedCount)
        {
            if (BulkProfileVersion != BulkTransferProtocol.ProfileVersion ||
                !BulkTransferProtocol.IsValidMask(BulkLaneMask, allowEmpty: requestedCount == 0) ||
                ReceiveWindowBytes > BulkTransferProtocol.MaxWindowBytes ||
                ReceiveWindowFrames > BulkTransferProtocol.MaxWindowFrames ||
                (requestedCount != 0 && (ReceiveWindowBytes < FileChunkMessage.MaxChunkBytes || ReceiveWindowFrames == 0)))
                throw new InvalidDataException("File request bulk profile, physical lanes or receive window is invalid.");
        }
    }
}
