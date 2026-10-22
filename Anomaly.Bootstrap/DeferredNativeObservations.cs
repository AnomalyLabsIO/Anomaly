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
    private readonly HashSet<IntPtr> _pending = new();
    private readonly Func<IntPtr, MethodBase?> _resolve;
    private readonly Action<MethodBase> _report;
    private readonly Action<string> _reportGap;
    private bool _overflowReported;

    internal DeferredNativeObservations() : this(
        source => ProtectedMethodCatalog.TryGetNativeTarget(source, out MethodBase target) ? target : null,
        RuntimeGuard.ReportNativeObservation,
        RuntimeGuard.ReportCoverageGap)
    {
    }

    internal DeferredNativeObservations(Func<IntPtr, MethodBase?> resolve, Action<MethodBase> report,
        Action<string> reportGap)
    {
        _resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
        _report = report ?? throw new ArgumentNullException(nameof(report));
        _reportGap = reportGap ?? throw new ArgumentNullException(nameof(reportGap));
    }

    internal void Record(IntPtr source, bool applied)
    {
        if (!applied || source == IntPtr.Zero)
            return;
        bool overflow = false;
        lock (_sync)
        {
            if (_pending.Contains(source))
                return;
            if (_pending.Count < MaximumPendingSources)
                _pending.Add(source);
            else if (!_overflowReported)
                overflow = _overflowReported = true;
        }
        if (overflow)
            _reportGap("Deferred native-source observation capacity exceeded; an applied source could not be retained.");
    }

    internal void Reconcile()
    {
        IntPtr[] snapshot;
        lock (_sync) snapshot = _pending.ToArray();
        foreach (IntPtr source in snapshot)
        {
            // Catalog resolution and audit publication never run under our lock.
            // Unresolved sources remain pending across every subsequent refresh.
            MethodBase? target = _resolve(source);
            if (target == null)
                continue;
            lock (_sync)
                if (!_pending.Remove(source))
                    continue;
            _report(target);
        }
    }
}
