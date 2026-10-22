using System;
using Anomaly.Server.Poses;
using Anomaly.Shared.Poses;
using LabApi.Features.Wrappers;

namespace Anomaly.Server.Api.Features;

/// <summary>Owns pose handles for one plugin. Use and dispose on the game thread.</summary>
public sealed class PlayerPoseScope : IDisposable
{
    private readonly PlayerPoseService _service;
    private readonly long _identity;
    private bool _disposed;
    internal PlayerPoseScope(PlayerPoseService service, long identity, string ownerId) { _service = service; _identity = identity; OwnerId = ownerId; }
    /// <summary>The diagnostic owner namespace supplied when creating this scope.</summary>
    public string OwnerId { get; }

    /// <summary>Creates a contribution bound to the player's current model lifetime.</summary>
    public bool TryCreate(Player player, PoseFrame initialFrame, PlayerPoseOptions options,
        out PlayerPoseOverride handle, out PoseFailure failure)
    {
        _service.RequireGameThread();
        handle = null;
        if (_disposed) { failure = PoseFailure.Disposed; return false; }
        return _service.TryCreate(_identity, player, initialFrame, options, out handle, out failure);
    }

    /// <summary>Releases only this scope's poses and permanently invalidates its handles.</summary>
    public void Dispose()
    {
        _service.RequireGameThread();
        if (_disposed) return;
        _disposed = true;
        _service.RemoveOwner(_identity);
    }
}
