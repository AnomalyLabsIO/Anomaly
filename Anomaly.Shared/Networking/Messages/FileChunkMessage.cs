using System.IO;

namespace Anomaly.Shared.Networking.Messages
{
    public class FileChunkMessage : AuthenticatedServerToClientMessage, IAnomalyMessage
    {
        public const int MaxChunkBytes = 16 * 1024;
        public const int MaxChunksPerFile = 4096;
        public const int PrefixBytes = 3;
        public override int MaxPayloadBytes => PrefixBytes + 8 + 32 + 2 + 2 + 2 + MaxChunkBytes;

        public string MessageName => "anomaly:filechunk";
        public MessageChannel TransportChannel => MessageChannel.ReliableUnordered;

        public byte ProfileVersion { get; set; } = BulkTransferProtocol.ProfileVersion;
        public byte Domain { get; set; }
        public byte Attempt { get; set; }
        public ulong SessionId { get; set; }
        public byte[] Hash { get; set; }
        public ushort ChunkIndex { get; set; }
        public ushort TotalChunks { get; set; }
        public byte[] Data { get; set; }
        // Supplied by the authenticated envelope boundary, never by deserialization.
        internal uint FrameCounter { get; set; }

        public void Serialize(BinaryWriter writer)
        {
            ValidatePrefix();
            if (SessionId == 0)
                throw new InvalidDataException("File-transfer session id cannot be zero.");
            if (Hash == null || Hash.Length != 32)
                throw new InvalidDataException("Chunk hash must be 32 bytes.");
            if (TotalChunks == 0 || TotalChunks > MaxChunksPerFile || ChunkIndex >= TotalChunks)
                throw new InvalidDataException("Chunk index/count is invalid.");
            if (Data == null || Data.Length == 0 || Data.Length > MaxChunkBytes)
                throw new InvalidDataException($"Chunk data must be 1-{MaxChunkBytes} bytes.");

            writer.Write(ProfileVersion);
            writer.Write(Domain);
            writer.Write(Attempt);
            writer.Write(SessionId);
            writer.Write(Hash);
            writer.Write(ChunkIndex);
            writer.Write(TotalChunks);
            writer.Write((ushort)Data.Length);
            writer.Write(Data);
        }

        public void Deserialize(BinaryReader reader)
        {
            ProfileVersion = reader.ReadByte();
            Domain = reader.ReadByte();
            Attempt = reader.ReadByte();
            ValidatePrefix();
            SessionId = reader.ReadUInt64();
            if (SessionId == 0)
                throw new InvalidDataException("File-transfer session id cannot be zero.");

            Hash = reader.ReadBytes(32);
            if (Hash.Length != 32)
                throw new EndOfStreamException("Chunk hash was truncated.");
            ChunkIndex = reader.ReadUInt16();
            TotalChunks = reader.ReadUInt16();
            if (TotalChunks == 0 || TotalChunks > MaxChunksPerFile || ChunkIndex >= TotalChunks)
                throw new InvalidDataException("Chunk index/count is invalid.");
            ushort dataLen = reader.ReadUInt16();
            if (dataLen == 0 || dataLen > MaxChunkBytes)
                throw new InvalidDataException($"Chunk data must be 1-{MaxChunkBytes} bytes.");
            Data = reader.ReadBytes(dataLen);
            if (Data.Length != dataLen)
                throw new EndOfStreamException("Chunk data was truncated.");
        }

        private void ValidatePrefix()
        {
            if (ProfileVersion != BulkTransferProtocol.ProfileVersion || Domain >= BulkTransferProtocol.MaxDomains ||
                Attempt > BulkTransferProtocol.MaxRetries)
                throw new InvalidDataException("File chunk bulk profile, domain or attempt is invalid.");
        }
    }
}
