using System;
using System.Collections.Generic;
using Anomaly.Server.Api.Map;

namespace Anomaly.Server.Api.Events.Arguments;

/// <summary>The complete selected facility layout before its final placements are frozen.</summary>
public sealed class FacilityLayoutFinalizingEventArgs : EventArgs
{
    internal FacilityLayoutFinalizingEventArgs(ulong generationId, int seed,
        IReadOnlyList<FacilityRoomPlacement> rooms)
    {
        if (rooms == null) throw new ArgumentNullException(nameof(rooms));
        var copy = new List<FacilityRoomPlacement>(rooms.Count);
        foreach (var room in rooms)
            copy.Add(room ?? throw new ArgumentException("A facility layout cannot contain a null room.", nameof(rooms)));
        GenerationId = generationId;
        Seed = seed;
        Rooms = copy.AsReadOnly();
    }

    /// <summary>The generation identifier of the snapshot being authored.</summary>
    public ulong GenerationId { get; }
    /// <summary>The seed used to select the facility layout.</summary>
    public int Seed { get; }
    /// <summary>Selected rooms in generation order, including rooms removed by earlier handlers.</summary>
    /// <remarks>The collection is fixed; edit its descriptors synchronously during the event.</remarks>
    public IReadOnlyList<FacilityRoomPlacement> Rooms { get; }
}
