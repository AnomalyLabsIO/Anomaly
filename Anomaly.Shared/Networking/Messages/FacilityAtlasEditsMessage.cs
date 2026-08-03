using System.Collections.Generic;
using System.IO;

namespace Anomaly.Shared.Networking.Messages
{
    /// <summary>
    /// Server → client. Pixel-level edits applied to the atlas textures that
    /// <c>MapGeneration.AtlasZoneGenerator</c> scans to decide which rooms to
    /// place. Because both server and client clone the atlas and apply these
    /// edits before <c>MapAtlasInterpreter.Interpret</c> runs, the resulting
    /// facility is identical on both sides. Sent immediately after
    /// <c>FacilityLayoutRecipeMessage</c> so the receiver has the full
    /// pre-generation state in place before the vanilla <c>SeedMessage</c>
    /// triggers the client's deterministic generator.
    /// </summary>
    public class FacilityAtlasEditsMessage : AuthenticatedServerToClientMessage, IAnomalyMessage
    {
        public const int MaxEdits = 4096;
        public override int MaxPayloadBytes => 2 + (MaxEdits * 9);
        public string MessageName => "anomaly:facility:atlasedits";
        public MessageChannel TransportChannel => MessageChannel.ReliableOrdered;

        public List<AtlasPixelEdit> Edits { get; set; } = new List<AtlasPixelEdit>();

        public void Serialize(BinaryWriter writer)
        {
            int count = Edits == null ? 0 : Edits.Count;
            if (count > MaxEdits)
                throw new InvalidDataException($"Atlas edit count exceeded {MaxEdits}.");
            writer.Write((ushort)count);
            for (int i = 0; i < count; i++)
            {
                var e = Edits[i];
                writer.Write(e.ZoneId);
                writer.Write(e.AtlasIdx);
                writer.Write(e.PixelX);
                writer.Write(e.PixelY);
                writer.Write(e.R);
                writer.Write(e.G);
                writer.Write(e.B);
            }
        }

        public void Deserialize(BinaryReader reader)
        {
            int count = reader.ReadUInt16();
            if (count > MaxEdits)
                throw new InvalidDataException($"Atlas edit count exceeded {MaxEdits}.");
            Edits = new List<AtlasPixelEdit>(count);
            for (int i = 0; i < count; i++)
            {
                Edits.Add(new AtlasPixelEdit
                {
                    ZoneId   = reader.ReadByte(),
                    AtlasIdx = reader.ReadByte(),
                    PixelX   = reader.ReadUInt16(),
                    PixelY   = reader.ReadUInt16(),
                    R        = reader.ReadByte(),
                    G        = reader.ReadByte(),
                    B        = reader.ReadByte()
                });
            }
        }
    }

    /// <summary>
    /// One pixel edit targeting a specific <c>(zone, atlasIndex, pixelX, pixelY)</c>
    /// in the atlas array of the matching <c>AtlasZoneGenerator</c>. Alpha is
    /// implicitly 255 — the game's pixel matcher only cares about RGB.
    /// </summary>
    public struct AtlasPixelEdit
    {
        public byte   ZoneId;
        public byte   AtlasIdx;
        public ushort PixelX;
        public ushort PixelY;
        public byte   R;
        public byte   G;
        public byte   B;
    }
}
