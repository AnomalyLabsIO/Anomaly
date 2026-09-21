using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Anomaly.Shared.Content;

namespace Anomaly.Shared.Networking.Messages
{
    public enum ContentCatalogActivationStatus : byte
    {
        Failed = 0,
        Activated = 1,
    }

    /// <summary>Client confirmation that the complete offered catalog was committed or failed.</summary>
    public class ContentCatalogAckMessage : AuthenticatedClientToServerMessage, IAnomalyMessage
    {
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        public const int MaxFailedKeys = ContentCatalogMessage.MaxEntries;
        public const int MaxFailedKeyBytes = ContentCatalogMessage.MaxKeyBytes;
        public override int MaxPayloadBytes => 64 * 1024;
        public string MessageName => "anomaly:content:catalogack";
        public MessageChannel TransportChannel => MessageChannel.ReliableOrdered;

        public byte[] CatalogSha256 { get; set; } = new byte[ContentCatalog.Sha256Bytes];
        public byte[] ManifestSha256 { get; set; } = new byte[ContentCatalog.Sha256Bytes];
        public ContentCatalogActivationStatus Status { get; set; }
        public List<string> FailedKeys { get; set; } = new List<string>();

        public void Serialize(BinaryWriter writer)
        {
            Validate();
            CatalogIo.WriteRequiredHash(writer, CatalogSha256);
            CatalogIo.WriteRequiredHash(writer, ManifestSha256);
            writer.Write((byte)Status);
            writer.Write((ushort)FailedKeys.Count);
            foreach (string key in FailedKeys)
            {
                byte[] bytes = Utf8.GetBytes(key);
                writer.Write((ushort)bytes.Length);
                writer.Write(bytes);
            }
        }

        public void Deserialize(BinaryReader reader)
        {
            CatalogSha256 = CatalogIo.ReadRequiredHash(reader);
            ManifestSha256 = CatalogIo.ReadRequiredHash(reader);
            Status = (ContentCatalogActivationStatus)reader.ReadByte();
            int count = reader.ReadUInt16();
            if (count > MaxFailedKeys)
                throw new InvalidDataException($"Failed-key count exceeded {MaxFailedKeys}.");
            FailedKeys = new List<string>(count);
            for (int index = 0; index < count; index++)
            {
                int length = reader.ReadUInt16();
                if (length <= 0 || length > MaxFailedKeyBytes)
                    throw new InvalidDataException("Failed content key length was invalid.");
                byte[] bytes = reader.ReadBytes(length);
                if (bytes.Length != length)
                    throw new EndOfStreamException("Failed content key was truncated.");
                FailedKeys.Add(Utf8.GetString(bytes));
            }
            Validate();
        }

        private void Validate()
        {
            if (CatalogSha256 == null || CatalogSha256.Length != ContentCatalog.Sha256Bytes ||
                ManifestSha256 == null || ManifestSha256.Length != ContentCatalog.Sha256Bytes)
                throw new InvalidDataException("Content activation hashes must be 32-byte SHA-256 values.");
            if (Status != ContentCatalogActivationStatus.Failed &&
                Status != ContentCatalogActivationStatus.Activated)
                throw new InvalidDataException("Content activation status was invalid.");
            if (FailedKeys == null || FailedKeys.Count > MaxFailedKeys)
                throw new InvalidDataException($"Failed-key count exceeded {MaxFailedKeys}.");
            if (Status == ContentCatalogActivationStatus.Activated && FailedKeys.Count != 0)
                throw new InvalidDataException("An activated catalog cannot contain failed keys.");

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string key in FailedKeys)
            {
                if (!ModNamespace.IsValidContentKey(key) || !seen.Add(key) ||
                    Utf8.GetByteCount(key) > MaxFailedKeyBytes)
                    throw new InvalidDataException("Failed-key list contained an invalid or duplicate key.");
            }
        }
    }
}
