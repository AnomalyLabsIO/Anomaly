using Il2CppAdminToys;
using UnityEngine;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Features;

/// <summary>Read-only wrapper for client-observed WaypointToy state.</summary>
[ScriptType]
public class WaypointAdminToy : AdminToy
{
    internal WaypointAdminToy(WaypointToy toy) : base(toy) => WaypointBase = toy;

    public WaypointToy WaypointBase { get; }
    public Vector3 BoundsSize => WaypointBase.BoundsSize;
    public float Priority => WaypointBase.Priority;
    public bool VisualizeBounds => WaypointBase.VisualizeBounds;
}
