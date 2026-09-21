using Anomaly.Shared.Networking.Messages;

namespace Anomaly.Client.Api.Internal;

/// <summary>
/// Deduplicates authoritative round snapshots within one connection while
/// allowing the same phase to be published again after a reconnect.
/// </summary>
internal sealed class RoundStateConnectionGate
{
    private bool _hasSnapshot;
    private RoundPhase _currentPhase = RoundPhase.WaitingForPlayers;

    internal RoundPhase CurrentPhase => _currentPhase;

    internal bool TryAccept(RoundPhase phase, out RoundPhase previousPhase)
    {
        previousPhase = _currentPhase;
        if (_hasSnapshot && phase == _currentPhase)
            return false;

        _currentPhase = phase;
        _hasSnapshot = true;
        return true;
    }

    internal void Reset()
    {
        _hasSnapshot = false;
        _currentPhase = RoundPhase.WaitingForPlayers;
    }
}
