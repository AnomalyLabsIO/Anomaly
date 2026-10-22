using Il2CppAdminToys;
using UnityEngine;

namespace Anomaly.Client.Api.Features;

/// <summary>Read-only wrapper for client-observed Scp079CameraToy state.</summary>
public class CameraAdminToy : AdminToy
{
    internal CameraAdminToy(Scp079CameraToy toy) : base(toy) => CameraBase = toy;

    public Scp079CameraToy CameraBase { get; }
    public string Label => CameraBase.Label;
    public Vector2 VerticalConstraint => CameraBase.VerticalConstraint;
    public Vector2 HorizontalConstraint => CameraBase.HorizontalConstraint;
    public Vector2 ZoomConstraint => CameraBase.ZoomConstraint;
    public FacilityCamera Camera => FacilityCamera.Get(CameraBase.Camera);
}
