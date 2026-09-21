using System.IO;
using System.Text;

namespace Anomaly.Shared.Networking.Messages
{
    /// <summary>Discloses the opaque, server-local authenticated player identifier.</summary>
    public sealed class EnrollmentIdentityMessage : AuthenticatedServerToClientMessage, IAnomalyMessage
    {
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        public const int MaxEnrollmentIdBytes = 128;
        public override int MaxPayloadBytes => 2 + MaxEnrollmentIdBytes;
        public string MessageName => "anomaly:identity:enrollment";
        public MessageChannel TransportChannel => MessageChannel.ReliableOrdered;

        public string EnrollmentId { get; set; } = string.Empty;

        public void Serialize(BinaryWriter writer)
        {
            byte[] bytes = ValidateAndEncode(EnrollmentId);
            writer.Write((ushort)bytes.Length);
            writer.Write(bytes);
        }

        public void Deserialize(BinaryReader reader)
        {
            int length = reader.ReadUInt16();
            if (length <= 0 || length > MaxEnrollmentIdBytes)
                throw new InvalidDataException("Enrollment id length was invalid.");
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length)
                throw new EndOfStreamException("Enrollment id was truncated.");
            EnrollmentId = Utf8.GetString(bytes);
            ValidateAndEncode(EnrollmentId);
        }

        private static byte[] ValidateAndEncode(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || !value.EndsWith("@anomaly"))
                throw new InvalidDataException("Enrollment id was invalid.");
            byte[] bytes = Utf8.GetBytes(value);
            if (bytes.Length > MaxEnrollmentIdBytes)
                throw new InvalidDataException("Enrollment id was too long.");
            return bytes;
        }
    }
}
