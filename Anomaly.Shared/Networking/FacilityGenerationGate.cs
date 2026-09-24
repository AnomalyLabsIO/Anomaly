using System;
using System.IO;
using Anomaly.Shared.Networking.Messages;

namespace Anomaly.Shared.Networking;

/// <summary>Pure connection-local gate; unauthenticated seeds never authorize generation.</summary>
internal sealed class FacilityGenerationGate
{
    private int? _seed;
    private ulong _lastGeneration;
    private ulong _completedGeneration;
    private double? _waitingSince;
    private bool _seedMayBelongToCompletedGeneration;
    internal FacilitySnapshotMessage Snapshot { get; private set; }
    internal bool Completed { get; private set; }
    internal bool Generating { get; private set; }

    internal void ReceiveSeed(int seed)
    {
        if (Completed)
        {
            // A duplicate vanilla seed must not invalidate a completed bootstrap.
            // Retain a newly received seed for the next authenticated generation.
            _seed = seed;
            _seedMayBelongToCompletedGeneration = true;
            return;
        }
        if (_seed.HasValue && _seed.Value != seed && Snapshot != null)
            throw new InvalidDataException("Facility seed changed during bootstrap.");
        _seed = seed;
        _seedMayBelongToCompletedGeneration = false;
        CheckMatch();
    }

    internal void ReceiveSnapshot(FacilitySnapshotMessage snapshot)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
        snapshot.Validate();
        if (snapshot.GenerationId <= _lastGeneration)
        {
            if (Snapshot != null && snapshot.GenerationId == Snapshot.GenerationId &&
                Convert.ToBase64String(snapshot.Digest) == Convert.ToBase64String(Snapshot.Digest)) return;
            throw new InvalidDataException("Stale or conflicting facility generation.");
        }
        if (Generating) throw new InvalidDataException("Facility snapshot changed during generation.");
        // A duplicate prior seed can cross scene teardown before the next snapshot.
        // It must not reject a later authenticated snapshot carrying a different seed.
        if (_seedMayBelongToCompletedGeneration && _seed != snapshot.Seed) _seed = null;
        _seedMayBelongToCompletedGeneration = false;
        Completed = false;
        _lastGeneration = snapshot.GenerationId;
        Snapshot = snapshot.Copy();
        CheckMatch();
    }

    internal bool TryBegin()
    {
        if (Completed || Generating) return false;
        CheckMatch();
        if (Snapshot == null || !_seed.HasValue) return false;
        Generating = true;
        return true;
    }

    internal void Complete()
    {
        if (!Generating) throw new InvalidOperationException("Facility generation was not authorized.");
        Generating = false;
        Completed = true;
        _completedGeneration = Snapshot.GenerationId;
        _seed = null;
        _seedMayBelongToCompletedGeneration = false;
        _waitingSince = null;
    }

    internal void EndScene()
    {
        if (Snapshot?.GenerationId == _completedGeneration) Snapshot = null;
        Completed = Generating = false;
    }

    internal void BeginWaiting(double now) { if (!_waitingSince.HasValue) _waitingSince = now; }
    internal bool HasTimedOut(double now) => _waitingSince.HasValue && now - _waitingSince.Value >= 30d;

    internal void Reset()
    {
        Snapshot = null;
        _seed = null;
        _lastGeneration = 0;
        _completedGeneration = 0;
        _waitingSince = null;
        Completed = Generating = false;
        _seedMayBelongToCompletedGeneration = false;
    }

    private void CheckMatch()
    {
        if (Snapshot != null && _seed.HasValue && Snapshot.Seed != _seed.Value)
            throw new InvalidDataException("Vanilla seed did not match authenticated facility snapshot.");
    }
}
