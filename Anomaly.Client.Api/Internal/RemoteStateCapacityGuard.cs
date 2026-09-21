using System;
using System.Collections.Generic;

namespace Anomaly.Client.Api.Internal;

/// <summary>
/// Bounds authenticated remote state by both total key count and per-scope key count.
/// Existing keys may be refreshed without consuming additional capacity.
/// </summary>
internal sealed class RemoteStateCapacityGuard<TScope, TKey>
{
    private readonly int _maximumTotal;
    private readonly int _maximumPerScope;
    private readonly Dictionary<TScope, HashSet<TKey>> _keysByScope = new();
    private int _count;

    internal RemoteStateCapacityGuard(int maximumTotal, int maximumPerScope)
    {
        if (maximumTotal <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumTotal));
        if (maximumPerScope <= 0 || maximumPerScope > maximumTotal)
            throw new ArgumentOutOfRangeException(nameof(maximumPerScope));

        _maximumTotal = maximumTotal;
        _maximumPerScope = maximumPerScope;
    }

    internal int Count => _count;

    internal bool TryAdd(TScope scope, TKey key)
    {
        if (_keysByScope.TryGetValue(scope, out HashSet<TKey> existing))
        {
            if (existing.Contains(key))
                return true;
            if (existing.Count >= _maximumPerScope || _count >= _maximumTotal)
                return false;

            existing.Add(key);
            _count++;
            return true;
        }

        if (_count >= _maximumTotal)
            return false;

        var created = new HashSet<TKey> { key };
        _keysByScope.Add(scope, created);
        _count++;
        return true;
    }

    internal bool Remove(TScope scope, TKey key)
    {
        if (!_keysByScope.TryGetValue(scope, out HashSet<TKey> keys) || !keys.Remove(key))
            return false;

        _count--;
        if (keys.Count == 0)
            _keysByScope.Remove(scope);
        return true;
    }

    internal int RemoveScope(TScope scope)
    {
        if (!_keysByScope.TryGetValue(scope, out HashSet<TKey> keys))
            return 0;

        int removed = keys.Count;
        _keysByScope.Remove(scope);
        _count -= removed;
        return removed;
    }

    internal void Clear()
    {
        _keysByScope.Clear();
        _count = 0;
    }
}
