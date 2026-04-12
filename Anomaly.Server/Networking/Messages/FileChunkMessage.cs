using System.IO;

namespace Anomaly.Server.Networking.Messages;

public class FileChunkMessage : IAnomalyMessage
{
    public string MessageName => "anomaly:filechunk";
    public MessageChannel TransportChannel => MessageChannel.ReliableUnordered;

    public byte[] Hash { get; set; }
    public ushort ChunkIndex { get; set; }
    public ushort TotalChunks { get; set; }
    public byte[] Data { get; set; }

    public void Serialize(BinaryWriter writer)
    {
        writer.Write(Hash);
        writer.Write(ChunkIndex);
        writer.Write(TotalChunks);
        writer.Write((ushort)Data.Length);
        writer.Write(Data);
    }

    public void Deserialize(BinaryReader reader)
    {
        Hash = reader.ReadBytes(32);
        ChunkIndex = reader.ReadUInt16();
        TotalChunks = reader.ReadUInt16();
        ushort dataLen = reader.ReadUInt16();
        Data = reader.ReadBytes(dataLen);
    }
}
