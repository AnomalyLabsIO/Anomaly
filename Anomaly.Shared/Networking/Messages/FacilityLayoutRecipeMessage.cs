using System.Collections.Generic;
using System.IO;

namespace Anomaly.Shared.Networking.Messages
{
    /// <summary>
    /// Server → client. Carries the list of pre-generation facility mutations that
    /// both sides apply while running the deterministic map generator. Sent once
    /// per connecting client immediately before the vanilla <c>SeedMessage</c>
    /// so both messages ride the same reliable channel and arrive in order.
    /// </summary>
    /// <remarks>
    /// Entries are identified by <see cref="Anomaly.Shared.Networking.Messages.FacilityRecipeEntry.ZoneId"/>
    /// (matches the game's <c>MapGeneration.FacilityZone</c> enum) plus the
    /// 2-D atlas coordinate that <c>AtlasZoneGenerator</c> assigns during
    /// interpretation. This message intentionally has no Unity or game
    /// references so it compiles inside <c>Anomaly.Shared</c>.
    /// </remarks>
    public class FacilityLayoutRecipeMessage : IAnomalyMessage
    {
        public string MessageName => "anomaly:facility:recipe";
        public MessageChannel TransportChannel => MessageChannel.ReliableOrdered;

        public const byte OpTransformOverride = 0;
        public const byte OpSkip              = 1;

        public const byte DirtyPosition = 1 << 0;
        public const byte DirtyRotation = 1 << 1;

        public List<FacilityRecipeEntry> Entries { get; set; } = new List<FacilityRecipeEntry>();

        public void Serialize(BinaryWriter writer)
        {
            int count = Entries == null ? 0 : Entries.Count;
            writer.Write((ushort)count);
            for (int i = 0; i < count; i++)
            {
                var e = Entries[i];
                writer.Write(e.ZoneId);
                writer.Write(e.CoordsX);
                writer.Write(e.CoordsY);
                writer.Write(e.OpType);

                if (e.OpType == OpTransformOverride)
                {
                    writer.Write(e.DirtyMask);
                    if ((e.DirtyMask & DirtyPosition) != 0)
                    {
                        writer.Write(e.OffsetX);
                        writer.Write(e.OffsetY);
                        writer.Write(e.OffsetZ);
                    }
                    if ((e.DirtyMask & DirtyRotation) != 0)
                    {
                        writer.Write(e.RotationY);
                    }
                }
            }
        }

        public void Deserialize(BinaryReader reader)
        {
            int count = reader.ReadUInt16();
            Entries = new List<FacilityRecipeEntry>(count);
            for (int i = 0; i < count; i++)
            {
                var e = new FacilityRecipeEntry
                {
                    ZoneId  = reader.ReadByte(),
                    CoordsX = reader.ReadInt16(),
                    CoordsY = reader.ReadInt16(),
                    OpType  = reader.ReadByte()
                };

                if (e.OpType == OpTransformOverride)
                {
                    e.DirtyMask = reader.ReadByte();
                    if ((e.DirtyMask & DirtyPosition) != 0)
                    {
                        e.OffsetX = reader.ReadSingle();
                        e.OffsetY = reader.ReadSingle();
                        e.OffsetZ = reader.ReadSingle();
                    }
                    if ((e.DirtyMask & DirtyRotation) != 0)
                    {
                        e.RotationY = reader.ReadSingle();
                    }
                }

                Entries.Add(e);
            }
        }
    }

    /// <summary>
    /// Plain wire record describing one pre-generation mutation for a single
    /// atlas tile in a single facility zone. Lives in Shared so both Server
    /// and Client can reference the same struct without pulling in Unity.
    /// </summary>
    public struct FacilityRecipeEntry
    {
        public byte  ZoneId;
        public short CoordsX;
        public short CoordsY;
        public byte  OpType;
        public byte  DirtyMask;
        public float OffsetX;
        public float OffsetY;
        public float OffsetZ;
        public float RotationY;
    }
}
