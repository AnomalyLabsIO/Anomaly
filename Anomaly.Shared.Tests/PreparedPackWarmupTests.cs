using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Anomaly.Client.Assets;
using Xunit;

namespace Anomaly.Shared.Tests;

public sealed class PreparedPackWarmupTests
{
    [Fact]
    public void Ready_pack_leases_are_transferred_once_to_final_preparation_and_retirement_releases_only_untaken_packs()
    {
        using var warmup = new PreparedPackWarmup<Lease>();
        var first = new Lease();
        var second = new Lease();
        using var secondPrepared = new ManualResetEventSlim();
        int calls = 0;
        warmup.Schedule("first", _ => { Interlocked.Increment(ref calls); return first; });
        warmup.Schedule("first", _ => throw new InvalidOperationException("duplicate preparation"));
        warmup.Schedule("second", _ => { secondPrepared.Set(); return second; });
        Assert.Same(first, warmup.Take("first", CancellationToken.None));
        Assert.Null(warmup.Take("first", CancellationToken.None));
        Assert.True(secondPrepared.Wait(TimeSpan.FromSeconds(5)));
        warmup.Dispose();
        Wait(() => second.Disposals == 1);
        Assert.NotEqual(Environment.CurrentManagedThreadId, second.DisposeThread);
        Assert.Equal(0, first.Disposals);
        Assert.Equal(1, calls);
        first.Dispose();
        Assert.Equal(1, first.Disposals);
    }

    [Fact]
    public void Retirement_does_not_wait_for_a_blocked_validator_or_release_its_late_lease_in_the_replacement_offer()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var old = new PreparedPackWarmup<Lease>();
        using var current = new PreparedPackWarmup<Lease>();
        var late = new Lease();
        var active = new Lease();
        old.Schedule("pack", _ => { entered.Set(); release.Wait(); return late; });
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        try
        {
            var watch = Stopwatch.StartNew();
            old.Dispose();
            Assert.True(watch.Elapsed < TimeSpan.FromMilliseconds(250));
            current.Schedule("pack", _ => active);
            Assert.Same(active, current.Take("pack", CancellationToken.None));
            Assert.Throws<OperationCanceledException>(() => old.Take("pack", CancellationToken.None));
            release.Set();
            Wait(() => late.Disposals == 1);
            Assert.Equal(0, active.Disposals);
        }
        finally { release.Set(); active.Dispose(); }
    }

    [Fact]
    public void Retirement_cancels_queued_validators_and_still_disposes_the_active_late_result()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var warmup = new PreparedPackWarmup<Lease>();
        var late = new Lease();
        int queuedCalls = 0;
        warmup.Schedule("active", _ => { entered.Set(); release.Wait(); return late; });
        warmup.Schedule("queued", _ => { Interlocked.Increment(ref queuedCalls); return new Lease(); });
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        try
        {
            warmup.Dispose();
            release.Set();
            Wait(() => late.Disposals == 1);
            Assert.Equal(0, queuedCalls);
            Assert.NotEqual(Environment.CurrentManagedThreadId, late.DisposeThread);
        }
        finally { release.Set(); }
    }

    [Fact]
    public void Validators_are_serial_and_a_failed_pack_remains_a_failure_of_the_final_transaction()
    {
        using var firstEntered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var secondEntered = new ManualResetEventSlim();
        using var warmup = new PreparedPackWarmup<Lease>();
        var unused = new Lease();
        warmup.Schedule("invalid", _ =>
        {
            firstEntered.Set(); release.Wait();
            throw new InvalidDataException("inert bytes failed validation");
        });
        warmup.Schedule("later", _ => { secondEntered.Set(); return unused; });
        Assert.True(firstEntered.Wait(TimeSpan.FromSeconds(5)));
        try
        {
            Assert.False(secondEntered.IsSet);
            release.Set();
            Assert.Throws<InvalidDataException>(() => warmup.Take("invalid", CancellationToken.None));
            Assert.True(secondEntered.Wait(TimeSpan.FromSeconds(5)));
            warmup.Dispose();
            Wait(() => unused.Disposals == 1);
        }
        finally { release.Set(); }
    }

    private static void Wait(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition()) { Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5)); Thread.Sleep(1); }
    }

    private sealed class Lease : IDisposable
    {
        private int _disposals;
        internal int DisposeThread { get; private set; }
        internal int Disposals => Volatile.Read(ref _disposals);
        public void Dispose()
        {
            DisposeThread = Environment.CurrentManagedThreadId;
            Interlocked.Increment(ref _disposals);
        }
    }
}
