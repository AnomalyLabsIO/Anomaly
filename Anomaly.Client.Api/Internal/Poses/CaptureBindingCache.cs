using System;
using System.Collections.Generic;

namespace Anomaly.Client.Api.Internal.Poses;

/// <summary>Bounded incarnation cache; capture bindings own no visual or gameplay mutations.</summary>
internal sealed class CaptureBindingCache<TKey, TBinding> where TBinding : class
{
    private readonly Dictionary<TKey, TBinding> _bindings = new();
    private readonly Func<TBinding, bool> _isCurrent;
    private readonly int _capacity;

    internal CaptureBindingCache(Func<TBinding, bool> isCurrent, int capacity = 128)
    {
        _isCurrent = isCurrent ?? throw new ArgumentNullException(nameof(isCurrent));
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    internal int Count => _bindings.Count;

    internal bool TryGetOrCreate(TKey key, Func<TKey, TBinding> create, out TBinding binding)
    {
        if (_bindings.TryGetValue(key, out binding))
        {
            if (_isCurrent(binding)) return true;
            _bindings.Remove(key);
        }
        binding = create(key);
        if (binding == null) return false;
        if (_bindings.Count >= _capacity) Prune();
        // Rebinding is safe; bounded retention is preferable to retaining retired players.
        if (_bindings.Count >= _capacity) _bindings.Clear();
        _bindings.Add(key, binding);
        return true;
    }

    internal void Remove(TKey key) => _bindings.Remove(key);
    internal void Clear() => _bindings.Clear();

    internal void Prune()
    {
        var retired = new List<TKey>();
        foreach (var pair in _bindings)
            if (!_isCurrent(pair.Value)) retired.Add(pair.Key);
        foreach (var key in retired) _bindings.Remove(key);
    }
}
