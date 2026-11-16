using System.IO;

namespace Anomaly.Shared.Networking.Messages;

/// <summary>Cumulative unique chunk bytes/frames consumed by the client's disk worker.</summary>
public class FileCreditMessage : AuthenticatedClientToServerMessage, IAnomalyMessage
{
    public override int MaxPayloadBytes => 8 + 4 + 8 + (BulkTransferProtocol.MaxDomains * 8);
    public string MessageName => "anomaly:filecredit";
    public MessageChannel TransportChannel => MessageChannel.ReliableOrdered;
    public ulong SessionId { get; set; }
    public uint CreditSequence { get; set; }
    public ulong ReleasedBytes { get; set; }
    public uint[] ReleasedFrames { get; set; } = new uint[BulkTransferProtocol.MaxDomains];
    public uint[] ConsumedCounters { get; set; } = new uint[BulkTransferProtocol.MaxDomains];

    public void Serialize(BinaryWriter writer)
    {
        Validate();
        writer.Write(SessionId);
        writer.Write(CreditSequence);
        writer.Write(ReleasedBytes);
        foreach (uint frames in ReleasedFrames) writer.Write(frames);
        foreach (uint counter in ConsumedCounters) writer.Write(counter);
    }

    public void Deserialize(BinaryReader reader)
    {
        SessionId = reader.ReadUInt64();
        CreditSequence = reader.ReadUInt32();
        ReleasedBytes = reader.ReadUInt64();
        ReleasedFrames = new uint[BulkTransferProtocol.MaxDomains];
        for (int index = 0; index < ReleasedFrames.Length; index++) ReleasedFrames[index] = reader.ReadUInt32();
        ConsumedCounters = new uint[BulkTransferProtocol.MaxDomains];
        for (int index = 0; index < ConsumedCounters.Length; index++) ConsumedCounters[index] = reader.ReadUInt32();
        Validate();
    }

    private void Validate()
    {
        if (SessionId == 0 || CreditSequence == 0 || ReleasedFrames == null ||
            ReleasedFrames.Length != BulkTransferProtocol.MaxDomains || ConsumedCounters == null ||
            ConsumedCounters.Length != BulkTransferProtocol.MaxDomains)
            throw new InvalidDataException("File credit session, sequence or frame totals are invalid.");
        foreach (uint counter in ConsumedCounters)
            if (counter == uint.MaxValue) throw new InvalidDataException("Bulk consumed counters cannot use the reserved maximum.");
        // Three complete attempts are the most this manifest can authorize.
        const ulong maximum = (ulong)FileManifestMessage.MaxAggregateBytes * (BulkTransferProtocol.MaxRetries + 1);
        const ulong maximumFrames = ((ulong)FileManifestMessage.MaxAggregateBytes / FileChunkMessage.MaxChunkBytes +
            FileManifestMessage.MaxFiles) * (BulkTransferProtocol.MaxRetries + 1);
        ulong frames = 0;
        foreach (uint count in ReleasedFrames) frames += count;
        if (ReleasedBytes > maximum || frames > maximumFrames || ReleasedBytes < frames ||
            ReleasedBytes > frames * FileChunkMessage.MaxChunkBytes)
            throw new InvalidDataException("File credit totals exceed the bounded transfer inventory.");
    }
}
