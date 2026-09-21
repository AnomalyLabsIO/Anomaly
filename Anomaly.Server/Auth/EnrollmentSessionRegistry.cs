using System;
using System.Collections.Generic;

namespace Anomaly.Server.Auth;

/// <summary>
/// Orders concurrent authentications for one enrollment and conditionally owns
/// the active hub. Superseded pending authentications cannot replace a newer one,
/// and an older hub's disconnect cannot remove its replacement.
/// </summary>
internal static class EnrollmentSessionRegistry
{
    private static readonly object Sync = new object();
    private static readonly Dictionary<string, EnrollmentEntry> Entries =
        new Dictionary<string, EnrollmentEntry>(StringComparer.Ordinal);
    private static ulong _nextReservation;

    internal static int Count
    {
        get
        {
            lock (Sync)
                return Entries.Count;
        }
    }

    public static ulong Reserve(string enrollmentId, bool allowSameAccountJoining)
    {
        if (allowSameAccountJoining)
            return 0;
        if (string.IsNullOrWhiteSpace(enrollmentId))
            throw new ArgumentException("Enrollment ID is required.", nameof(enrollmentId));

        lock (Sync)
        {
            if (_nextReservation == ulong.MaxValue)
                throw new InvalidOperationException("Enrollment reservation sequence is exhausted.");
            _nextReservation++;
            if (!Entries.TryGetValue(enrollmentId, out var entry))
            {
                entry = new EnrollmentEntry();
                Entries.Add(enrollmentId, entry);
            }
            entry.PendingReservations.Add(_nextReservation);
            return _nextReservation;
        }
    }

    public static bool TryActivate(
        string enrollmentId,
        ulong reservation,
        ReferenceHub hub,
        bool allowSameAccountJoining,
        out ReferenceHub previousHub)
    {
        previousHub = null;
        if (allowSameAccountJoining)
            return true;
        if (reservation == 0 || hub == null || string.IsNullOrWhiteSpace(enrollmentId))
            return false;

        lock (Sync)
        {
            if (!Entries.TryGetValue(enrollmentId, out var entry)
                || entry.PendingReservations.Count == 0
                || entry.PendingReservations[entry.PendingReservations.Count - 1] != reservation)
            {
                return false;
            }

            previousHub = ReferenceEquals(entry.ActiveHub, hub) ? null : entry.ActiveHub;
            entry.PendingReservations.Clear();
            entry.ActiveHub = hub;
            return true;
        }
    }

    public static void Cancel(string enrollmentId, ulong reservation)
    {
        if (reservation == 0 || string.IsNullOrWhiteSpace(enrollmentId))
            return;
        lock (Sync)
        {
            if (!Entries.TryGetValue(enrollmentId, out var entry))
                return;
            entry.PendingReservations.Remove(reservation);
            if (entry.ActiveHub == null && entry.PendingReservations.Count == 0)
                Entries.Remove(enrollmentId);
        }
    }

    /// <summary>
    /// Restores the prior active hub only when <paramref name="replacementHub"/>
    /// still owns the enrollment. A newer successful login is never overwritten
    /// by a late rollback from an older attempt.
    /// </summary>
    public static bool TryRollbackActivation(
        string enrollmentId,
        ReferenceHub replacementHub,
        ReferenceHub previousHub)
    {
        if (replacementHub == null || string.IsNullOrWhiteSpace(enrollmentId))
            return false;

        lock (Sync)
        {
            if (!Entries.TryGetValue(enrollmentId, out var entry) ||
                !ReferenceEquals(entry.ActiveHub, replacementHub))
                return false;

            entry.ActiveHub = previousHub;
            if (entry.ActiveHub == null && entry.PendingReservations.Count == 0)
                Entries.Remove(enrollmentId);
            return true;
        }
    }

    public static void Remove(ReferenceHub hub)
    {
        if (hub == null)
            return;
        lock (Sync)
        {
            string emptyEntry = null;
            foreach (var pair in Entries)
            {
                if (!ReferenceEquals(pair.Value.ActiveHub, hub))
                    continue;
                pair.Value.ActiveHub = null;
                if (pair.Value.PendingReservations.Count == 0)
                    emptyEntry = pair.Key;
                break;
            }
            if (emptyEntry != null)
                Entries.Remove(emptyEntry);
        }
    }

    public static void Reset()
    {
        lock (Sync)
        {
            Entries.Clear();
            _nextReservation = 0;
        }
    }

    private sealed class EnrollmentEntry
    {
        public readonly List<ulong> PendingReservations = new List<ulong>();
        public ReferenceHub ActiveHub;
    }
}
