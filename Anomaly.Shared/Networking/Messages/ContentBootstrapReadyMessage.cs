using System.IO;
using Anomaly.Shared.Content;

namespace Anomaly.Shared.Networking.Messages;

/// <summary>Server confirmation of the exact connection-owned content initialization.</summary>
public sealed class ContentBootstrapReadyMessage : AuthenticatedServerToClientMessage, IAnomalyMessage
{
    public override int MaxPayloadBytes => 8 + ContentCatalog.Sha256Bytes * 2;
    public string MessageName => "anomaly:content:bootstrapready";
    public MessageChannel TransportChannel => MessageChannel.ReliableOrdered;
    public ulong SessionId { get; set; }
    public byte[] CatalogSha256 { get; set; } = new byte[ContentCatalog.Sha256Bytes];
    public byte[] ManifestSha256 { get; set; } = new byte[ContentCatalog.Sha256Bytes];

    public void Serialize(BinaryWriter writer)
    {
        if (SessionId == 0) throw new InvalidDataException("Content bootstrap session is missing.");
        writer.Write(SessionId);
        CatalogIo.WriteRequiredHash(writer, CatalogSha256);
        CatalogIo.WriteRequiredHash(writer, ManifestSha256);
    }

    public void Deserialize(BinaryReader reader)
    {
        SessionId = reader.ReadUInt64();
        CatalogSha256 = CatalogIo.ReadRequiredHash(reader);
        ManifestSha256 = CatalogIo.ReadRequiredHash(reader);
        if (SessionId == 0) throw new InvalidDataException("Content bootstrap session is missing.");
    }
}
