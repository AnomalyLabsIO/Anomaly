using Anomaly.Client.Api.Features;

namespace Anomaly.Client.Api.Internal.Items;

/// <summary>Tracks snapshot revisions so event callbacks can invalidate an in-progress notification batch.</summary>
internal sealed class FirearmObservationTracker<TKey>
{
    private readonly Dictionary<TKey, Snapshot> _snapshots = new();
    private long _revision;

    private readonly struct Snapshot
    {
        internal Snapshot(FirearmState state, long revision)
        {
            State = state;
            Revision = revision;
        }

        internal FirearmState State { get; }
        internal long Revision { get; }
    }

    internal IEnumerable<TKey> Keys => _snapshots.Keys;

    internal bool TryObserve(TKey key, FirearmState current, out FirearmState? previous, out long revision)
    {
        bool hadPrevious = _snapshots.TryGetValue(key, out Snapshot snapshot);
        previous = hadPrevious ? snapshot.State : null;
        if (hadPrevious && snapshot.State.Equals(current))
        {
            revision = snapshot.Revision;
            return false;
        }

        revision = ++_revision;
        _snapshots[key] = new Snapshot(current, revision);
        return true;
    }

    internal bool IsCurrent(TKey key, long revision) =>
        _snapshots.TryGetValue(key, out Snapshot snapshot) && snapshot.Revision == revision;

    internal void Remove(TKey key) => _snapshots.Remove(key);

    internal void Clear()
    {
        _snapshots.Clear();
        // Keep the sequence monotonic: callbacks can reconnect and observe the same
        // key before an older event batch resumes, even with identical values.
    }
}
