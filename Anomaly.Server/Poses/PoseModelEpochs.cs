using System;
using System.Collections.Generic;

namespace Anomaly.Server.Poses;

/// <summary>Shared generation source keyed by native life, never pooled object or role-type equality.</summary>
internal sealed class PoseModelEpochs
{
    private sealed class Life
    {
        internal object Hub;
        internal int NativeId;
        internal ulong Generation;
    }
    private readonly Dictionary<uint, Life> _lifetimes = new Dictionary<uint, Life>();
    private ulong _next;

    internal ulong Observe(uint netId, object hub, int nativeLife)
    {
        if (netId == 0 || hub == null) throw new ArgumentException("A live player identity is required.");
        if (_lifetimes.TryGetValue(netId, out Life current) && ReferenceEquals(hub, current.Hub) && nativeLife == current.NativeId)
            return current.Generation;
        ulong generation = checked(++_next);
        _lifetimes[netId] = new Life { Hub = hub, NativeId = nativeLife, Generation = generation };
        return generation;
    }
    internal void Remove(uint netId) => _lifetimes.Remove(netId);
    internal void Clear() => _lifetimes.Clear();
}
