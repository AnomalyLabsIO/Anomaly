using System.Collections.Generic;
using System.Linq;

namespace Anomaly.Shared.Networking.Messages;

/// <summary>Selects native seed generation or replay of an authoritative placement plan.</summary>
public enum FacilityGenerationMode : byte
{
    Native = 0,
    Planned = 1,
}

/// <summary>A zone's ordered interpretations and resolved room placements, without engine references.</summary>
public sealed class FacilityZonePlan
{
    public byte ZoneId { get; set; }
    public byte[] CatalogDigest { get; set; } = new byte[32];
    public float EntranceOffsetX { get; set; }
    public float EntranceOffsetY { get; set; }
    public float EntranceOffsetZ { get; set; }
    public List<FacilityPlannedRoom> Rooms { get; set; } = new List<FacilityPlannedRoom>();

    public FacilityZonePlan Copy() => new FacilityZonePlan
    {
        ZoneId = ZoneId,
        CatalogDigest = CatalogDigest == null ? null : (byte[])CatalogDigest.Clone(),
        EntranceOffsetX = EntranceOffsetX, EntranceOffsetY = EntranceOffsetY, EntranceOffsetZ = EntranceOffsetZ,
        Rooms = Rooms?.Select(room => room?.Copy()).ToList(),
    };
}

/// <summary>
/// One interpretation in original generation order. Removed placements retain their
/// selected candidate and duplicate ordinal so remaining room identities do not change.
/// </summary>
public sealed class FacilityPlannedRoom
{
    public short CoordsX { get; set; }
    public short CoordsY { get; set; }
    public int Shape { get; set; }
    public float InterpretationRotationY { get; set; }
    public List<int> SpecificRooms { get; set; } = new List<int>();
    public bool HasPlacement { get; set; }
    public int CandidateIndex { get; set; }
    public int Name { get; set; }
    public int DuplicateId { get; set; }
    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float PositionZ { get; set; }
    public float RotationY { get; set; }
    public bool IsRemoved { get; set; }

    public FacilityPlannedRoom Copy() => new FacilityPlannedRoom
    {
        CoordsX = CoordsX, CoordsY = CoordsY, Shape = Shape,
        InterpretationRotationY = InterpretationRotationY,
        SpecificRooms = SpecificRooms == null ? null : new List<int>(SpecificRooms),
        HasPlacement = HasPlacement, CandidateIndex = CandidateIndex, Name = Name, DuplicateId = DuplicateId,
        PositionX = PositionX, PositionY = PositionY, PositionZ = PositionZ,
        RotationY = RotationY, IsRemoved = IsRemoved,
    };
}
