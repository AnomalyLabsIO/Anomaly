using System.Text;
using Anomaly.Client.API.Enum;

namespace Anomaly.Client.API.Networking.Messages;

public class FileManifestMessage : IAnomalyMessage
{
    public string MessageName => "anomaly:filemanifest";
    public MessageChannel TransportChannel => MessageChannel.ReliableUnordered;

    public List<FileEntry> Files { get; set; } = new();

    public void Serialize(BinaryWriter writer)
    {
        writer.Write((ushort)Files.Count);
        foreach (var file in Files)
        {
            var pathBytes = Encoding.UTF8.GetBytes(file.Path);
            writer.Write((byte)pathBytes.Length);
            writer.Write(pathBytes);
            writer.Write(file.Hash);
            writer.Write(file.Size);
        }
    }

    public void Deserialize(BinaryReader reader)
    {
        int count = reader.ReadUInt16();
        Files = new List<FileEntry>(count);
        for (int i = 0; i < count; i++)
        {
            int pathLen = reader.ReadByte();
            string path = Encoding.UTF8.GetString(reader.ReadBytes(pathLen));
            byte[] hash = reader.ReadBytes(32);
            uint size = reader.ReadUInt32();
            Files.Add(new FileEntry(path, hash, size));
        }
    }

    public readonly record struct FileEntry(string Path, byte[] Hash, uint Size);
}
