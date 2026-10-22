namespace Anomaly.Bootstrap;

// A leased item stays in the dictionary until completion. Mutations during its
// processing cannot disappear behind a stale successful result.
internal sealed class RevisionWorkQueue<T> where T : notnull
{
    private readonly object _sync = new();
    private readonly int _capacity;
    private readonly Queue<T> _ready = new();
    private readonly Dictionary<T, long> _versions = new();
    private long _revision;
    internal RevisionWorkQueue(int capacity) { _capacity = capacity; }
    internal long Revision { get { lock (_sync) return _revision; } }
    internal int Count { get { lock (_sync) return _versions.Count; } }

    internal bool Enqueue(T item)
    {
        lock (_sync)
        {
            if (!_versions.ContainsKey(item))
            {
                if (_versions.Count == _capacity) return false;
                _ready.Enqueue(item);
            }
            _versions[item] = ++_revision;
            return true;
        }
    }

    internal bool TryTake(out T item, out long version)
    {
        lock (_sync)
        {
            if (_ready.Count == 0) { item = default!; version = 0; return false; }
            item = _ready.Dequeue();
            version = _versions[item];
            return true;
        }
    }

    internal bool IsCurrent(T item, long version)
    {
        lock (_sync) return _versions.TryGetValue(item, out long actual) && actual == version;
    }

    internal void Complete(T item, long version)
    {
        lock (_sync)
        {
            if (_versions[item] == version) _versions.Remove(item);
            else _ready.Enqueue(item);
        }
    }
}
