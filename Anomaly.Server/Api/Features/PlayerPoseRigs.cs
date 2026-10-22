using Anomaly.Server.Poses;
using Anomaly.Shared.Poses;
using LabApi.Features.Wrappers;

namespace Anomaly.Server.Api.Features;

/// <summary>Built-in versioned bone schemas supported by the base client renderer.</summary>
public static class PlayerPoseRigs
{
    /// <summary>Gets the schema for the player's current native role, or false for a role without a supported model.</summary>
    public static bool TryGet(Player player, out PoseRigDescriptor descriptor)
    {
        PlayerPoseRuntime.RequireGameThread();
        descriptor = null;
        return player?.ReferenceHub?.roleManager?.CurrentRole != null &&
            PoseRigCatalog.TryGetForRole((sbyte)player.ReferenceHub.roleManager.CurrentRole.RoleTypeId, out descriptor);
    }
}
