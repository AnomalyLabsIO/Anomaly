using System;
using System.Collections.Generic;

namespace Anomaly.Shared.Poses;

/// <summary>Identifies the model actually delivered to one recipient, rather than a process-local role object.</summary>
public sealed class PoseBinding
{
    public uint NetId { get; set; }
    public ulong ModelGeneration { get; set; }
    public ulong BindingRevision { get; set; }
    public sbyte RoleId { get; set; }
    public string RigId { get; set; }
    public ushort SchemaVersion { get; set; }

    public PoseBinding Clone() => (PoseBinding)MemberwiseClone();
    public bool Matches(PoseBinding other) => other != null && NetId == other.NetId && ModelGeneration == other.ModelGeneration &&
        BindingRevision == other.BindingRevision && RoleId == other.RoleId && RigId == other.RigId && SchemaVersion == other.SchemaVersion;
    public void Validate()
    {
        if (NetId == 0 || ModelGeneration == 0 || BindingRevision == 0) throw new ArgumentException("Pose binding identity must be nonzero.");
        _ = new PoseFrame(RigId, SchemaVersion, Array.Empty<PoseBone>());
    }
}

public enum PoseControlOperation : byte { Bind = 1, Activate = 2, Clear = 3, ResetAll = 4 }

/// <summary>Reliable control. Only Activate can introduce a pose generation.</summary>
public sealed class PoseControl
{
    public PoseControlOperation Operation { get; set; }
    public PoseBinding Binding { get; set; }
    public ulong OverrideGeneration { get; set; }
    public ulong Sequence { get; set; }
    public double ServerTime { get; set; }
    public PoseFrame Frame { get; set; }
}

/// <summary>One bounded fragment of an independently decodable complete sample.</summary>
public sealed class PoseSampleChunk
{
    public const int MaxChunks = 16;
    public uint NetId { get; set; }
    public ulong ModelGeneration { get; set; }
    public ulong BindingRevision { get; set; }
    public ulong OverrideGeneration { get; set; }
    public ulong Sequence { get; set; }
    public double ServerTime { get; set; }
    public byte ChunkIndex { get; set; }
    public byte ChunkCount { get; set; }
    public List<PoseBone> Bones { get; } = new List<PoseBone>();
}
