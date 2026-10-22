using System;
using Anomaly.Shared.Poses;

namespace Anomaly.Server.Api.Features;

/// <summary>A model-bound, independently revocable server authorization for native client combat queries.</summary>
public sealed class PlayerCombatPoseOverride : IDisposable
{
    private readonly PlayerPoseOverride _handle;
    internal PlayerCombatPoseOverride(PlayerPoseOverride handle) => _handle = handle;
    /// <summary>Whether the owner, model and inactivity lease are still current.</summary>
    public bool IsValid => _handle.IsValid;
    /// <summary>Replaces the whole contribution and refreshes its inactivity deadline.</summary>
    public bool TryUpdate(PoseFrame frame, out PoseFailure failure) => _handle.TryUpdate(frame, out failure);
    /// <summary>Returns a detached copy of this owner's authored frame.</summary>
    public bool TryGetState(out PoseFrame frame) => _handle.TryGetState(out frame);
    /// <summary>Clears interpolation after a tracking recenter without moving the gameplay root.</summary>
    public void ResetInterpolation() => _handle.ResetInterpolation();
    /// <summary>Permanently revokes this handle. Create a new handle to resume.</summary>
    public void Clear() => _handle.Clear();
    /// <summary>Reliably revokes this contribution on the game thread.</summary>
    public void Dispose() => _handle.Dispose();
}
