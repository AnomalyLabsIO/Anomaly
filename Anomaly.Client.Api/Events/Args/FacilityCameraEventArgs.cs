using Anomaly.Client.Api.Features;

namespace Anomaly.Client.Api.Events.Args;

/// <summary>Wraps a <see cref="FacilityCamera"/> reference for CCTV lifecycle / state events.</summary>
public class FacilityCameraEventArgs : EventArgs
{
    public FacilityCameraEventArgs(FacilityCamera camera) { Camera = camera; }
    public FacilityCamera Camera { get; }
}
