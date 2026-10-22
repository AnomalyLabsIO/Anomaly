using System.Reflection;
using Anomaly.Bootstrap;
using Xunit;

namespace Anomaly.Bootstrap.Tests;

public sealed class DeferredNativeRevisionTests
{
    private static MethodBase Target => typeof(DeferredNativeRevisionTests).GetMethod(nameof(Fixture), BindingFlags.NonPublic | BindingFlags.Static)!;

    [Fact]
    public void UnchangedMappingDoesNotRetryUnresolvedSources()
    {
        long revision = 0;
        int calls = 0;
        var catalog = new Dictionary<IntPtr, MethodBase>();
        var observed = new List<MethodBase>();
        var monitor = new DeferredNativeObservations(source =>
        {
            calls++;
            return catalog.TryGetValue(source, out var target) ? target : null;
        }, observed.Add, _ => throw new InvalidOperationException(), () => revision);
        monitor.Record(new IntPtr(1), true);
        monitor.Reconcile();
        Assert.Equal(1, calls);
        for (int i = 0; i < 100; i++)
        {
            Assert.False(monitor.HasPendingWork);
            Assert.False(monitor.ProcessOnePendingWork());
            monitor.Reconcile();
        }
        Assert.Equal(1, calls);

        catalog.Add(new IntPtr(1), Target);
        revision++;
        Assert.True(monitor.HasPendingWork);
        Assert.True(monitor.ProcessOnePendingWork());
        Assert.Equal(2, calls);
        Assert.Single(observed);
        Assert.False(monitor.HasPendingWork);
    }

    [Fact]
    public void InFlightResolutionAndPublicationCannotLookComplete()
    {
        DeferredNativeObservations? monitor = null;
        monitor = new DeferredNativeObservations(_ =>
        {
            Assert.True(monitor!.HasPendingWork);
            return Target;
        }, _ => Assert.True(monitor!.HasPendingWork), _ => throw new InvalidOperationException(), () => 0);
        long before = monitor.Revision;
        monitor.Record(new IntPtr(1), true);
        Assert.True(monitor.Revision > before);
        long recorded = monitor.Revision;
        Assert.True(monitor.ProcessOnePendingWork());
        Assert.False(monitor.HasPendingWork);
        Assert.True(monitor.Revision > recorded);
    }

    [Fact]
    public void PendingWorkReadsDoNotStartOrWalkAChangedMappingCohort()
    {
        long revision = 0;
        int calls = 0;
        var monitor = new DeferredNativeObservations(_ => { calls++; return null; },
            _ => throw new InvalidOperationException(), _ => throw new InvalidOperationException(), () => revision);
        for (int i = 1; i <= 32; i++) monitor.Record(new IntPtr(i), true);
        monitor.Reconcile();
        Assert.Equal(32, calls);
        revision++;
        var cursor = typeof(DeferredNativeObservations).GetField("_rescan", BindingFlags.NonPublic | BindingFlags.Instance)!;
        for (int i = 0; i < 100; i++) Assert.True(monitor.HasPendingWork);
        Assert.Null(cursor.GetValue(monitor));
        Assert.Equal(32, calls);
        Assert.True(monitor.ProcessOnePendingWork());
        Assert.Equal(33, calls);
        Assert.NotNull(cursor.GetValue(monitor));
    }

    [Fact]
    public void NewSourceDoesNotRescanOlderUnresolvedSources()
    {
        var calls = new List<IntPtr>();
        var observed = new List<MethodBase>();
        var monitor = new DeferredNativeObservations(source =>
        {
            calls.Add(source);
            return source == new IntPtr(2) ? Target : null;
        }, observed.Add, _ => throw new InvalidOperationException(), () => 0);
        monitor.Record(new IntPtr(1), true);
        monitor.Reconcile();
        monitor.Record(new IntPtr(2), true);
        monitor.Reconcile();
        Assert.Equal(new[] { new IntPtr(1), new IntPtr(2) }, calls);
        Assert.Single(observed);
    }

    [Fact]
    public void IncrementalProcessingHandlesOnlyOneSourcePerCall()
    {
        int calls = 0;
        var observed = new List<MethodBase>();
        var monitor = new DeferredNativeObservations(_ => { calls++; return Target; },
            observed.Add, _ => throw new InvalidOperationException(), () => 0);
        for (int i = 1; i <= 4; i++) monitor.Record(new IntPtr(i), true);
        for (int i = 1; i <= 4; i++)
        {
            Assert.True(monitor.ProcessOnePendingWork());
            Assert.Equal(i, calls);
            Assert.Equal(i, observed.Count);
        }
        Assert.False(monitor.ProcessOnePendingWork());
        monitor.Record(new IntPtr(1), true);
        Assert.True(monitor.ProcessOnePendingWork());
        Assert.Equal(5, observed.Count);
    }

    [Fact]
    public void RevisionAndObservationArrivingDuringResolutionRemainPending()
    {
        long revision = 0;
        int calls = 0;
        var observed = new List<MethodBase>();
        DeferredNativeObservations? monitor = null;
        monitor = new DeferredNativeObservations(source =>
        {
            calls++;
            if (source == new IntPtr(1))
            {
                revision++;
                monitor!.Record(new IntPtr(2), true);
            }
            return Target;
        }, observed.Add, _ => throw new InvalidOperationException(), () => revision);
        monitor.Record(new IntPtr(1), true);
        monitor.Reconcile();
        Assert.Single(observed);
        Assert.True(monitor.HasPendingWork);
        monitor.Reconcile();
        Assert.Equal(2, observed.Count);
        Assert.Equal(2, calls);
        Assert.False(monitor.HasPendingWork);
    }

    private static void Fixture() { }
}
