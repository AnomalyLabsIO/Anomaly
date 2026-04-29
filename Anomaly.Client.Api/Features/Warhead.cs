using Il2Cpp;

namespace Anomaly.Client.Api.Features;

/// <summary>
/// Read-only client-side facade over <see cref="AlphaWarheadController"/>.
/// All state is replicated from the server, so values are accurate for any
/// connected client (not just admins). Mods that want to render countdown
/// HUDs, low-time screen tints, or custom siren replacements should hang off
/// these properties.
/// </summary>
/// <remarks>
/// LabAPI's server-side <c>Warhead</c> wrapper exposes <c>Start</c> /
/// <c>Stop</c> / <c>Detonate</c> for admin control. Those are intentionally
/// absent here — they're server-only operations and the client API is
/// read-only by design. Use the events on <see cref="AlphaWarheadController"/>
/// (or future <c>WarheadEvents</c>) to react to state transitions.
/// </remarks>
public static class Warhead
{
    /// <summary>The underlying <see cref="AlphaWarheadController"/> singleton, or null if not yet initialized.</summary>
    public static AlphaWarheadController Base => AlphaWarheadController.SingletonSet ? AlphaWarheadController.Singleton : null;

    /// <summary>True if the warhead has already detonated this round.</summary>
    public static bool AlreadyDetonated => Base != null && Base.AlreadyDetonated;

    /// <summary>True if the warhead countdown is currently running.</summary>
    public static bool IsInProgress => AlphaWarheadController.InProgress;

    /// <summary>True after the warhead has detonated.</summary>
    public static bool Detonated => AlphaWarheadController.Detonated;

    /// <summary>True if the warhead is locked (cannot be cancelled / stopped).</summary>
    public static bool IsLocked => Base != null && Base.IsLocked;

    /// <summary>
    /// Seconds remaining until detonation, or <see cref="float.NaN"/> if no
    /// countdown is active. Updated continuously while <see cref="IsInProgress"/>.
    /// </summary>
    public static float TimeUntilDetonation => IsInProgress ? AlphaWarheadController.TimeUntilDetonation : float.NaN;

    /// <summary>Number of player kills attributed to this warhead detonation, or 0 if it hasn't detonated.</summary>
    public static int Kills => Base?.WarheadKills ?? 0;

    /// <summary>The <see cref="ReferenceHub"/> that triggered the warhead, if any.</summary>
    public static ReferenceHub TriggeredBy => AlphaWarheadController.WarheadTriggeredby;

    /// <summary>The triggering player as a wrapper, or null if unknown / triggered by the server.</summary>
    public static Player TriggeredByPlayer
    {
        get
        {
            ReferenceHub hub = TriggeredBy;
            return hub != null ? Player.Get(hub) : null;
        }
    }
}
