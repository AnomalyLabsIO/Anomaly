using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using Anomaly.Client.FileTransfer;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using Xunit;

namespace Anomaly.Shared.Tests;

public sealed class FileReceiveWorkerTests
{
    [Fact]
    public void Cache_checks_writes_and_commits_run_on_the_disk_owner_and_early_completion_waits_for_all_chunks()
    {
        int caller = Environment.CurrentManagedThreadId, checker = caller;
        var buffer = new Buffer(2);
        using var worker = Create(buffer, hash => { checker = Environment.CurrentManagedThreadId; return false; });
        var prepared = Next(worker);
        Assert.Equal(FileReceiveResultKind.Prepared, prepared.Kind);
        Assert.NotEqual(caller, checker);
        var file = Assert.Single(prepared.Missing);
        Assert.True(worker.TryEnqueue(file, 0, 0, null, 0));
        Assert.True(worker.TryEnqueue(file, 1, 2, new byte[7], 1, 72));
        var later = Next(worker);
        Assert.Equal(7, later.Bytes);
        Assert.Equal(72u, later.FrameCounter);
        Assert.Equal((byte)1, later.Domain);
        Assert.Equal(0, buffer.Commits);
        Assert.True(worker.TryEnqueue(file, 0, 2, new byte[FileChunkMessage.MaxChunkBytes], 0, 71));
        var earlier = Next(worker);
        Assert.Equal(FileChunkMessage.MaxChunkBytes, earlier.Bytes);
        Assert.Equal(71u, earlier.FrameCounter);
        Assert.Equal((byte)0, earlier.Domain);
        var complete = Next(worker);
        Assert.Equal(FileReceiveResultKind.Finished, complete.Kind);
        Assert.True(complete.Success);
        Assert.Equal(1, buffer.Commits);
        Assert.All(buffer.Threads, thread => Assert.NotEqual(caller, thread));
    }

    [Fact]
    public void Duplicate_chunks_do_not_release_credit_twice()
    {
        var buffer = new Buffer(2);
        using var worker = Create(buffer);
        var file = Assert.Single(Next(worker).Missing);
        Assert.True(worker.TryEnqueue(file, 0, 2, new byte[12], 3));
        Assert.Equal(12, Next(worker).Bytes);
        Assert.True(worker.TryEnqueue(file, 0, 2, new byte[12], 3));
        Wait(() => worker.IsIdle);
        Assert.False(worker.TryRead(out _));
        Assert.Equal(1, buffer.Writes);
    }

