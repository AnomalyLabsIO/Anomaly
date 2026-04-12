using System.IO;

namespace Anomaly.Server.Networking.Messages;

public class FileTransferCompleteMessage : IAnomalyMessage
{
    public string MessageName => "anomaly:filecomplete";
    public MessageChannel TransportChannel => MessageChannel.ReliableUnordered;

    public byte[] Hash { get; set; }

    public void Serialize(BinaryWriter writer) => writer.Write(Hash);
    public void Deserialize(BinaryReader reader) => Hash = reader.ReadBytes(32);
}
