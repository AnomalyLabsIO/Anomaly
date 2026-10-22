using System.Reflection;

namespace Anomaly.Bootstrap;

/// <summary>
/// Retains successful raw-source applications until the finite native catalog can
/// identify them. Undoing a detour cannot erase process-lifetime evidence.
/// </summary>
internal sealed class DeferredNativeObservations
{
    internal const int MaximumPendingSources = 4096;
    private readonly object _sync = new();
    private readonly Dictionary<IntPtr, PendingSource> _pending = new();
    private readonly LinkedList<PendingSource> _newSources = new();
    private readonly Func<IntPtr, MethodBase?> _resolve;
    private readonly Action<MethodBase> _report;
    private readonly Action<string> _reportGap;
    private readonly Func<long>? _mappingRevision;
    private PendingSource? _first;
    private PendingSource? _last;
    private PendingSource? _rescan;
    private long _sequence;
    private long _rescanThrough;
    private long _rescanRevision;
    private long _completedRevision;
    private long _revision;
    private int _inFlight;
    private bool _revisionInitialized;
    private bool _rescanActive;
    private bool _preferNew;
    private bool _overflowReported;

    internal long Revision { get { lock (_sync) return _revision; } }

    internal DeferredNativeObservations() : this(
        source => ProtectedMethodCatalog.TryGetNativeTarget(source, out MethodBase target) ? target : null,
        RuntimeGuard.ReportNativeObservation,
        RuntimeGuard.ReportCoverageGap,
        () => ProtectedMethodCatalog.NativeRevision)
    {
    }

    internal DeferredNativeObservations(Func<IntPtr, MethodBase?> resolve, Action<MethodBase> report,
        Action<string> reportGap, Func<long>? mappingRevision = null)
    {
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        _report = report ?? throw new ArgumentNullException(nameof(report));
        _reportGap = reportGap ?? throw new ArgumentNullException(nameof(reportGap));
        _mappingRevision = mappingRevision;
    }

    internal void Record(IntPtr source, bool applied)
    {
        if (!applied || source == IntPtr.Zero) return;
        bool overflow = false;
        lock (_sync)
        {
            if (_pending.ContainsKey(source)) return;
            if (_pending.Count < MaximumPendingSources)
            {
                var pending = new PendingSource(source, ++_sequence) { Previous = _last };
                if (_last == null) _first = pending;
                else _last.Next = pending;
                _last = pending;
                pending.NewNode = _newSources.AddLast(pending);
                _pending.Add(source, pending);
                _revision++;
            }
            else if (!_overflowReported)
            {
                overflow = _overflowReported = true;
                _inFlight++;
                _revision++;
            }
        }
        if (overflow)
        {
            try { _reportGap("Deferred native-source observation capacity exceeded; an applied source could not be retained."); }
            finally { lock (_sync) { _inFlight--; _revision++; } }
        }
    }

    internal bool HasPendingWork
    {
        get
        {
            // A worker only reads revisions/counts; it never starts a rescan or
            // walks retained sources while checking an admission completion.
            long? revision = _mappingRevision?.Invoke();
            lock (_sync)
                return _inFlight != 0 || _newSources.Count != 0 || _rescan != null ||
                    (_pending.Count != 0 && revision.HasValue && _revisionInitialized && revision.Value != _completedRevision);
        }
    }

