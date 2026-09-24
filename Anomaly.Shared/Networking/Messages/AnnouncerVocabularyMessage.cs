using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Anomaly.Shared.Assets;

namespace Anomaly.Shared.Networking.Messages
{
    public class AnnouncerVocabularyMessage : AuthenticatedServerToClientMessage, IAnomalyMessage
    {
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        public const int MaxWords = 128;
        public const int MaxApiNameBytes = 128;
        public const float MaxDurationSeconds = 30f;
        public override int MaxPayloadBytes => 64 * 1024;
        public string MessageName => "anomaly:announcer:vocabulary";
        public MessageChannel TransportChannel => MessageChannel.ReliableUnordered;

        public List<Entry> Words { get; set; } = new List<Entry>();

        /// <summary>New registrations cannot shadow stock names or the stock pause token.</summary>
        public static bool IsValidNewApiName(string apiName)
        {
            if (string.IsNullOrEmpty(apiName) || apiName == ".") return false;
            foreach (char character in apiName)
                if (char.IsWhiteSpace(character) || char.IsControl(character)) return false;
            try { if (Utf8.GetByteCount(apiName) > MaxApiNameBytes) return false; }
            catch (EncoderFallbackException) { return false; }
            return !StockAssetProfile.TryGetCassie("game/cassie/" + apiName.ToLowerInvariant(), out _);
        }

        public void Serialize(BinaryWriter writer)
        {
            if (Words == null || Words.Count > MaxWords)
                throw new InvalidDataException($"Vocabulary count exceeded {MaxWords}.");
            writer.Write((ushort)Words.Count);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var word in Words)
            {
                var nameBytes = Utf8.GetBytes(word.ApiName ?? "");
                if (nameBytes.Length > MaxApiNameBytes)
                    throw new InvalidDataException($"Vocabulary name exceeded {MaxApiNameBytes} bytes.");
                if (!IsValidNewApiName(word.ApiName) || !AssetAddress.TryParse(word.AudioAddress, out AssetAddress address) || address.IsGameReplacement)
                    throw new InvalidDataException("New vocabulary must reference a mods/ audio address.");
                if (!names.Add(word.ApiName))
                    throw new InvalidDataException("Vocabulary contained a duplicate API name.");
                if (float.IsNaN(word.DurationSeconds) || float.IsInfinity(word.DurationSeconds) ||
                    word.DurationSeconds <= 0f || word.DurationSeconds > MaxDurationSeconds)
                    throw new InvalidDataException("Vocabulary duration was invalid.");
                writer.Write((ushort)nameBytes.Length);
                writer.Write(nameBytes);
                AssetAddressWire.Write(writer, word.AudioAddress);
                writer.Write(word.DurationSeconds);
                writer.Write(word.Type);
            }
        }

        public void Deserialize(BinaryReader reader)
        {
            int count = reader.ReadUInt16();
            if (count > MaxWords)
                throw new InvalidDataException($"Vocabulary count exceeded {MaxWords}.");
            Words = new List<Entry>(count);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < count; i++)
            {
                int nameLength = reader.ReadUInt16();
                if (nameLength > MaxApiNameBytes)
                    throw new InvalidDataException($"Vocabulary name exceeded {MaxApiNameBytes} bytes.");
                byte[] nameBytes = reader.ReadBytes(nameLength);
                if (nameBytes.Length != nameLength || nameLength == 0)
                    throw new EndOfStreamException("Vocabulary name was truncated or empty.");
                string apiName = Utf8.GetString(nameBytes);
                if (!IsValidNewApiName(apiName)) throw new InvalidDataException("Vocabulary cannot shadow stock names or contain control/whitespace characters.");
                if (!names.Add(apiName))
                    throw new InvalidDataException("Vocabulary contained a duplicate API name.");
                string audioAddress = AssetAddressWire.Read(reader);
                if (AssetAddress.Parse(audioAddress).IsGameReplacement)
                    throw new InvalidDataException("New vocabulary cannot use a stock replacement address.");
                float durationSeconds = reader.ReadSingle();
                if (float.IsNaN(durationSeconds) || float.IsInfinity(durationSeconds) ||
                    durationSeconds <= 0f || durationSeconds > MaxDurationSeconds)
                    throw new InvalidDataException("Vocabulary duration was invalid.");
                byte type = reader.ReadByte();

                Words.Add(new Entry(apiName, audioAddress, durationSeconds, type));
            }
        }

        public struct Entry
        {
            public string ApiName;
            public string AudioAddress;
            public float DurationSeconds;
            public byte Type;

            public Entry(string apiName, string audioAddress, float durationSeconds, byte type)
            {
                ApiName = apiName;
                AudioAddress = audioAddress;
                DurationSeconds = durationSeconds;
                Type = type;
            }
        }
    }
}
