using Anomaly.Client.Api.Internal.Poses;
using Anomaly.Shared.Poses;

namespace Anomaly.Client.Api.Features;

/// <summary>Captures a producer's bone poses using Anomaly's built-in stable rig contracts.</summary>
/// <remarks>Call after your local body solver. Coordinates are relative to the current character model root,
/// including its authored scale. This does not transmit, solve IK, or move the player root.</remarks>
public static class PlayerPoseRigs
{
    /// <summary>Finds the built-in descriptor for the player's currently displayed role.</summary>
    public static bool TryGet(Player player, out PoseRigDescriptor rig)
    {
        rig = null;
        return player != null && !player.IsDestroyed &&
               PoseRigCatalog.TryGetForRole((sbyte)player.RoleType, out rig);
    }

    /// <summary>Captures every mapped bone in the current model, on the Unity game thread.</summary>
    public static bool TryCapture(Player player, out PoseFrame frame) => TryCapture(player, null, out frame);

    /// <summary>Captures the selected stable bone IDs, or the whole available rig when IDs are null.</summary>
    public static bool TryCapture(Player player, IEnumerable<ushort> boneIds, out PoseFrame frame)
    {
        return PoseCaptureCache.TryCapture(player, boneIds, out frame);
    }
}
