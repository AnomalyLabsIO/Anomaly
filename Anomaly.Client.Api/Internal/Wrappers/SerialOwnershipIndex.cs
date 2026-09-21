using System;
using System.Collections.Generic;

namespace Anomaly.Client.Api.Internal.Wrappers;

/// <summary>
/// Owns one live wrapper per non-zero game serial. Reassignment returns the
/// displaced wrapper and expected-owner removal cannot evict a newer owner.
/// </summary>
internal sealed class SerialOwnershipIndex<T> where T : class
{
    private readonly Dictionary<ushort, T> _bySerial = new();

    internal bool TryGet(ushort serial, out T value)
    {
        if (serial == 0)
        {
            value = null;
            return false;
        }

        return _bySerial.TryGetValue(serial, out value);
    }

    internal T Assign(ushort serial, T value)
    {
        if (serial == 0 || value == null)
            return null;

        _bySerial.TryGetValue(serial, out T previous);
        _bySerial[serial] = value;
        return previous != null && !ReferenceEquals(previous, value) ? previous : null;
    }

    internal bool RemoveIfOwned(ushort serial, T expectedOwner)
    {
        if (serial == 0 || expectedOwner == null ||
            !_bySerial.TryGetValue(serial, out T current) ||
            !ReferenceEquals(current, expectedOwner))
            return false;

        return _bySerial.Remove(serial);
    }

    internal void RemoveWhere(Func<T, bool> predicate)
    {
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));

        var stale = new List<ushort>();
        foreach (KeyValuePair<ushort, T> pair in _bySerial)
        {
            if (predicate(pair.Value))
                stale.Add(pair.Key);
        }

        foreach (ushort serial in stale)
            _bySerial.Remove(serial);
    }

    internal void Clear() => _bySerial.Clear();
}
