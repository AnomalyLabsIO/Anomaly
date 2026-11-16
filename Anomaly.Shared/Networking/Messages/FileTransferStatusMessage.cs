using System.IO;

namespace Anomaly.Shared.Networking.Messages;

public enum FileTransferStatus : byte { Queued = 1, Admitted = 2 }

/// <summary>One bounded admission decision for the exact consented file request.</summary>
public sealed class FileTransferStatusMessage : AuthenticatedServerToClientMessage, IAnomalyMessage
{
    public const ushort MaxQueueWaitMilliseconds = 60000;
    public override int MaxPayloadBytes => 8 + 1 + 2 + 1;
    public string MessageName => "anomaly:filestatus";
    public MessageChannel TransportChannel => MessageChannel.ReliableOrdered;
    public ulong SessionId { get; set; }
    public FileTransferStatus Status { get; set; }
    public ushort QueueWaitMilliseconds { get; set; }
    public byte QueuePosition { get; set; }

    public void Serialize(BinaryWriter writer)
    {
        Validate();
        writer.Write(SessionId);
        writer.Write((byte)Status);
        writer.Write(QueueWaitMilliseconds);
        writer.Write(QueuePosition);
    }

    public void Deserialize(BinaryReader reader)
    {
        SessionId = reader.ReadUInt64();
        Status = (FileTransferStatus)reader.ReadByte();
        QueueWaitMilliseconds = reader.ReadUInt16();
        QueuePosition = reader.ReadByte();
        Validate();
    }

    private void Validate()
    {
        if (SessionId == 0 ||
            Status == FileTransferStatus.Queued && (QueueWaitMilliseconds == 0 ||
                QueueWaitMilliseconds > MaxQueueWaitMilliseconds || QueuePosition == 0 || QueuePosition > 32) ||
            Status == FileTransferStatus.Admitted && (QueueWaitMilliseconds != 0 || QueuePosition != 0) ||
            Status != FileTransferStatus.Queued && Status != FileTransferStatus.Admitted)
            throw new InvalidDataException("File transfer admission status is invalid.");
    }
}
