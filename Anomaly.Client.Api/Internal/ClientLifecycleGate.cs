using System.Collections.Generic;

namespace Anomaly.Client.Api.Internal;

/// <summary>
/// Owns Mirror lifecycle transitions for a concrete manager. Native hooks and
/// local-host readiness recovery share this state, while a later reconnect on
/// the same manager starts a new lifecycle.
/// </summary>
internal sealed class ClientLifecycleGate
{
    private enum LifecycleState
    {
        Disconnected,
        Started,
        Connected,
        Ready,
    }

    private long _instanceId;
    private LifecycleState _state;
    private readonly HashSet<long> _retiredInstanceIds = new();

    internal bool TryStart(long instanceId)
    {
        if (!Bind(instanceId, allowNewInstance: true))
            return false;
        if (_state != LifecycleState.Disconnected)
            return false;
        _state = LifecycleState.Started;
        return true;
    }

    internal bool TryConnect(long instanceId)
    {
        if (!Bind(instanceId, allowNewInstance: true))
            return false;
        if (_state != LifecycleState.Started && _state != LifecycleState.Disconnected)
            return false;
        _state = LifecycleState.Connected;
        return true;
    }

    internal bool TryReady(long instanceId)
    {
        if (!Bind(instanceId, allowNewInstance: false))
            return false;
        if (_state != LifecycleState.Started && _state != LifecycleState.Connected)
            return false;
        _state = LifecycleState.Ready;
        return true;
    }

    internal bool TryDisconnect(long instanceId)
    {
        if (!Bind(instanceId, allowNewInstance: false) || _state == LifecycleState.Disconnected)
            return false;
        _state = LifecycleState.Disconnected;
        return true;
    }

    internal void BeginSceneTransition()
    {
        if (_state == LifecycleState.Ready)
            _state = LifecycleState.Connected;
    }

    internal void Reset()
    {
        _instanceId = 0;
        _state = LifecycleState.Disconnected;
        _retiredInstanceIds.Clear();
    }

    private bool Bind(long instanceId, bool allowNewInstance)
    {
        if (instanceId == 0)
            return false;
        if (_instanceId == instanceId)
            return true;
        if (!allowNewInstance || _retiredInstanceIds.Contains(instanceId))
            return false;

        if (_instanceId != 0)
            _retiredInstanceIds.Add(_instanceId);
        _instanceId = instanceId;
        _state = LifecycleState.Disconnected;
        return true;
    }
}
