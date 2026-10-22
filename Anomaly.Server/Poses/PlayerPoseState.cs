using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Anomaly.Server.Api.Features;
using Anomaly.Shared.Poses;

namespace Anomaly.Server.Poses;

/// <summary>Engine-independent ownership, expiration and deterministic per-bone composition.</summary>
internal sealed class PlayerPoseState
{
    internal const int MaximumOverridesPerPlayer = 16;
    internal const int MaximumOverrides = 1024;
    internal sealed class Entry
    {
        internal ulong Id;
        internal long Owner;
        internal uint NetId;
        internal ulong ModelGeneration;
        internal PlayerPoseOptions Options;
        internal PoseFrame Frame;
        internal double UpdatedAt;
    }
    internal sealed class Composed
    {
        internal PoseFrame Frame;
        internal ulong Revision;
        internal ulong Sequence;
        internal double ServerTime;
        internal ulong[] Winners;
    }

    private readonly Dictionary<ulong, Entry> _entries = new Dictionary<ulong, Entry>();
    private readonly Dictionary<uint, Composed> _composed = new Dictionary<uint, Composed>();
    private readonly HashSet<uint> _dirty = new HashSet<uint>();
    private ulong _nextId;

    internal bool TryCreate(long owner, uint netId, ulong modelGeneration, PoseFrame frame,
        PlayerPoseOptions options, double now, out ulong id)
    {
        id = 0;
        if (_entries.Count >= MaximumOverrides || _entries.Values.Count(entry => entry.NetId == netId) >= MaximumOverridesPerPlayer)
            return false;
        id = checked(++_nextId);
        _entries.Add(id, new Entry { Id = id, Owner = owner, NetId = netId, ModelGeneration = modelGeneration,
            Frame = Copy(frame), Options = options.Copy(), UpdatedAt = now });
        _dirty.Add(netId);
        return true;
    }

    internal bool TryGet(ulong id, long owner, out Entry entry) => _entries.TryGetValue(id, out entry) && entry.Owner == owner;

    internal void Update(Entry entry, PoseFrame frame, double now)
    {
        entry.Frame = Copy(frame);
        entry.UpdatedAt = now;
        _dirty.Add(entry.NetId);
    }

    internal void Clear(Entry entry)
    {
        _entries.Remove(entry.Id);
        _dirty.Add(entry.NetId);
    }

    internal void Remove(ulong id, long owner)
    {
        if (!TryGet(id, owner, out Entry entry)) return;
        _entries.Remove(id);
        _dirty.Add(entry.NetId);
    }

    internal void RemoveOwner(long owner)
    {
        foreach (Entry entry in _entries.Values.Where(entry => entry.Owner == owner).ToArray())
            Remove(entry.Id, owner);
    }

    internal void RemovePlayer(uint netId)
    {
        foreach (Entry entry in _entries.Values.Where(entry => entry.NetId == netId).ToArray())
            _entries.Remove(entry.Id);
        _composed.Remove(netId);
        _dirty.Remove(netId);
    }

    internal void Reset()
    {
        _entries.Clear();
        _composed.Clear();
        _dirty.Clear();
        // IDs deliberately remain monotonic so stale handles cannot address a later registration.
    }

    internal void ResetInterpolation(uint netId)
    {
        if (_composed.TryGetValue(netId, out Composed state)) state.Revision = checked(state.Revision + 1);
        _dirty.Add(netId);
    }

    internal void Expire(double now)
    {
        foreach (Entry entry in _entries.Values.ToArray())
            if (entry.Frame != null && entry.Options.InactivityTimeout != Timeout.InfiniteTimeSpan &&
                now - entry.UpdatedAt >= entry.Options.InactivityTimeout.TotalSeconds)
                Clear(entry);
    }

    internal Composed Compose(uint netId, double now)
    {
        if (!_composed.TryGetValue(netId, out Composed result))
        {
            result = new Composed();
            _composed.Add(netId, result);
            _dirty.Add(netId);
        }
        if (!_dirty.Remove(netId))
        {
            // Persistent poses still refresh the receiver's safety timeout while the stream is healthy.
            if (result.Frame != null && now - result.ServerTime >= .2d)
            { result.Sequence = checked(result.Sequence + 1); result.ServerTime = now; }
            return result;
        }

        var winners = new SortedDictionary<ushort, KeyValuePair<Entry, PoseBone>>();
        foreach (Entry entry in _entries.Values.Where(entry => entry.NetId == netId && entry.Frame != null)
                     .OrderByDescending(entry => entry.Options.Priority).ThenBy(entry => entry.Id))
        {
            foreach (PoseBone bone in entry.Frame.Bones)
            {
                if (bone.Weight * entry.Options.Weight <= 0f || winners.ContainsKey(bone.BoneId)) continue;
                winners.Add(bone.BoneId, new KeyValuePair<Entry, PoseBone>(entry, bone));
            }
        }
        PoseFrame frame = null;
        ulong[] winnerIds = winners.Values.Select(pair => pair.Key.Id).ToArray();
        if (winners.Count > 0)
        {
            Entry first = winners.Values.First().Key;
            frame = new PoseFrame(first.Frame.RigId, first.Frame.SchemaVersion, winners.Values.Select(pair =>
            {
                PoseBone bone = pair.Value;
                return new PoseBone(bone.BoneId, bone.Components, bone.X, bone.Y, bone.Z,
                    bone.Qx, bone.Qy, bone.Qz, bone.Qw, bone.Weight * pair.Key.Options.Weight,
                    pair.Key.Options.IncludeLocalPlayer);
            }));
        }
        bool topologyChanged = !SameTopology(result.Frame, frame) || result.Winners == null || !result.Winners.SequenceEqual(winnerIds);
        if (topologyChanged) result.Revision = checked(result.Revision + 1);
        result.Sequence = checked(result.Sequence + 1);
        result.ServerTime = now;
        result.Frame = frame;
        result.Winners = winnerIds;
        return result;
    }

    private static bool SameTopology(PoseFrame left, PoseFrame right)
    {
        if (left == null || right == null) return left == right;
        if (left.RigId != right.RigId || left.SchemaVersion != right.SchemaVersion || left.Bones.Count != right.Bones.Count) return false;
        for (int i = 0; i < left.Bones.Count; i++)
        {
            PoseBone a = left.Bones[i], b = right.Bones[i];
            if (a.BoneId != b.BoneId || a.Components != b.Components || a.IncludeLocalPlayer != b.IncludeLocalPlayer)
                return false;
        }
        return true;
    }

    internal static PoseFrame Copy(PoseFrame frame) => frame == null ? null : new PoseFrame(frame.RigId, frame.SchemaVersion, frame.Bones);
}
