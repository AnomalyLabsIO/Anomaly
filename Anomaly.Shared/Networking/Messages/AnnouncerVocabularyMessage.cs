using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Anomaly.Shared.Networking.Messages
{
    public class AnnouncerVocabularyMessage : AuthenticatedServerToClientMessage, IAnomalyMessage
    {
        public const int MaxWords = 128;
        public const int MaxApiNameBytes = 128;
        public const float MaxDurationSeconds = 30f;
        public override int MaxPayloadBytes => 32 * 1024;
        public string MessageName => "anomaly:announcer:vocabulary";
        public MessageChannel TransportChannel => MessageChannel.ReliableUnordered;

        public List<Entry> Words { get; set; } = new List<Entry>();

        public void Serialize(BinaryWriter writer)
        {
            if (Words == null || Words.Count > MaxWords)
                throw new InvalidDataException($"Vocabulary count exceeded {MaxWords}.");
            writer.Write((ushort)Words.Count);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var word in Words)
            {
                var nameBytes = Encoding.UTF8.GetBytes(word.ApiName ?? "");
                if (nameBytes.Length > MaxApiNameBytes)
                    throw new InvalidDataException($"Vocabulary name exceeded {MaxApiNameBytes} bytes.");
                if (nameBytes.Length == 0 || word.AudioHash == null || word.AudioHash.Length != 32)
                    throw new InvalidDataException("Vocabulary entry name/hash was invalid.");
                if (!names.Add(word.ApiName))
                    throw new InvalidDataException("Vocabulary contained a duplicate API name.");
                if (float.IsNaN(word.DurationSeconds) || float.IsInfinity(word.DurationSeconds) ||
                    word.DurationSeconds <= 0f || word.DurationSeconds > MaxDurationSeconds)
                    throw new InvalidDataException("Vocabulary duration was invalid.");
                writer.Write((ushort)nameBytes.Length);
                writer.Write(nameBytes);
                writer.Write(word.AudioHash);
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
                string apiName = Encoding.UTF8.GetString(nameBytes);
                if (!names.Add(apiName))
                    throw new InvalidDataException("Vocabulary contained a duplicate API name.");
                byte[] audioHash = reader.ReadBytes(32);
                if (audioHash.Length != 32)
                    throw new EndOfStreamException("Vocabulary audio hash was truncated.");
                float durationSeconds = reader.ReadSingle();
                if (float.IsNaN(durationSeconds) || float.IsInfinity(durationSeconds) ||
                    durationSeconds <= 0f || durationSeconds > MaxDurationSeconds)
                    throw new InvalidDataException("Vocabulary duration was invalid.");
                byte type = reader.ReadByte();

                Words.Add(new Entry(apiName, audioHash, durationSeconds, type));
            }
        }

        public struct Entry
        {
            public string ApiName;
            public byte[] AudioHash;
            public float DurationSeconds;
            public byte Type;

            public Entry(string apiName, byte[] audioHash, float durationSeconds, byte type)
            {
                ApiName = apiName;
                AudioHash = audioHash;
                DurationSeconds = durationSeconds;
                Type = type;
            }
        }
    }
}
