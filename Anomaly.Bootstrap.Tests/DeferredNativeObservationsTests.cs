using System.Reflection;
using Anomaly.Bootstrap;
using Xunit;

namespace Anomaly.Bootstrap.Tests;

public sealed class DeferredNativeObservationsTests
{
    private static MethodBase Target => typeof(DeferredNativeObservationsTests)
        .GetMethod(nameof(Fixture), BindingFlags.NonPublic | BindingFlags.Static)!;

    [Fact]
    public void Applied_source_is_retained_until_resolved_even_after_undo()
    {
        var catalog = new Dictionary<IntPtr, MethodBase>();
        var observed = new List<MethodBase>();
        var gaps = new List<string>();
        var monitor = Create(catalog, observed, gaps);
        IntPtr source = new(123);

        monitor.Record(source, applied: true);
        monitor.Reconcile();
        monitor.Record(source, applied: false);
        Assert.Empty(observed);

        catalog.Add(source, Target);
        monitor.Reconcile();
        monitor.Reconcile();
        Assert.Equal(new[] { Target }, observed);
        Assert.Empty(gaps);
    }

    [Fact]
    public void Unapplied_and_zero_sources_are_not_modification_evidence()
    {
        var catalog = new Dictionary<IntPtr, MethodBase> { [new IntPtr(7)] = Target, [IntPtr.Zero] = Target };
        var observed = new List<MethodBase>();
        var gaps = new List<string>();
        var monitor = Create(catalog, observed, gaps);

        monitor.Record(new IntPtr(7), applied: false);
        monitor.Record(IntPtr.Zero, applied: true);
        monitor.Reconcile();
        Assert.Empty(observed);
        Assert.Empty(gaps);
    }

    [Fact]
    public void Capacity_is_bounded_deduplicated_and_overflow_is_explicit()
    {
        var catalog = new Dictionary<IntPtr, MethodBase>();
        var observed = new List<MethodBase>();
        var gaps = new List<string>();
        var monitor = Create(catalog, observed, gaps);
        for (int i = 1; i <= DeferredNativeObservations.MaximumPendingSources; i++)
        {
            monitor.Record(new IntPtr(i), applied: true);
            monitor.Record(new IntPtr(i), applied: true);
            catalog.Add(new IntPtr(i), Target);
        }
        Assert.Empty(gaps);
        monitor.Record(new IntPtr(DeferredNativeObservations.MaximumPendingSources + 1), applied: true);
        monitor.Record(new IntPtr(DeferredNativeObservations.MaximumPendingSources + 2), applied: true);
        Assert.Single(gaps);
        monitor.Reconcile();
        Assert.Equal(DeferredNativeObservations.MaximumPendingSources, observed.Count);

        monitor.Record(new IntPtr(1), applied: true);
        monitor.Reconcile();
        Assert.Equal(DeferredNativeObservations.MaximumPendingSources + 1, observed.Count);
    }

    [Fact]
    public void Observation_recorded_while_reconciling_is_retained_for_the_next_pass()
    {
        var observed = new List<MethodBase>();
        DeferredNativeObservations? monitor = null;
        monitor = new DeferredNativeObservations(source =>
        {
            if (source == new IntPtr(1))
                monitor!.Record(new IntPtr(2), applied: true);
            return Target;
        }, observed.Add, _ => throw new InvalidOperationException("Unexpected coverage gap."));
        monitor.Record(new IntPtr(1), applied: true);
        monitor.Reconcile();
        Assert.Single(observed);
        monitor.Reconcile();
        Assert.Equal(2, observed.Count);
    }

    private static DeferredNativeObservations Create(Dictionary<IntPtr, MethodBase> catalog,
        List<MethodBase> observed, List<string> gaps) => new(
        source => catalog.TryGetValue(source, out MethodBase? target) ? target : null,
        observed.Add, gaps.Add);

    private static void Fixture() { }
}
