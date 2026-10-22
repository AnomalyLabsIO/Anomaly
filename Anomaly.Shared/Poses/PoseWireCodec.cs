using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Anomaly.Shared.Poses;

/// <summary>Bounded pose encoding: positions in millimeters (centimeters for large model coordinates) and signed 16-bit quaternion components.</summary>
public static class PoseWireCodec
{
    /// <summary>Minimum renderer/combat capability minor version supporting the wide-position flag.</summary>
    public const ushort CapabilityMinor = 1;
    private const float NarrowPositionLimit = 32.767f;
    public const int SampleChunkHeaderBytes = 48;
    public const int MessageHeaderBytes = 6;
    public static int GetBoneSize(PoseBone bone) => 3 + ((bone.Components & PoseComponents.Position) != 0 ? 6 : 0) +
        ((bone.Components & PoseComponents.Rotation) != 0 ? 8 : 0);
    public static int GetSampleChunkSize(PoseSampleChunk chunk)
    {
        int result = SampleChunkHeaderBytes;
        foreach (var bone in chunk.Bones) result = checked(result + GetBoneSize(bone));
        return result;
    }

    public static void WriteBinding(BinaryWriter writer, PoseBinding binding)
    {
        binding?.Validate();
        if (binding == null) throw new InvalidDataException("Missing pose binding.");
        writer.Write(binding.NetId); writer.Write(binding.ModelGeneration); writer.Write(binding.BindingRevision);
        writer.Write(binding.RoleId); WriteId(writer, binding.RigId); writer.Write(binding.SchemaVersion);
    }

    public static PoseBinding ReadBinding(BinaryReader reader)
    {
        var binding = new PoseBinding { NetId = reader.ReadUInt32(), ModelGeneration = reader.ReadUInt64(),
            BindingRevision = reader.ReadUInt64(), RoleId = reader.ReadSByte(), RigId = ReadId(reader), SchemaVersion = reader.ReadUInt16() };
        binding.Validate();
        return binding;
    }

    public static void WriteBones(BinaryWriter writer, IReadOnlyList<PoseBone> bones)
    {
        if (bones == null || bones.Count > PoseFrame.MaxBones) throw new InvalidDataException("Invalid pose bone count.");
        writer.Write((ushort)bones.Count);
        var seen = new HashSet<ushort>();
        foreach (var bone in bones)
        {
            if (!seen.Add(bone.BoneId)) throw new InvalidDataException("Duplicate pose bone.");
            // Validate default structs before writing.
            _ = new PoseBone(bone.BoneId, bone.Components, bone.X, bone.Y, bone.Z, bone.Qx, bone.Qy, bone.Qz, bone.Qw, bone.Weight, bone.IncludeLocalPlayer);
            bool wide = (bone.Components & PoseComponents.Position) != 0 &&
                (Math.Abs(bone.X) > NarrowPositionLimit || Math.Abs(bone.Y) > NarrowPositionLimit || Math.Abs(bone.Z) > NarrowPositionLimit);
            writer.Write((byte)bone.BoneId);
            writer.Write((byte)((byte)bone.Components | (bone.IncludeLocalPlayer ? 4 : 0) | (wide ? 8 : 0)));
            writer.Write((byte)Math.Round(bone.Weight * 255));
            if ((bone.Components & PoseComponents.Position) != 0)
            {
                double units = wide ? 100d : 1000d;
                writer.Write(checked((short)Math.Round(bone.X * units))); writer.Write(checked((short)Math.Round(bone.Y * units))); writer.Write(checked((short)Math.Round(bone.Z * units)));
            }
            if ((bone.Components & PoseComponents.Rotation) != 0)
            {
                writer.Write(QuantizeQuaternion(bone.Qx)); writer.Write(QuantizeQuaternion(bone.Qy));
                writer.Write(QuantizeQuaternion(bone.Qz)); writer.Write(QuantizeQuaternion(bone.Qw));
            }
        }
    }

