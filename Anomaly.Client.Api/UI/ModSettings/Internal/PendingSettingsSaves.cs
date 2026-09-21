using System;
using System.Collections.Generic;

namespace Anomaly.Client.Api.UI.ModSettings.Internal;

/// <summary>Keeps failed and reentrant edits dirty until their own save succeeds.</summary>
internal sealed class PendingSettingsSaves<T>
{
    private readonly Dictionary<T, long> _pending = new();
    private long _revision;

    internal int Count => _pending.Count;

    internal void MarkDirty(T item) => _pending[item] = ++_revision;

    internal void Flush(Func<T, bool> save)
    {
        foreach (var item in new List<KeyValuePair<T, long>>(_pending))
        {
            if (save(item.Key) && _pending.TryGetValue(item.Key, out long revision) && revision == item.Value)
                _pending.Remove(item.Key);
        }
    }
}
