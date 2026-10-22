using Il2CppAdminToys;
using UnityEngine;

namespace Anomaly.Client.Api.Features;

/// <summary>Read-only wrapper for client-observed WaypointToy state.</summary>
public class WaypointAdminToy : AdminToy
{
    internal WaypointAdminToy(WaypointToy toy) : base(toy) => WaypointBase = toy;

    public WaypointToy WaypointBase { get; }
    public Vector3 BoundsSize => WaypointBase.BoundsSize;
    public float Priority => WaypointBase.Priority;
    public bool VisualizeBounds => WaypointBase.VisualizeBounds;
}
