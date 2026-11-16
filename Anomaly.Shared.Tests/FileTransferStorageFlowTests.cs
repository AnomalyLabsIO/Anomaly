using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using Anomaly.Client.Api.FileTransfer;
using Anomaly.Client.Api.Networking;
using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Client.FileTransfer;
using Anomaly.Client.Components;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using Xunit;

namespace Anomaly.Shared.Tests;

public sealed class FileTransferStorageFlowTests : IDisposable
{
    public FileTransferStorageFlowTests()
    {
        FileTransferTestPump.Reset();
        AnomalyMessaging.ResetForTests();
        FileCache.HasFileResult = _ => false;
    }

    [Fact]
    public void Cancellation_during_cache_verification_returns_immediately_and_old_results_cannot_approve_a_new_session()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var verified = new List<byte[]>();
        Action<byte[]> onVerified = hash => verified.Add(hash);
        FileTransferClient.FileVerified += onVerified;
        FileCache.HasFileResult = _ => { entered.Set(); release.Wait(); return false; };
        var old = Offer(42, new byte[] { 1 });
        Assert.True(FileTransferClient.ApprovePendingManifest(old.ManifestSha256));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        Assert.Empty(AnomalyMessaging.Sent);
        var worker = (FileReceiveWorker)typeof(FileTransferClient).GetField("_worker", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        try
        {
            var watch = Stopwatch.StartNew();
            FileTransferClient.Reset();
            Assert.True(watch.Elapsed < TimeSpan.FromMilliseconds(250));
            FileCache.HasFileResult = _ => true;
            var current = Offer(43, new byte[] { 2 });
            Assert.True(FileTransferClient.ApprovePendingManifest(current.ManifestSha256));
            FileTransferTestPump.Drain();
            Assert.True(FileTransferClient.IsFileApproved(current.Files[0].Hash, FileContentKind.Bundle));
            release.Set();
            Assert.True(SpinWait.SpinUntil(() => worker.IsStopped, TimeSpan.FromSeconds(5)));
            FileTransferClient.Tick();
            Assert.Equal(43UL, Assert.Single(AnomalyMessaging.Sent.OfType<FileRequestMessage>()).SessionId);
            Assert.False(FileTransferClient.IsFileApproved(old.Files[0].Hash, FileContentKind.Bundle));
            Assert.Equal(current.Files[0].Hash, Assert.Single(verified));
        }
        finally
        {
            release.Set();
            Assert.True(SpinWait.SpinUntil(() => worker.IsStopped, TimeSpan.FromSeconds(5)));
            FileTransferClient.FileVerified -= onVerified;
        }
    }

    [Fact]
    public void Early_completion_integrity_retry_and_stale_attempts_preserve_cumulative_disk_credits_and_final_readiness()
    {
        var data = Enumerable.Range(1, 16).Select(value => (byte)value).ToArray();
        var manifest = Offer(51, data);
        FileTransferClient.ApprovePendingManifest(manifest.ManifestSha256);
        FileTransferTestPump.Drain();
        Complete(manifest, 0);
        Chunk(manifest, 0, new byte[data.Length]);
        FileTransferTestPump.Drain();
        var failure = Assert.Single(AnomalyMessaging.Sent.OfType<FileAckMessage>());
        Assert.Equal((byte)1, failure.Status);
        Assert.Equal((byte)0, failure.Attempt);
        Assert.False(FileTransferClient.IsFileApproved(manifest.Files[0].Hash, FileContentKind.Bundle));
        Chunk(manifest, 0, data); // A delayed old attempt cannot create a new write/credit.
        Complete(manifest, 0);
        Complete(manifest, 1);
        Chunk(manifest, 1, data);
        FileTransferTestPump.Drain();
        Assert.True(FileTransferClient.IsFileApproved(manifest.Files[0].Hash, FileContentKind.Bundle));
        Assert.Equal(new byte[] { 0, 1 }, AnomalyMessaging.Sent.OfType<FileAckMessage>().Select(ack => ack.Attempt));
        var credits = AnomalyMessaging.Sent.OfType<FileCreditMessage>().ToArray();
        Assert.Equal(2, credits.Length);
        Assert.Equal(32UL, credits.Last().ReleasedBytes);
        Assert.Equal(2u, credits.Last().ReleasedFrames[0]);
        Assert.Equal((byte)0, AnomalyMessaging.Sent.OfType<FileAckMessage>().Last().Status);
    }

