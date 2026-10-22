using System;
using Anomaly.Server.Poses;
using Anomaly.Shared.Poses;

namespace Anomaly.Server.Api.Features;

/// <summary>One owner's contribution to one player/model lifetime. Clear and timeout permanently close it.</summary>
public sealed class PlayerPoseOverride : IDisposable
{
    private readonly PlayerPoseService _service;
    private readonly long _owner;
    private readonly ulong _id;
    private bool _disposed;
    internal PlayerPoseOverride(PlayerPoseService service, long owner, ulong id) { _service = service; _owner = owner; _id = id; }
    /// <summary>Whether this handle still addresses its original live model and scope.</summary>
    public bool IsValid => !_disposed && _service.IsValid(_owner, _id);

    /// <summary>Replaces the complete authored bone mask and refreshes its inactivity deadline.</summary>
    public bool TryUpdate(PoseFrame frame, out PoseFailure failure)
    {
        _service.RequireGameThread();
        if (_disposed) { failure = PoseFailure.Disposed; return false; }
        return _service.TryUpdate(_owner, _id, frame, out failure);
    }

    /// <summary>Returns a detached copy of this owner's authored pose, before priority composition.</summary>
    public bool TryGetState(out PoseFrame frame)
    {
        _service.RequireGameThread();
        frame = null;
        return !_disposed && _service.TryGetState(_owner, _id, out frame);
    }

    /// <summary>Restarts presentation interpolation after a tracking recenter without moving the player root.</summary>
    public void ResetInterpolation()
    {
        _service.RequireGameThread();
        if (_disposed) throw new ObjectDisposedException(nameof(PlayerPoseOverride));
        _service.ResetInterpolation(_owner, _id);
    }

    /// <summary>Reliably releases this contribution. Create a new handle to publish again.</summary>
    public void Clear() => Dispose();
    public void Dispose()
    {
        _service.RequireGameThread();
        if (_disposed) return;
        _disposed = true;
        _service.Remove(_owner, _id);
    }
}
