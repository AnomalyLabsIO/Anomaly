using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Shared.Networking.Messages;

namespace Anomaly.Client.Api.Features;

/// <summary>
/// Read-only facade over the current round's lifecycle state. Mirrors LabAPI's
/// server-side <c>Round</c> shape (<c>IsStarted</c>, <c>IsEnded</c>, etc.) but
/// is driven by <see cref="RoundEvents.CurrentPhase"/>, which is fed from the
/// server's <see cref="RoundStateMessage"/> broadcasts.
/// </summary>
/// <remarks>
/// The phase is reset to <see cref="RoundPhase.WaitingForPlayers"/> until the
/// first server message arrives. After that, every transition flows through
/// <see cref="RoundEvents.PhaseChanged"/>; mods that need to react should
/// subscribe to those events rather than poll <see cref="CurrentPhase"/>.
/// </remarks>
public static class Round
{
    /// <summary>
    /// The most recently received phase from the server.
    /// Equivalent to <see cref="RoundEvents.CurrentPhase"/>; both reflect the
    /// same backing field.
    /// </summary>
    public static RoundPhase CurrentPhase => RoundEvents.CurrentPhase;

    /// <summary>
    /// True while the round is actively running (between <see cref="RoundPhase.Starting"/>
    /// and <see cref="RoundPhase.Ending"/>, inclusive of <see cref="RoundPhase.InProgress"/>).
    /// </summary>
    public static bool IsStarted
    {
        get
        {
            RoundPhase p = CurrentPhase;
            return p == RoundPhase.Starting || p == RoundPhase.InProgress || p == RoundPhase.Ending;
        }
    }

    /// <summary>
    /// True specifically during the gameplay phase, after <see cref="RoundPhase.Starting"/>
    /// completes and before <see cref="RoundPhase.Ending"/> begins.
    /// </summary>
    public static bool IsInProgress => CurrentPhase == RoundPhase.InProgress;

    /// <summary>
    /// True from <see cref="RoundPhase.Ending"/> onward, until the next round
    /// restart broadcasts <see cref="RoundPhase.WaitingForPlayers"/>.
    /// </summary>
    public static bool IsEnded
    {
        get
        {
            RoundPhase p = CurrentPhase;
            return p == RoundPhase.Ending || p == RoundPhase.Ended;
        }
    }

    /// <summary>
    /// True while the server is waiting for enough players to start a round
    /// (the lobby phase before <see cref="RoundPhase.Starting"/>).
    /// </summary>
    public static bool IsLobby => CurrentPhase == RoundPhase.WaitingForPlayers;

    /// <summary>
    /// True only on the single-tick <see cref="RoundPhase.Restarted"/> phase
    /// fired when the server has finished tearing down a finished round and
    /// is preparing the next lobby. Most mods should subscribe to
    /// <see cref="RoundEvents.Restarted"/> rather than polling this.
    /// </summary>
    public static bool IsRestarting => CurrentPhase == RoundPhase.Restarted;
}
