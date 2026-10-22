using System;
using Anomaly.Shared.Poses;
using LabApi.Features.Wrappers;

namespace Anomaly.Server.Api.Features;

/// <summary>Owns combat-authorized poses independently of every cosmetic pose scope.</summary>
public sealed class PlayerCombatPoseScope : IDisposable
{
    private readonly PlayerPoseScope _scope;
    internal PlayerCombatPoseScope(PlayerPoseScope scope) => _scope = scope;
    /// <summary>The diagnostic owner namespace.</summary>
    public string OwnerId => _scope.OwnerId;
    /// <summary>Authorizes a complete pose for this player's current model lifetime, on the game thread.</summary>
    public bool TryCreate(Player player, PoseFrame initialFrame, PlayerPoseOptions options,
        out PlayerCombatPoseOverride handle, out PoseFailure failure)
    {
        handle = null;
        if (!_scope.TryCreate(player, initialFrame, options, out var inner, out failure)) return false;
        handle = new PlayerCombatPoseOverride(inner);
        return true;
    }
    /// <summary>Reliably revokes this owner's authorizations and invalidates its handles.</summary>
    public void Dispose() => _scope.Dispose();
}
