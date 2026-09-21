using System.IO;
using Anomaly.Shared.Content;

namespace Anomaly.Shared.Networking.Messages
{
    public enum ContentCatalogDecision : byte
    {
        Declined = 0,
        Approved = 1,
    }

    /// <summary>User decision for one authenticated catalog and manifest tuple.</summary>
    public sealed class ContentCatalogDecisionMessage : AuthenticatedClientToServerMessage, IAnomalyMessage
    {
        public override int MaxPayloadBytes => (ContentCatalog.Sha256Bytes * 2) + 1;
        public string MessageName => "anomaly:content:decision";
        public MessageChannel TransportChannel => MessageChannel.ReliableOrdered;

        public byte[] CatalogSha256 { get; set; } = new byte[ContentCatalog.Sha256Bytes];
        public byte[] ManifestSha256 { get; set; } = new byte[ContentCatalog.Sha256Bytes];
        public ContentCatalogDecision Decision { get; set; }

        public void Serialize(BinaryWriter writer)
        {
            Validate();
            CatalogIo.WriteRequiredHash(writer, CatalogSha256);
            CatalogIo.WriteRequiredHash(writer, ManifestSha256);
            writer.Write((byte)Decision);
        }

        public void Deserialize(BinaryReader reader)
        {
            CatalogSha256 = CatalogIo.ReadRequiredHash(reader);
            ManifestSha256 = CatalogIo.ReadRequiredHash(reader);
            Decision = (ContentCatalogDecision)reader.ReadByte();
            Validate();
        }

        private void Validate()
        {
            if (CatalogSha256 == null || CatalogSha256.Length != ContentCatalog.Sha256Bytes ||
                ManifestSha256 == null || ManifestSha256.Length != ContentCatalog.Sha256Bytes)
                throw new InvalidDataException("Content decision hashes must be 32-byte SHA-256 values.");
            if (Decision != ContentCatalogDecision.Declined && Decision != ContentCatalogDecision.Approved)
                throw new InvalidDataException("Content decision was invalid.");
        }
    }
}
