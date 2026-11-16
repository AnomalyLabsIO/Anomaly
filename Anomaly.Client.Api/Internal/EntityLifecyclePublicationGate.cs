using System.Collections.Generic;

namespace Anomaly.Client.Api.Internal;

/// <summary>Owns entity lifecycle publications independently of lazy wrapper caches and Unity frames.</summary>
internal sealed class EntityLifecyclePublicationGate
{
    private readonly Dictionary<int, Publication> _publications = new();
    private long _revision;

    private readonly struct Publication
    {
        internal Publication(bool added, long revision)
        {
            Added = added;
            Revision = revision;
        }

        internal bool Added { get; }
        internal long Revision { get; }
    }

    internal bool TryAdd(int instanceId, out long revision) => TryTransition(instanceId, true, out revision);

    internal bool TryRemove(int instanceId, out long revision) => TryTransition(instanceId, false, out revision);

    internal bool IsCurrent(int instanceId, long revision) =>
        _publications.TryGetValue(instanceId, out Publication publication) && publication.Revision == revision;

    internal void Reset() => _publications.Clear();

    private bool TryTransition(int instanceId, bool added, out long revision)
    {
        revision = 0;
        if (instanceId == 0 ||
            (_publications.TryGetValue(instanceId, out Publication previous) && previous.Added == added))
            return false;

        // Commit before subscribers run. A removal followed by a real re-add is a
        // new publication even in the same frame; an old notification batch must stop.
        revision = checked(++_revision);
        _publications[instanceId] = new Publication(added, revision);
        return true;
    }
}
