namespace Anomaly.Client.Api.Internal;

/// <summary>Hands transport failures to main-thread teardown without carrying stale attempts or local stops.</summary>
internal sealed class TransportDisconnectGate
{
    private readonly object _sync = new();
    private int _generation = -1;
    private bool _cancelled;
    private long _host;
    private string _reason;

    internal void Begin(int generation)
    {
        lock (_sync)
        {
            _generation = generation;
            _cancelled = false;
            _host = 0;
            _reason = null;
        }
    }

    internal void Observe(int generation, long currentHost, long peerHost, bool networkFailure, string reason)
    {
        lock (_sync)
        {
            if (generation != _generation || _cancelled || currentHost == 0 || peerHost != currentHost)
                return;
            _host = networkFailure ? currentHost : 0;
            _reason = networkFailure ? reason : null;
        }
    }

    internal void Cancel(int generation)
    {
        lock (_sync)
        {
            if (generation != _generation) return;
            _cancelled = true;
            _host = 0;
            _reason = null;
        }
    }

    internal bool TryConsume(int generation, long currentHost, out string reason)
    {
        lock (_sync)
        {
            reason = generation == _generation && !_cancelled && _host != 0 && _host == currentHost ? _reason : null;
            _host = 0;
            _reason = null;
            return !string.IsNullOrEmpty(reason);
        }
    }
}