    internal bool ProcessOnePendingWork()
    {
        long? revision = _mappingRevision?.Invoke();
        PendingSource source;
        long attemptRevision;
        lock (_sync)
        {
            if (revision.HasValue)
            {
                if (!_revisionInitialized)
                {
                    _revisionInitialized = true;
                    _completedRevision = revision.Value;
                }
                if (_rescan == null && revision.Value != _completedRevision)
                {
                    _rescan = _first;
                    _rescanThrough = _sequence;
                    _rescanRevision = revision.Value;
                    _rescanActive = _rescan != null;
                    if (_rescan == null) _completedRevision = revision.Value;
                }
            }
            _preferNew = !_preferNew;
            bool useNew = _newSources.Count != 0 && (_preferNew || _rescan == null);
            if (useNew)
            {
                source = _newSources.First!.Value;
                _newSources.RemoveFirst();
                source.NewNode = null;
                attemptRevision = revision ?? long.MinValue;
            }
            else if (_rescan != null)
            {
                source = _rescan;
                _rescan = source.Next;
                attemptRevision = _rescanRevision;
                FinishRescanIfNeededLocked();
                if (source.LastAttemptRevision >= attemptRevision) return true;
                if (source.NewNode != null)
                {
                    _newSources.Remove(source.NewNode);
                    source.NewNode = null;
                }
            }
            else return false;
            source.LastAttemptRevision = attemptRevision;
            _inFlight++;
            _revision++;
        }
        try { Resolve(source); }
        finally { lock (_sync) { _inFlight--; _revision++; } }
        return true;
    }

    internal void Reconcile()
    {
        // Explicit checkpoints preserve the original captured-cohort behavior.
        // The three-argument test adapter has no revision source, so its caller
        // still controls when a changed fake dictionary must be retried.
        long? revision = _mappingRevision?.Invoke();
        PendingSource? cursor;
        long through;
        lock (_sync)
        {
            if (revision.HasValue && _revisionInitialized && revision.Value == _completedRevision &&
                _rescan == null && _newSources.Count == 0) return;
            cursor = _first;
            through = _sequence;
        }
        while (cursor != null && cursor.Sequence <= through)
        {
            PendingSource current = cursor;
            lock (_sync)
            {
                // Removed nodes retain their forward link for a detached cohort.
                cursor = current.Next;
                if (!_pending.TryGetValue(current.Source, out PendingSource? live) || !ReferenceEquals(live, current)) continue;
                if (revision.HasValue && current.LastAttemptRevision >= revision.Value) continue;
                current.LastAttemptRevision = revision ?? long.MinValue;
                if (current.NewNode != null)
                {
                    _newSources.Remove(current.NewNode);
                    current.NewNode = null;
                }
                _inFlight++;
                _revision++;
            }
            try { Resolve(current); }
            finally { lock (_sync) { _inFlight--; _revision++; } }
        }
        lock (_sync)
        {
            _rescan = null;
            _rescanActive = false;
            if (revision.HasValue)
            {
                _revisionInitialized = true;
                _completedRevision = revision.Value;
            }
        }
    }

    private void Resolve(PendingSource source)
    {
        MethodBase? target = _resolve(source.Source);
        if (target == null) return;
        lock (_sync)
        {
            if (!_pending.TryGetValue(source.Source, out PendingSource? current) || !ReferenceEquals(current, source)) return;
            _pending.Remove(source.Source);
            if (source.NewNode != null) _newSources.Remove(source.NewNode);
            if (source.Previous == null) _first = source.Next;
            else source.Previous.Next = source.Next;
            if (source.Next == null) _last = source.Previous;
            else source.Next.Previous = source.Previous;
            if (ReferenceEquals(_rescan, source)) _rescan = source.Next;
            FinishRescanIfNeededLocked();
        }
        _report(target);
    }

    private void FinishRescanIfNeededLocked()
    {
        if (!_rescanActive) return;
        if (_rescan != null && _rescan.Sequence <= _rescanThrough) return;
        _rescan = null;
        _rescanActive = false;
        _completedRevision = _rescanRevision;
    }

    private sealed class PendingSource
    {
        internal readonly IntPtr Source;
        internal readonly long Sequence;
        internal PendingSource? Previous;
        internal PendingSource? Next;
        internal LinkedListNode<PendingSource>? NewNode;
        internal long LastAttemptRevision = long.MinValue;
        internal PendingSource(IntPtr source, long sequence) { Source = source; Sequence = sequence; }
    }
}