    public static List<PoseBone> ReadBones(BinaryReader reader)
    {
        int count = reader.ReadUInt16();
        if (count > PoseFrame.MaxBones) throw new InvalidDataException("Pose bone count exceeded.");
        var bones = new List<PoseBone>(count);
        var seen = new HashSet<ushort>();
        for (int i = 0; i < count; i++)
        {
            ushort id = reader.ReadByte(); byte flags = reader.ReadByte();
            if ((flags & ~15) != 0 || (flags & 8) != 0 && (flags & 1) == 0 || !seen.Add(id)) throw new InvalidDataException("Invalid/duplicate pose bone.");
            var components = (PoseComponents)(flags & 3);
            float weight = reader.ReadByte() / 255f;
            float x = 0, y = 0, z = 0, qx = 0, qy = 0, qz = 0, qw = 1;
            if ((components & PoseComponents.Position) != 0)
            {
                float units = (flags & 8) != 0 ? 100f : 1000f;
                x = reader.ReadInt16() / units; y = reader.ReadInt16() / units; z = reader.ReadInt16() / units;
            }
            if ((components & PoseComponents.Rotation) != 0)
            { qx = reader.ReadInt16() / 32767f; qy = reader.ReadInt16() / 32767f; qz = reader.ReadInt16() / 32767f; qw = reader.ReadInt16() / 32767f; }
            bones.Add(new PoseBone(id, components, x, y, z, qx, qy, qz, qw, weight, (flags & 4) != 0));
        }
        return bones;
    }

    public static void WriteChunk(BinaryWriter writer, PoseSampleChunk chunk)
    {
        ValidateChunk(chunk);
        writer.Write(chunk.NetId); writer.Write(chunk.ModelGeneration); writer.Write(chunk.BindingRevision);
        writer.Write(chunk.OverrideGeneration); writer.Write(chunk.Sequence); writer.Write(chunk.ServerTime);
        writer.Write(chunk.ChunkIndex); writer.Write(chunk.ChunkCount); WriteBones(writer, chunk.Bones);
    }
    public static PoseSampleChunk ReadChunk(BinaryReader reader)
    {
        var chunk = new PoseSampleChunk { NetId = reader.ReadUInt32(), ModelGeneration = reader.ReadUInt64(),
            BindingRevision = reader.ReadUInt64(), OverrideGeneration = reader.ReadUInt64(), Sequence = reader.ReadUInt64(),
            ServerTime = reader.ReadDouble(), ChunkIndex = reader.ReadByte(), ChunkCount = reader.ReadByte() };
        chunk.Bones.AddRange(ReadBones(reader)); ValidateChunk(chunk); return chunk;
    }
    public static void ValidateChunk(PoseSampleChunk chunk)
    {
        if (chunk == null || chunk.NetId == 0 || chunk.ModelGeneration == 0 || chunk.BindingRevision == 0 ||
            chunk.OverrideGeneration == 0 || chunk.Sequence == 0 || !ValidTime(chunk.ServerTime) ||
            chunk.ChunkCount == 0 || chunk.ChunkCount > PoseSampleChunk.MaxChunks || chunk.ChunkIndex >= chunk.ChunkCount ||
            chunk.Bones.Count > PoseFrame.MaxBones || chunk.Bones.Count == 0 && chunk.ChunkCount != 1)
            throw new InvalidDataException("Invalid pose sample identity, timestamp, or chunk bounds.");
    }
    public static bool ValidTime(double time) => !double.IsNaN(time) && !double.IsInfinity(time) && time >= 0;
    private static short QuantizeQuaternion(float value) => (short)Math.Round(Math.Max(-1f, Math.Min(1f, value)) * 32767f);
    private static void WriteId(BinaryWriter writer, string id)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(id);
        writer.Write((byte)bytes.Length); writer.Write(bytes);
    }
    private static string ReadId(BinaryReader reader)
    {
        int count = reader.ReadByte();
        if (count == 0 || count > PoseFrame.MaxRigIdLength) throw new InvalidDataException("Invalid pose rig identifier length.");
        byte[] bytes = reader.ReadBytes(count);
        if (bytes.Length != count) throw new EndOfStreamException();
        return Encoding.ASCII.GetString(bytes);
    }
}
