using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

/// <summary>Wraps a <see cref="FacilityCamera"/> reference for CCTV lifecycle / state events.</summary>
[ScriptType]
public class FacilityCameraEventArgs : EventArgs
{
    public FacilityCameraEventArgs(FacilityCamera camera) { Camera = camera; }
    [ScriptVisible]
    public FacilityCamera Camera { get; }
}