    [Fact]
    public void Receipt_without_pumping_disk_results_cannot_grant_a_ninth_domain_frame()
    {
        var data = new byte[FileChunkMessage.MaxChunkBytes * 9];
        var manifest = Offer(61, data);
        FileTransferClient.ApprovePendingManifest(manifest.ManifestSha256);
        FileTransferTestPump.Drain();
        for (ushort index = 0; index < 9; index++)
            FileTransferClient.ReceiveBootstrap(new FileChunkMessage { SessionId = manifest.SessionId, Hash = manifest.Files[0].Hash,
                ChunkIndex = index, TotalChunks = 9, FrameCounter = (uint)index + 1, Data = new byte[FileChunkMessage.MaxChunkBytes] });
        Assert.Empty(AnomalyMessaging.Sent.OfType<FileCreditMessage>());
        Assert.False(FileTransferClient.IsBulkReceiving);
        Assert.False(FileTransferClient.IsFileApproved(manifest.Files[0].Hash, FileContentKind.Bundle));
    }

    [Fact]
    public void Queued_admission_can_wait_beyond_thirty_seconds_without_renewing_its_bounded_deadline()
    {
        var manifest = Offer(71, new byte[] { 1, 2, 3 });
        FileTransferClient.ApprovePendingManifest(manifest.ManifestSha256);
        FileTransferTestPump.Drain();
        Assert.Equal("AwaitingAdmission", State());
        var status = new FileTransferStatusMessage { SessionId = manifest.SessionId, Status = FileTransferStatus.Queued,
            QueueWaitMilliseconds = 60000, QueuePosition = 3 };
        FileTransferClient.ReceiveBootstrap(status);
        var deadline = Field("_queueDeadline");
        var original = deadline.GetValue(null);
        Field("_lastActivityUtc").SetValue(null, DateTime.UtcNow.AddSeconds(-31));
        FileTransferClient.Tick();
        Assert.Equal("Queued", State());
        FileTransferClient.ReceiveBootstrap(status);
        Assert.Equal(original, deadline.GetValue(null));
        deadline.SetValue(null, 0d);
        FileTransferClient.Tick();
        Assert.Equal("Aborted", State());
    }

    [Fact]
    public void Valid_bulk_overtaking_admission_is_kept_and_late_control_cannot_renew_inactivity()
    {
        byte[] data = { 3, 4, 5 };
        var manifest = Offer(72, data);
        FileTransferClient.ApprovePendingManifest(manifest.ManifestSha256);
        FileTransferTestPump.Drain();
        Chunk(manifest, 0, data);
        Assert.Equal("Receiving", State());
        var old = DateTime.UtcNow.AddSeconds(-10);
        Field("_lastActivityUtc").SetValue(null, old);
        FileTransferClient.ReceiveBootstrap(new FileTransferStatusMessage
        { SessionId = manifest.SessionId, Status = FileTransferStatus.Admitted });
        Assert.Equal(old, Field("_lastActivityUtc").GetValue(null));
        Complete(manifest, 0); FileTransferTestPump.Drain();
        Assert.True(FileTransferClient.IsFileApproved(manifest.Files[0].Hash, FileContentKind.Bundle));
    }

