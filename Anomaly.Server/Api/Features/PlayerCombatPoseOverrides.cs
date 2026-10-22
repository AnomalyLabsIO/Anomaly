using Anomaly.Server.Poses;
using Anomaly.Shared.Capabilities;
using Anomaly.Shared.Poses;
using LabApi.Features.Wrappers;

namespace Anomaly.Server.Api.Features;

/// <summary>Explicit server authorization for client combat acquisition against posed native hitboxes.</summary>
/// <remarks>Independent of cosmetic poses. Does not move server colliders or grant clients damage authority.</remarks>
public static class PlayerCombatPoseOverrides
{
    /// <summary>Creates an independent owner; dispose in the LabAPI plugin's Disable method on the game thread.</summary>
    public static PlayerCombatPoseScope CreateScope(string ownerId) => new PlayerCombatPoseScope(PlayerPoseRuntime.Combat.CreateScope(ownerId));
    /// <summary>Whether the viewer advertises the installed scoped SCP-049/SCP-173 acquisition bridge.</summary>
    public static bool IsSupported(Player viewer) => ClientCapabilities.Supports(viewer, new CapabilityDescriptor("anomaly:player-combat-pose", 1, PoseWireCodec.CapabilityMinor));
}
