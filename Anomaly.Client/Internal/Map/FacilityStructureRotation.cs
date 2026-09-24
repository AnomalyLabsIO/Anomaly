using System.Collections.Generic;
using Il2CppMapGeneration;
using Il2CppMapGeneration.Distributors;

namespace Anomaly.Client.Internal.Map;

/// <summary>Tracks only structures owned by rooms materialized from the frozen plan.</summary>
internal static class FacilityStructureRotation
{
    private static readonly HashSet<int> Pending = new();

    internal static void Register(SpawnableRoom room)
    {
        foreach (var identity in room.OriginalIdentities)
        {
            var structure = identity.Target.GetComponent<StructurePositionSync>();
            if (structure != null) Pending.Add(structure.GetInstanceID());
        }
    }

    internal static bool Take(StructurePositionSync structure) => Pending.Remove(structure.GetInstanceID());

    internal static void Clear() => Pending.Clear();
}
