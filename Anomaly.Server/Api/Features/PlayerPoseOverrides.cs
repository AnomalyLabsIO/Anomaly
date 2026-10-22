using Anomaly.Server.Poses;

namespace Anomaly.Server.Api.Features;

/// <summary>Server-authoritative cosmetic bone posing for compatible Anomaly clients.</summary>
public static class PlayerPoseOverrides
{
    /// <summary>Creates plugin ownership. Dispose this scope from the owning LabAPI plugin's Disable method.</summary>
    public static PlayerPoseScope CreateScope(string ownerId) => PlayerPoseRuntime.CreateScope(ownerId);
}
