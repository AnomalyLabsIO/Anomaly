using System.Collections.Generic;
using System.IO;

namespace Anomaly.Shared.Networking.Messages
{
    /// <summary>
    /// Encodes the atlas-pixel section of an authenticated facility snapshot.
    /// </summary>
    internal sealed class FacilityAtlasEditsCodec
    {
        public const int MaxEdits = 4096;

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