    [Fact]
    public void Retired_status_cannot_admit_replacement_and_active_transport_change_aborts_current_request()
    {
        var manifest = Offer(73, new byte[] { 9 });
        FileTransferClient.ConfigureBulkProfile(BulkTransferProtocol.ProfileVersion, FileTransferTestPump.LaneMask, () => true);
        FileTransferClient.ApprovePendingManifest(manifest.ManifestSha256); FileTransferTestPump.Drain();
        FileTransferClient.ReceiveBootstrap(new FileTransferStatusMessage { SessionId = 72, Status = FileTransferStatus.Admitted });
        Assert.Equal("AwaitingAdmission", State());
        FileTransferClient.ConfigureBulkProfile(BulkTransferProtocol.ProfileVersion, FileTransferTestPump.LaneMask, () => false);
        FileTransferClient.Tick();
        Assert.Equal("Aborted", State());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Abort_preserves_failure_context_when_a_subscriber_resets_or_starts_a_replacement(bool replacement)
    {
        const ulong oldSession = 14913118788744606455;
        var first = Offer(oldSession, new byte[] { 1, 2, 3 });
        byte[] laterHash;
        using (var sha = SHA256.Create()) laterHash = sha.ComputeHash(new byte[] { 4, 5, 6 });
        // Re-offer two real requested files, so one verified file leaves an
        // active transaction whose failure count must survive subscriber Reset.
        FileTransferClient.Reset();
        first.Files.Add(new FileManifestMessage.FileEntry("later.bundle", laterHash, 3, FileContentKind.Bundle));
        first.ManifestSha256 = FileManifestMessage.ComputeManifestSha256(first.Files);
        FileTransferTestPump.Configure(first);
        FileTransferClient.ReceiveBootstrap(first);
        Assert.True(FileTransferClient.ApprovePendingManifest(first.ManifestSha256));
        FileTransferTestPump.Drain();
        Chunk(first, 0, new byte[] { 1, 2, 3 });
        Complete(first, 0);
        FileTransferTestPump.Drain();
        Assert.Equal(1, (int)Field("_completedFiles").GetValue(null));
        Assert.Equal("Receiving", State());

        var errors = new List<string>();
        var completion = new List<FileTransferCompleteEventArgs>();
        MelonLoader.MelonLogger.ErrorObserved = errors.Add;
        FileTransferEvents.CompleteObserved = completion.Add;
        int failures = 0;
        Action<bool> onFinished = successful =>
        {
            Assert.False(successful);
            failures++;
            Assert.Contains(errors, text => text.Contains(oldSession.ToString("x16")));
            Assert.False(FileTransferOverlay.Visible);
            FileTransferClient.Reset();
            if (replacement) Offer(91, new byte[] { 7, 8, 9 });
        };
        FileTransferClient.TransferFinished += onFinished;
        try
        {
            FileTransferClient.ConfigureBulkProfile(BulkTransferProtocol.ProfileVersion, FileTransferTestPump.LaneMask, () => false);
            FileTransferClient.Tick(); // Real active-transport failure invokes Abort.
            Assert.Equal(1, failures);
            var failure = Assert.Single(completion);
            Assert.False(failure.Success);
            Assert.Equal(1, failure.FileCount);
            Assert.Contains(errors, text => text.Contains(oldSession.ToString("x16")) &&
                text.Contains("the active transport no longer owns the selected bulk pool"));
            Assert.Equal(replacement, FileTransferOverlay.Visible);
            Assert.Equal(replacement ? "AwaitingConsent" : "Idle", State());
            FileTransferClient.Tick();
            Assert.Equal(1, failures);
            Assert.Single(completion);
        }
        finally
        {
            FileTransferClient.TransferFinished -= onFinished;
            FileTransferEvents.CompleteObserved = null;
            MelonLoader.MelonLogger.ErrorObserved = null;
        }
    }

    private static FieldInfo Field(string name) => typeof(FileTransferClient).GetField(name, BindingFlags.Static | BindingFlags.NonPublic);
    private static string State() => Field("_state").GetValue(null).ToString();

    [Fact]
    public void Later_disk_consumption_returns_storage_bytes_but_only_the_missing_first_counter_advances_the_watermark()
    {
        const uint counterBase = 100;
        var manifest = Offer(81, new byte[FileChunkMessage.MaxChunkBytes * 9], counterBase);
        Assert.True(FileTransferClient.ApprovePendingManifest(manifest.ManifestSha256));
        FileTransferTestPump.Drain();
        for (ushort index = 1; index < 8; index++)
            IndexedChunk(manifest, index, 9, counterBase + index + 1);
        FileTransferTestPump.Drain();
        FlushCredits();
        var beforeGap = AnomalyMessaging.Sent.OfType<FileCreditMessage>().Last();
        Assert.Equal(7u, beforeGap.ReleasedFrames[0]);
        Assert.Equal(7UL * FileChunkMessage.MaxChunkBytes, beforeGap.ReleasedBytes);
        Assert.Equal(counterBase, beforeGap.ConsumedCounters[0]);
        Assert.Equal("Receiving", State());

        IndexedChunk(manifest, 0, 9, counterBase + 1);
        FileTransferTestPump.Drain();
        FlushCredits();
        var closedGap = AnomalyMessaging.Sent.OfType<FileCreditMessage>().Last();
        Assert.Equal(8u, closedGap.ReleasedFrames[0]);
        Assert.Equal(counterBase + 8, closedGap.ConsumedCounters[0]);

        IndexedChunk(manifest, 8, 9, counterBase + 9);
        Complete(manifest, 0);
        FileTransferTestPump.Drain();
        var final = AnomalyMessaging.Sent.OfType<FileCreditMessage>().Last();
        Assert.Equal(9u, final.ReleasedFrames[0]);
        Assert.Equal(counterBase + 9, final.ConsumedCounters[0]);
        Assert.True(FileTransferClient.IsFileApproved(manifest.Files[0].Hash, FileContentKind.Bundle));
    }

    [Fact]
    public void Consuming_later_frames_cannot_recycle_storage_credits_past_a_held_counter_gap()
    {
        const uint counterBase = 700;
        var manifest = Offer(82, new byte[FileChunkMessage.MaxChunkBytes * 9], counterBase);
        FileTransferClient.ApprovePendingManifest(manifest.ManifestSha256);
        FileTransferTestPump.Drain();
        for (ushort index = 1; index < 8; index++)
            IndexedChunk(manifest, index, 9, counterBase + index + 1);
        FileTransferTestPump.Drain();
        FlushCredits();
        var before = AnomalyMessaging.Sent.OfType<FileCreditMessage>().Last();
        Assert.Equal(7u, before.ReleasedFrames[0]);
        Assert.Equal(counterBase, before.ConsumedCounters[0]);
        IndexedChunk(manifest, 8, 9, counterBase + 9);
        FileTransferTestPump.Drain();
        Assert.Equal("Aborted", State());
        Assert.Same(before, AnomalyMessaging.Sent.OfType<FileCreditMessage>().Last());
        Assert.False(FileTransferClient.IsFileApproved(manifest.Files[0].Hash, FileContentKind.Bundle));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Storage_results_from_a_retired_epoch_or_session_cannot_return_current_counter_credits(bool oldEpoch)
    {
        byte[] data = { 1, 2, 3 };
        var manifest = Offer(83, data, 1000);
        FileTransferClient.ApprovePendingManifest(manifest.ManifestSha256);
        FileTransferTestPump.Drain();
        FileTransferClient.ReceiveBootstrap(new FileTransferStatusMessage { SessionId = manifest.SessionId, Status = FileTransferStatus.Admitted });
        var worker = (FileReceiveWorker)Field("_worker").GetValue(null);
        var results = (System.Collections.Concurrent.ConcurrentQueue<FileReceiveResult>)typeof(FileReceiveWorker)
            .GetField("_results", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(worker);
        long epoch = (long)Field("_epoch").GetValue(null);
        // Model a callback published by retired storage. Every other ownership
        // field deliberately matches, so epoch/session are the deciding guards.
        results.Enqueue(new FileReceiveResult
        {
            Kind = FileReceiveResultKind.Written, Epoch = oldEpoch ? epoch - 1 : epoch,
            SessionId = oldEpoch ? manifest.SessionId : manifest.SessionId - 1,
            File = new FileReceiveFile { Key = FileCache.ToHex(manifest.Files[0].Hash), Hash = manifest.Files[0].Hash,
                Path = manifest.Files[0].Path, Size = manifest.Files[0].Size },
            Domain = 0, FrameCounter = 1001, Bytes = data.Length,
        });
        FileTransferClient.Tick();
        Assert.Equal("Receiving", State());
        Assert.Empty(AnomalyMessaging.Sent.OfType<FileCreditMessage>());
        Assert.Equal(0UL, (ulong)Field("_releasedBytes").GetValue(null));

        Chunk(manifest, 0, data, 1001);
        Complete(manifest, 0);
        FileTransferTestPump.Drain();
        var credit = Assert.Single(AnomalyMessaging.Sent.OfType<FileCreditMessage>());
        Assert.Equal(1001u, credit.ConsumedCounters[0]);
        Assert.Equal(1u, credit.ReleasedFrames[0]);
        Assert.Equal((ulong)data.Length, credit.ReleasedBytes);
        Assert.True(FileTransferClient.IsFileApproved(manifest.Files[0].Hash, FileContentKind.Bundle));
    }

    private static void IndexedChunk(FileManifestMessage manifest, ushort index, ushort total, uint counter) =>
        FileTransferClient.ReceiveBootstrap(new FileChunkMessage { SessionId = manifest.SessionId, Hash = manifest.Files[0].Hash,
            ChunkIndex = index, TotalChunks = total, FrameCounter = counter, Data = new byte[FileChunkMessage.MaxChunkBytes] });

    private static void FlushCredits()
    {
        Field("_lastCreditUtc").SetValue(null, DateTime.MinValue);
        FileTransferClient.Tick();
    }

    private static FileManifestMessage Offer(ulong session, byte[] data, uint counterBase = 0)
    {
        byte[] hash;
        using (var sha = SHA256.Create()) hash = sha.ComputeHash(data);
        var manifest = new FileManifestMessage { SessionId = session, Files = {
            new FileManifestMessage.FileEntry("flow.bundle", hash, (uint)data.Length, FileContentKind.Bundle) } };
        manifest.ManifestSha256 = FileManifestMessage.ComputeManifestSha256(manifest.Files);
        manifest.BulkCounterBases[0] = counterBase;
        FileTransferTestPump.Configure(manifest);
        FileTransferClient.ReceiveBootstrap(manifest);
        return manifest;
    }

    private static void Complete(FileManifestMessage manifest, byte attempt) =>
        FileTransferClient.ReceiveBootstrap(new FileTransferCompleteMessage { SessionId = manifest.SessionId, Hash = manifest.Files[0].Hash, Attempt = attempt });

    private static void Chunk(FileManifestMessage manifest, byte attempt, byte[] data, uint counter = 0) =>
        FileTransferClient.ReceiveBootstrap(new FileChunkMessage { SessionId = manifest.SessionId, Hash = manifest.Files[0].Hash,
            Attempt = attempt, ChunkIndex = 0, TotalChunks = 1, FrameCounter = counter == 0 ? (uint)attempt + 1 : counter, Data = data });

    public void Dispose()
    {
        FileTransferTestPump.Reset();
        FileCache.HasFileResult = _ => false;
        FileCache.ThrowOnImport = false;
    }
}
