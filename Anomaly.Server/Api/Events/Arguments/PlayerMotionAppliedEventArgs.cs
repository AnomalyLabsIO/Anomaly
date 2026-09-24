using Anomaly.Server.Api.Features;
using LabApi.Features.Wrappers;

namespace Anomaly.Server.Api.Events.Arguments;

/// <summary>An authoritative movement discontinuity that was successfully applied.</summary>
public sealed class PlayerMotionAppliedEventArgs : ClientEventArgs
{
    internal PlayerMotionAppliedEventArgs(Player player, string ownerId, MotionDiscontinuity motion) : base(player)
    {
        OwnerId = ownerId;
        Motion = motion;
    }
    /// <summary>The explicit owner of this movement handoff.</summary>
    public string OwnerId { get; }
    /// <summary>The applied position, orientation, momentum and sequence.</summary>
    public MotionDiscontinuity Motion { get; }
}
