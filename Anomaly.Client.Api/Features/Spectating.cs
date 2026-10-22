using Il2CppPlayerRoles;
using Il2CppPlayerRoles.Spectating;

namespace Anomaly.Client.Api.Features;

/// <summary>Read-only access to the local client's spectator camera target.</summary>
public static class Spectating
{
    /// <summary>Whether the local spectator or overwatch camera tracker is active.</summary>
    public static bool IsActive
    {
        get
        {
            LocalPlayer local = LocalPlayer.Instance;
            return local != null && !local.IsDestroyed &&
                (local.RoleType == RoleTypeId.Spectator || local.RoleType == RoleTypeId.Overwatch) &&
                SpectatorTargetTracker.TrackerSet;
        }
    }

    /// <summary>The current player target, or null when there is no active player target.</summary>
    public static Player CurrentPlayer => TryGetCurrentPlayer(out Player player) ? player : null;

    /// <summary>The current native camera target, or null while the tracker is inactive.</summary>
    public static SpectatableModuleBase CurrentTarget => IsActive ? SpectatorTargetTracker.CurrentTarget : null;

    /// <summary>
    /// Resolves the local target only. Other players' spectator selections are
    /// not inferred from server-side spectator fields.
    /// </summary>
    public static bool TryGetCurrentPlayer(out Player player)
    {
        player = null;
        if (!IsActive || !SpectatorTargetTracker.TryGetTrackedPlayer(out var hub) || hub == null)
            return false;

        player = Player.Get(hub);
        if (player != null && !player.IsDestroyed)
            return true;

        player = null;
        return false;
    }
}
