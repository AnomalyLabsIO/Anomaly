using System;
using System.Collections.Generic;
using System.Linq;
using Anomaly.Shared.Assets;

namespace Anomaly.Server.Assets;

/// <summary>Pure bounded ownership/state bookkeeping; it never creates Unity objects.</summary>
internal sealed class DecorativeVisualState
{
    internal sealed class Entry
    {
        internal long Owner;
        internal DecorativeVisualInstance Instance;
        internal byte[] IndexHash;
        internal int Triangles;
    }
    private readonly Dictionary<uint, Entry> _entries = new Dictionary<uint, Entry>();
    private uint _nextId;
    internal ulong Generation { get; private set; } = 1;
    internal ulong Revision { get; private set; }
    internal long Triangles { get; private set; }
    internal IEnumerable<Entry> Entries => _entries.Values;
    internal int Count => _entries.Count;

    internal Entry Spawn(long owner, AssetAddress address, byte[] indexHash, int triangles, float[] position, float[] rotation, float[] scale)
    {
        if (owner <= 0 || indexHash == null || indexHash.Length != 32 || triangles < 0) throw new ArgumentException("Invalid visual ownership or metadata.");
        if (_entries.Count >= AssetPackIndexCodec.MaxSceneInstances || Triangles + triangles > AssetPackIndexCodec.MaxSceneTriangles)
            throw new InvalidOperationException("Decorative visual instance/triangle budget exceeded.");
        uint id = checked(_nextId + 1);
        var instance = new DecorativeVisualInstance { Id = id, Address = address.Value, Position = position, Rotation = rotation, Scale = scale };
        instance.Validate();
        var entry = new Entry { Owner = owner, Instance = instance.Clone(), IndexHash = (byte[])indexHash.Clone(), Triangles = triangles };
        _entries.Add(id, entry); _nextId = id; Triangles += triangles; Revision = checked(Revision + 1);
        return entry;
    }
    internal Entry Update(long owner, uint id, float[] position, float[] rotation, float[] scale)
    {
        if (!_entries.TryGetValue(id, out var entry) || entry.Owner != owner) throw new InvalidOperationException("Visual does not belong to this scope.");
        var candidate = new DecorativeVisualInstance { Id = id, Address = entry.Instance.Address, Position = position, Rotation = rotation, Scale = scale };
        candidate.Validate(); entry.Instance = candidate.Clone(); Revision = checked(Revision + 1); return entry;
    }
    internal bool Remove(long owner, uint id)
    {
        if (!_entries.TryGetValue(id, out var entry)) return false;
        if (entry.Owner != owner) throw new InvalidOperationException("Visual does not belong to this scope.");
        _entries.Remove(id); Triangles -= entry.Triangles; Revision = checked(Revision + 1); return true;
    }
    internal uint[] OwnedIds(long owner) => _entries.Where(pair => pair.Value.Owner == owner).Select(pair => pair.Key).ToArray();
    internal void Clear() { _entries.Clear(); Triangles = 0; Generation = checked(Generation + 1); Revision = 0; }
}
