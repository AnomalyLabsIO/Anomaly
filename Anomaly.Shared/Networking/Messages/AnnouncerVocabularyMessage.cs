using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Anomaly.Shared.Networking.Messages
{
    public class AnnouncerVocabularyMessage : IAnomalyMessage
    {
        public string MessageName => "anomaly:announcer:vocabulary";
        public MessageChannel TransportChannel => MessageChannel.ReliableUnordered;

        public List<Entry> Words { get; set; } = new List<Entry>();

        public void Serialize(BinaryWriter writer)
        {
            writer.Write((ushort)Words.Count);
            foreach (var word in Words)
            {
                var nameBytes = Encoding.UTF8.GetBytes(word.ApiName ?? "");
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
            Words = new List<Entry>(count);
            for (int i = 0; i < count; i++)
            {
                int nameLength = reader.ReadUInt16();
                string apiName = Encoding.UTF8.GetString(reader.ReadBytes(nameLength));
                byte[] audioHash = reader.ReadBytes(32);
                float durationSeconds = reader.ReadSingle();
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
