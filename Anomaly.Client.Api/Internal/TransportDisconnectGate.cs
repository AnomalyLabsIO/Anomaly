namespace Anomaly.Client.Api.Internal;

/// <summary>Hands detached transport outcomes to main-thread teardown without stale attempts or local stops.</summary>
internal sealed class TransportDisconnectGate
{
    internal enum Kind { NetworkFailure, ServerClosed }
    internal readonly struct Notice
    {
        internal readonly Kind Outcome;
        internal readonly string Reason;
        internal Notice(Kind outcome, string reason) { Outcome = outcome; Reason = reason; }
    }

    private readonly object _sync = new();
    private int _generation = -1;
    private bool _cancelled;
    private long _host;
    private string _reason;
    private Kind _kind;
    private long _consumedHost;

    internal void Begin(int generation)
    {
        lock (_sync)
        {
            _generation = generation;
            _cancelled = false;
            _host = 0;
            _reason = null;
            _consumedHost = 0;
        }
    }

    internal void Observe(int generation, long currentHost, long peerHost, bool networkFailure, string reason)
    {
        lock (_sync)
        {
            if (generation != _generation || _cancelled || currentHost == 0 || peerHost != currentHost)
                return;
            RetireReplacedHost(currentHost);
            if (!networkFailure || string.IsNullOrEmpty(reason) || _reason != null || _consumedHost == currentHost) return;
            _host = currentHost;
            _reason = reason;
            _kind = Kind.NetworkFailure;
        }
    }

    internal void ObserveServerClose(int generation, long currentHost, long peerHost, byte[] additionalData)
    {
        lock (_sync)
        {
            if (generation != _generation || _cancelled || currentHost == 0 || peerHost != currentHost) return;
            RetireReplacedHost(currentHost);
            if (_reason != null || _consumedHost == currentHost ||
                !StockDisconnectReason.TryDecode(additionalData, out string reason)) return;
            _host = currentHost;
            _reason = reason;
            _kind = Kind.ServerClosed;
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
        bool consumed = TryConsumeNotice(generation, currentHost, out var notice);
        reason = consumed ? notice.Reason : null;
        return consumed;
    }

    internal bool TryConsumeNotice(int generation, long currentHost, out Notice notice)
    {
        lock (_sync)
        {
            notice = default;
            // An old queued callback must not drain a newer attempt's notice.
            if (generation != _generation || _host == 0 || _host != currentHost) return false;
            bool consumed = !_cancelled && !string.IsNullOrEmpty(_reason);
            if (consumed)
            {
                notice = new Notice(_kind, _reason);
                _consumedHost = _host;
            }
            _host = 0;
            _reason = null;
            return consumed;
        }
    }

    private void RetireReplacedHost(long currentHost)
    {
        // A validated observation from the live replacement host can retire
        // its predecessor. An unrelated teardown consumer cannot do so.
        if (_host == 0 || _host == currentHost) return;
        _host = 0;
        _reason = null;
    }
}