    [Fact]
    public void Blocked_disk_write_holds_the_entire_bounded_window_and_cancellation_never_waits_for_it()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var buffer = new Buffer(128) { Entered = entered, Release = release };
        var worker = Create(buffer);
        try
        {
            var file = Assert.Single(Next(worker).Missing);
            Assert.True(worker.TryEnqueue(file, 0, 128, new byte[FileChunkMessage.MaxChunkBytes], 0));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            for (ushort index = 1; index < 128; index++)
                Assert.True(worker.TryEnqueue(file, index, 128, new byte[FileChunkMessage.MaxChunkBytes], 0));
            Assert.False(worker.TryEnqueue(file, 0, 128, new byte[1], 0));
            Assert.False(worker.TryRead(out _)); // Receipt and queueing alone release no credits.
            var watch = Stopwatch.StartNew();
            worker.Dispose();
            Assert.True(watch.Elapsed < TimeSpan.FromMilliseconds(250));
            Assert.False(worker.IsStopped);
            release.Set();
            Wait(() => worker.IsStopped);
            Assert.False(worker.TryRead(out _));
            Assert.True(buffer.Disposed);
        }
        finally { release.Set(); worker.Dispose(); Wait(() => worker.IsStopped); }
    }

    [Fact]
    public void Disk_failures_retire_the_worker_and_preserve_the_failed_file_identity()
    {
        var buffer = new Buffer(1) { FailWrite = true };
        using var worker = Create(buffer);
        var file = Assert.Single(Next(worker).Missing);
        worker.TryEnqueue(file, 0, 1, new byte[7], 0);
        var failure = Next(worker);
        Assert.Equal(FileReceiveResultKind.Failed, failure.Kind);
        Assert.Same(file, failure.File);
        Wait(() => worker.IsStopped);
        Assert.False(worker.TryEnqueue(file, 0, 1, new byte[7], 0));
        Assert.True(buffer.Disposed);
    }

    [Fact]
    public void Integrity_failure_releases_one_attempt_before_a_new_attempt_can_commit()
    {
        var buffer = new Buffer(1) { Valid = false };
        using var worker = Create(buffer);
        var first = Assert.Single(Next(worker).Missing);
        worker.TryEnqueue(first, 0, 1, new byte[7], 0);
        worker.TryEnqueue(first, 0, 0, null, 0);
        Assert.Equal(FileReceiveResultKind.Written, Next(worker).Kind);
        var result = Next(worker);
        Assert.False(result.Success);
        Assert.Equal((byte)0, result.File.Attempt);
        Assert.True(buffer.Disposed);
    }

    [Fact]
    public void Completion_markers_cannot_open_more_than_four_disk_buffers()
    {
        var buffers = new List<Buffer>();
        var files = Enumerable.Range(0, 5).Select(index => new FileReceiveFile
            { Key = index.ToString(), Hash = new byte[32], Path = "test.bundle", Size = 7 }).ToList();
        using var worker = new FileReceiveWorker(1, 7, files, _ => false,
            _ => { var buffer = new Buffer(1); buffers.Add(buffer); return buffer; });
        var prepared = Next(worker);
        foreach (var file in prepared.Missing) Assert.True(worker.TryEnqueue(file, 0, 0, null, 0));
        Assert.Equal(FileReceiveResultKind.Failed, Next(worker).Kind);
        Wait(() => worker.IsStopped);
        Assert.Equal(4, buffers.Count);
        Assert.All(buffers, buffer => Assert.True(buffer.Disposed));
    }

    [Fact]
    public void Published_results_retain_epoch_and_session_identity_after_the_owner_is_cancelled()
    {
        using var old = Create(new Buffer(1), epoch: 11, session: 17);
        var oldPrepared = Next(old);
        old.Dispose();
        using var current = Create(new Buffer(1), epoch: 12, session: 18);
        var currentPrepared = Next(current);
        Assert.Equal(11, oldPrepared.Epoch);
        Assert.Equal(17UL, oldPrepared.SessionId);
        Assert.Equal(12, currentPrepared.Epoch);
        Assert.Equal(18UL, currentPrepared.SessionId);
        Assert.NotEqual(currentPrepared.Epoch, oldPrepared.Epoch);
    }

    private static FileReceiveWorker Create(Buffer buffer, Func<byte[], bool> hasFile = null, long epoch = 1, ulong session = 7)
    {
        var file = new FileReceiveFile { Key = "test", Hash = new byte[32], Path = "test.bundle", Size = 7 };
        return new FileReceiveWorker(epoch, session, new List<FileReceiveFile> { file }, hasFile ?? (_ => false), _ => buffer);
    }

    private static FileReceiveResult Next(FileReceiveWorker worker)
    {
        FileReceiveResult result = null;
        Wait(() => worker.TryRead(out result));
        return result;
    }

    private static void Wait(Func<bool> done)
    {
        var watch = Stopwatch.StartNew();
        while (!done()) { Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), "Storage worker did not finish bounded fixture work."); Thread.Sleep(1); }
    }

    private sealed class Buffer : IFileReceiveBuffer
    {
        private readonly bool[] _received;
        internal readonly List<int> Threads = new();
        internal ManualResetEventSlim Entered, Release;
        internal int Writes, Commits;
        internal bool Disposed, FailWrite, Valid = true;
        internal Buffer(int chunks) { _received = new bool[chunks]; }
        public bool IsComplete => _received.All(value => value);
        public bool TryAddChunk(ushort index, ushort total, byte[] data, out bool duplicate, out string error)
        {
            Threads.Add(Environment.CurrentManagedThreadId);
            Entered?.Set(); Release?.Wait();
            if (FailWrite) throw new IOException("injected write failure");
            error = null;
            duplicate = _received[index];
            if (duplicate) return false;
            _received[index] = true; Writes++;
            return true;
        }
        public bool CommitToCache(CancellationToken cancellation)
        { cancellation.ThrowIfCancellationRequested(); Threads.Add(Environment.CurrentManagedThreadId); Commits++; return Valid; }
        public void Dispose() { Disposed = true; }
    }
}

internal static class FileTransferTestPump
{
    internal const ulong LaneMask = 0xFFFFUL << BulkTransferProtocol.FirstPhysicalChannel;
    private static readonly FieldInfo Worker = typeof(FileTransferClient).GetField("_worker", BindingFlags.NonPublic | BindingFlags.Static);

    internal static void Configure(FileManifestMessage manifest)
    {
        FileTransferClient.ConfigureBulkProfile(BulkTransferProtocol.ProfileVersion, LaneMask);
        manifest.BulkLaneMask = LaneMask;
    }

    internal static void Drain()
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            FileTransferClient.Tick();
            var worker = (FileReceiveWorker)Worker.GetValue(null);
            if (worker == null || worker.IsIdle) return;
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), "Client did not consume storage results.");
            Thread.Sleep(1);
        }
    }

    internal static void Reset()
    {
        var worker = (FileReceiveWorker)Worker.GetValue(null);
        FileTransferClient.Reset();
        var watch = Stopwatch.StartNew();
        while (worker != null && !worker.IsStopped)
        {
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), "Cancelled storage did not clean up its partial files.");
            Thread.Sleep(1);
        }
    }
}
