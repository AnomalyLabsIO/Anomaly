using System;
using System.Threading;

namespace Anomaly.Server.Api.Features;

/// <summary>Options copied when an owner creates a pose override.</summary>
public sealed class PlayerPoseOptions
{
    /// <summary>Higher priority wins each bone. Equal priorities favor the earlier created override.</summary>
    public int Priority { get; set; }
    /// <summary>Multiplier for each bone's weight against normal animation, between zero and one.</summary>
    public float Weight { get; set; } = 1f;
    /// <summary>Time without an update before releasing the pose. Use Timeout.InfiniteTimeSpan for a persistent pose.</summary>
    public TimeSpan InactivityTimeout { get; set; } = TimeSpan.FromMilliseconds(500);
    /// <summary>Allows the target's own client to render this pose; false by default.</summary>
    public bool IncludeLocalPlayer { get; set; }

    internal PlayerPoseOptions Copy()
    {
        if (float.IsNaN(Weight) || float.IsInfinity(Weight) || Weight < 0f || Weight > 1f)
            throw new ArgumentOutOfRangeException(nameof(Weight));
        if (InactivityTimeout != Timeout.InfiniteTimeSpan &&
            (InactivityTimeout < TimeSpan.FromMilliseconds(50) || InactivityTimeout > TimeSpan.FromHours(1)))
            throw new ArgumentOutOfRangeException(nameof(InactivityTimeout), "Use 50 milliseconds through one hour, or Timeout.InfiniteTimeSpan.");
        return new PlayerPoseOptions { Priority = Priority, Weight = Weight, InactivityTimeout = InactivityTimeout, IncludeLocalPlayer = IncludeLocalPlayer };
    }
}

/// <summary>The reason a pose could not be created or updated.</summary>
public enum PoseFailure : byte
{
    None,
    NoPlayer,
    UnsupportedRig,
    InvalidFrame,
    ModelChanged,
    Disposed,
    LimitExceeded,
    NotInitialized,
}
