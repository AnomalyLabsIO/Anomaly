using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using Anomaly.Server.Auth;
using Anomaly.Server.FileTransfer;
using Anomaly.Server.Networking;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using Xunit;
using Messaging = Anomaly.Server.Networking.AnomalyMessaging;

namespace Anomaly.Shared.Tests;

public sealed class BulkServerTransferTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "anomaly-bulk-server-" + Guid.NewGuid().ToString("N"));
    private int _completed;
    private readonly List<string> _failures = new List<string>();

    [Fact]
    public void RealStaggeredRequestCannotSpendAnIncumbentsRemainingDeadline()
    {
        FileTransferSession.ConfigureBudget(256 * 1024, 256 * 1024, 20);
        try
        {
            double incumbentSeconds = 80;
            var incumbent = Start(4, 1024 * 1024, remainingSeconds: () => incumbentSeconds);
            Assert.Empty(_failures);
            incumbentSeconds = 39;
            var newcomer = Start(4, 1024 * 1024, remainingSeconds: () => 110);
            Assert.Single(_failures);
            Assert.True(FileTransferSession.TryGetBulkSendScope(incumbent.Hub.connectionToClient, out _, out _));
            Assert.False(FileTransferSession.TryGetBulkSendScope(newcomer.Hub.connectionToClient, out _, out _));
            Assert.DoesNotContain(Messaging.Sent.OfType<FileTransferStatusMessage>(), status =>
                status.SessionId == newcomer.Session.Manifest.SessionId);
            Assert.Equal(1, Reserved());
        }
        finally
        {
            FileTransferSession.Reset();
            FileTransferSession.ConfigureBudget(BulkTransferBudget.DefaultClientBytesPerSecond,
                BulkTransferBudget.DefaultUploadBytesPerSecond, BulkTransferBudget.DefaultHeadroomPercent);
        }
    }

    [Fact]
    public void RepeatedRealSessionCancellationLeavesNoSchedulerQueueOwner()
    {
        var pump = (BulkTransferPump)typeof(FileTransferSession)
            .GetField("Pump", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        for (int index = 0; index < 64; index++)
        {
            var fixture = Start(1, 16384);
            FileTransferSession.CleanupConnection(fixture.Hub.connectionToClient);
            AuthenticatedTransportSessions.RemoveConnection(fixture.Hub.connectionToClient);
            Assert.Equal(0, pump.Count);
            Assert.Equal(0, pump.QueuedCount);
            FileTransferSession.Tick();
            Assert.Equal(0, Reserved());
        }
        Assert.Empty(_failures);
    }

    [Fact]
    public void RealRequestRejectsTinyFileTailCostBeforeAdmissionOrReadersAtLowUploadRate()
    {
        FileTransferSession.ConfigureBudget(64 * 1024, 64 * 1024, 90);
        try
        {
            var fixture = Start(256, 1, remainingSeconds: () => 18.5);
            Assert.Single(_failures);
            Assert.Contains("capacity", _failures[0]);
            Assert.False(FileTransferSession.TryGetBulkSendScope(fixture.Hub.connectionToClient, out _, out _));
            Assert.Empty(Chunks(fixture.Session));
            Assert.DoesNotContain(Messaging.Sent.OfType<FileTransferStatusMessage>(), message =>
                message.SessionId == fixture.Session.Manifest.SessionId);
        }
        finally
        {
            FileTransferSession.Reset();
            FileTransferSession.ConfigureBudget(BulkTransferBudget.DefaultClientBytesPerSecond,
                BulkTransferBudget.DefaultUploadBytesPerSecond, BulkTransferBudget.DefaultHeadroomPercent);
        }
    }

    [Fact]
    public void SessionCapacityKeepsExactPerFileTailCostThroughProgressVerificationAndRetry()
    {
        var fixture = Start(5, FileChunkMessage.MaxChunkBytes + 1);
        FileTransferAdmission.Work ReadWork()
        {
            object[] arguments = { null };
            Assert.True((bool)typeof(FileTransferSession).GetMethod("TryCapacityWork", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(fixture.Session, arguments));
            return (FileTransferAdmission.Work)arguments[0];
        }
        var requested = (System.Collections.IDictionary)typeof(FileTransferSession)
            .GetField("_requestedFiles", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(fixture.Session);
        var files = requested.Values.Cast<object>().ToArray();
        void Set(object file, string field, object value) => file.GetType()
            .GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(file, value);

        var full = ReadWork();
        Assert.Equal(5L * 16385, full.ReservedBytes);
        Assert.Equal(full.ReservedBytes, full.RemainingBytes);
        Assert.Equal(5L * (20736 + 258), full.RemainingAccountedBytes);
        Assert.Equal(5, full.RemainingFiles);
        Assert.Equal(5, full.PendingCompletions);
        Set(files[0], "NextChunk", 1);
        var partial = ReadWork();
        Assert.Equal(4L * 16385 + 1, partial.RemainingBytes);
        Assert.Equal(4L * 20994 + 258, partial.RemainingAccountedBytes);
        Set(files[1], "Verified", true);
        Set(files[1], "CompletionSent", true);
        var verified = ReadWork();
        Assert.Equal(full.ReservedBytes, verified.ReservedBytes);
        Assert.Equal(3L * 16385 + 1, verified.RemainingBytes);
        Assert.Equal(3L * 20994 + 258, verified.RemainingAccountedBytes);
        Assert.Equal(4, verified.RemainingFiles);
        Assert.Equal(4, verified.PendingCompletions);
        Set(files[0], "NextChunk", 0);
        Set(files[0], "Attempt", (byte)1);
        var retry = ReadWork();
        Assert.Equal(4L * 16385, retry.RemainingBytes);
        Assert.Equal(4L * 20994, retry.RemainingAccountedBytes);
        Assert.Equal(full.ReservedBytes, retry.ReservedBytes);
    }

    [Fact]
    public void DelayedFirstCompletionCannotBecomeStaleWhileOtherTinyFileSlotsCycle()
    {
        const int files = AnomalyReplayState.WindowBits + 8;
        AnomalyMessageRegistry.RegisterDefaults();
        var tx = new AnomalyReplayState();
        var rx = new AnomalyReplayState();
        var controls = new List<CompletionFrame>();
        Messaging.TrySendResult = message =>
        {
            if (message is FileTransferCompleteMessage complete)
            {
                Assert.True(AnomalyMessageRegistry.TryGetChannel(AnomalyMessageRegistry.GetHash(message.MessageName), out var channel));
                controls.Add(new CompletionFrame { Message = complete, Channel = channel, Counter = tx.NextTxCounter(channel) });
            }
            return true;
        };
        var fixture = Start(files, 1);
        double now = 0;
        typeof(FileTransferSession).GetField("_completionPacer", BindingFlags.NonPublic | BindingFlags.Instance)
            .SetValue(fixture.Session, new FileCompletionPacer(() => now));
        uint sequence = 0;
        int credited = 0;
        bool holdFirst = true;

        void DeliverAvailable()
        {
            for (int index = 0; index < controls.Count; index++)
            {
                var frame = controls[index];
                if (frame.Delivered) continue;
                if (holdFirst && index == 0)
                {
                    // Model the chosen native transport: loss of an ordered
                    // packet withholds subsequent deliveries; unordered data
                    // may pass it. Do not reorder ordered messages in the app.
                    if (frame.Channel == MessageChannel.ReliableOrdered) break;
                    continue;
                }
                Assert.Equal(AnomalyReplayState.RxResult.Accepted, rx.TryAcceptRx(frame.Channel, frame.Counter, out _));
                var chunk = Assert.Single(Chunks(fixture.Session).Where(chunk => Hex(chunk.Hash) == Hex(frame.Message.Hash)));
                using (var hash = SHA256.Create()) Assert.Equal(frame.Message.Hash, hash.ComputeHash(chunk.Data));
                frame.Delivered = true;
                Ack(fixture.Session, frame.Message);
            }
        }

        void Step()
        {
            now += 1d / 64;
            FileTransferSession.Tick();
            var chunks = Chunks(fixture.Session);
            if (chunks.Count != credited)
            {
                var credit = new FileCreditMessage { SessionId = fixture.Session.Manifest.SessionId,
                    CreditSequence = ++sequence, ReleasedBytes = (ulong)chunks.Sum(chunk => chunk.Data.Length) };
                foreach (var chunk in chunks)
                {
                    credit.ReleasedFrames[chunk.Domain]++;
                    credit.ConsumedCounters[chunk.Domain] = Math.Max(credit.ConsumedCounters[chunk.Domain], chunk.FrameCounter);
                }
                fixture.Session.HandleMessage(credit);
                credited = chunks.Count;
            }
            DeliverAvailable();
            Thread.Sleep(1);
        }

        PumpUntil(() => { Step(); return controls.Count >= 4; });
        // Under the old unordered contract the other three slots can recycle
        // across the whole inventory while completion1 is still withheld.
        if (controls[0].Channel == MessageChannel.ReliableUnordered)
            PumpUntil(() => { Step(); return controls.Count == files; });
        else
        {
            for (int index = 0; index < 32; index++) Step();
            Assert.Equal(4, controls.Count);
        }
        holdFirst = false;
        PumpUntil(() => { Step(); return fixture.Session.IsComplete; });
        Assert.Equal(files, controls.Count);
        Assert.All(controls, frame => Assert.True(frame.Delivered));
        Assert.Equal(1, _completed);
        Assert.Empty(_failures);
    }

    private sealed class CompletionFrame
    {
        internal FileTransferCompleteMessage Message;
        internal MessageChannel Channel;
        internal uint Counter;
        internal bool Delivered;
    }

    [Fact]
    public void Four_file_pipeline_accepts_reordered_exact_ACKs_and_waits_for_every_verified_file()
    {
        var fixture = Start(5, 50000);
        PumpUntil(() => Completions(fixture.Session).Count == 4);
        var first = Completions(fixture.Session).ToArray();
        Assert.Equal(4, first.Select(message => Hex(message.Hash)).Distinct().Count());
        Assert.Equal(0, _completed);
        Ack(fixture.Session, first[3]);
        PumpUntil(() => Completions(fixture.Session).Count == 5);
        Ack(fixture.Session, first[1]); Ack(fixture.Session, first[0]); Ack(fixture.Session, first[2]);
        Assert.False(fixture.Session.IsComplete);
        var last = Completions(fixture.Session).Last();
        Ack(fixture.Session, last); Ack(fixture.Session, last);
        Assert.True(fixture.Session.IsComplete);
        Assert.Equal(1, _completed); Assert.Empty(_failures);
        Assert.Equal(0, Reserved());
    }

    [Fact]
    public void Per_domain_credit_stops_at_eight_frames_and_duplicate_credits_cannot_mint_more()
    {
        var fixture = Start(1, 1024 * 1024, 1UL << 6);
        PumpUntil(() => Chunks(fixture.Session).Count == 8);
        for (int i = 0; i < 10; i++) FileTransferSession.Tick();
        Assert.Equal(8, Chunks(fixture.Session).Count);
        var credit = new FileCreditMessage { SessionId = fixture.Session.Manifest.SessionId,
            CreditSequence = 1, ReleasedBytes = 8u * FileChunkMessage.MaxChunkBytes };
        credit.ReleasedFrames[0] = 8;
        credit.ConsumedCounters[0] = 8;
        fixture.Session.HandleMessage(credit);
        PumpUntil(() => Chunks(fixture.Session).Count == 16);
        fixture.Session.HandleMessage(credit);
        for (int i = 0; i < 10; i++) FileTransferSession.Tick();
        Assert.Equal(16, Chunks(fixture.Session).Count);
        credit = new FileCreditMessage { SessionId = fixture.Session.Manifest.SessionId,
            CreditSequence = 2, ReleasedBytes = 17u * FileChunkMessage.MaxChunkBytes };
        credit.ReleasedFrames[0] = 17;
        fixture.Session.HandleMessage(credit);
        Assert.Single(_failures); Assert.Equal(0, Reserved());
    }

    [Fact]
    public void Transport_backpressure_keeps_the_same_unsent_chunk_and_spends_no_credit()
    {
        Messaging.BulkSendResult = _ => BulkSendResult.Backpressured;
        var fixture = Start(1, 50000);
        for (int i = 0; i < 10; i++) { FileTransferSession.Tick(); Thread.Sleep(1); }
        Assert.Empty(Chunks(fixture.Session)); Assert.Empty(_failures);
        var credits = (BulkCreditWindow)typeof(FileTransferSession).GetField("_credits", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(fixture.Session);
        Assert.Equal(0UL, credits.SentBytes);
        Messaging.BulkSendResult = _ => BulkSendResult.Sent;
        PumpUntil(() => Completions(fixture.Session).Count == 1);
        Assert.Equal(new ushort[] { 0, 1, 2, 3 }, Chunks(fixture.Session).Select(chunk => chunk.ChunkIndex));
        Ack(fixture.Session, Completions(fixture.Session).Single());
        Assert.True(fixture.Session.IsComplete); Assert.Empty(_failures);
    }

    [Fact]
    public void Retry_uses_a_new_attempt_and_old_attempt_ACK_cannot_commit_it()
    {
        var fixture = Start(1, 50000);
        PumpUntil(() => Completions(fixture.Session).Count == 1);
        var first = Completions(fixture.Session).Single();
        Ack(fixture.Session, first, 1);
        PumpUntil(() => Completions(fixture.Session).Count == 2);
        Ack(fixture.Session, first);
        Assert.False(fixture.Session.IsComplete); Assert.Empty(_failures);
        var retry = Completions(fixture.Session).Last();
        Assert.Equal((byte)1, retry.Attempt);
        Assert.All(Chunks(fixture.Session).Where(chunk => chunk.Attempt == 1), chunk => Assert.Equal(first.Hash, chunk.Hash));
        Ack(fixture.Session, retry);
        Assert.True(fixture.Session.IsComplete); Assert.Equal(1, _completed);
    }

    [Fact]
    public void Ninth_transfer_waits_without_readers_and_is_admitted_after_an_active_owner_is_cancelled()
    {
        var fixtures = Enumerable.Range(0, 9).Select(_ => Start(1, 50000)).ToArray();
        Assert.Equal(8, Reserved());
        Assert.False(FileTransferSession.TryGetBulkSendScope(fixtures[8].Hub.connectionToClient, out _, out _));
        Assert.Empty(Chunks(fixtures[8].Session));
        var queued = Assert.Single(Messaging.Sent.OfType<FileTransferStatusMessage>().Where(message =>
            message.SessionId == fixtures[8].Session.Manifest.SessionId));
        Assert.Equal(FileTransferStatus.Queued, queued.Status);
        Assert.InRange(queued.QueueWaitMilliseconds, (ushort)30001, (ushort)60000);
        typeof(FileTransferSession).GetField("_lastActivityUtc", BindingFlags.NonPublic | BindingFlags.Instance)
            .SetValue(fixtures[8].Session, DateTime.UtcNow.AddSeconds(-31));
        FileTransferSession.Tick();
        Assert.Empty(_failures);
        FileTransferSession.CleanupConnection(fixtures[0].Hub.connectionToClient);
        PumpUntil(() => Chunks(fixtures[8].Session).Count > 0);
        Assert.Contains(Messaging.Sent.OfType<FileTransferStatusMessage>(), message =>
            message.SessionId == fixtures[8].Session.Manifest.SessionId && message.Status == FileTransferStatus.Admitted);
        Assert.Equal(8, Reserved()); Assert.Empty(_failures);
    }

    [Fact]
    public void Immediately_available_slot_does_not_require_any_queue_wait_budget()
    {
        const int bytes = 50000;
        // The inert transport reports100ms full RTT; even a tiny single file
        // must fit its control/ACK round trip before the verification reserve.
        double remaining = Math.Max((double)FileTransferAdmission.AccountedFileRemainder(bytes) /
            BulkTransferBudget.Default.ClientBytesPerSecond, .1) +
            FileTransferAdmission.VerificationReserveSeconds + FileTransferAdmission.ActivationReserveSeconds;
        var fixture = Start(1, bytes, remainingSeconds: () => remaining);
        Assert.True(FileTransferSession.TryGetBulkSendScope(fixture.Hub.connectionToClient, out _, out _));
        Assert.Equal(FileTransferStatus.Admitted, Assert.Single(Messaging.Sent.OfType<FileTransferStatusMessage>()
            .Where(message => message.SessionId == fixture.Session.Manifest.SessionId)).Status);
        Assert.Empty(_failures);
    }

    [Fact]
    public void Cancelled_entries_behind_a_blocked_head_are_pruned_without_changing_live_FIFO_order()
    {
        var active = Enumerable.Range(0, 8).Select(_ => Start(1, 50000)).ToArray();
        var head = Start(1, 50000);
        Assert.Equal(8, Reserved());
        for (int attempt = 0; attempt < 64; attempt++)
        {
            var retired = Start(1, 50000);
            FileTransferSession.CleanupConnection(retired.Hub.connectionToClient);
            AuthenticatedTransportSessions.RemoveConnection(retired.Hub.connectionToClient);
            // One just-retired node can remain until the next admission pass;
            // repeated cancellation must not build an unbounded physical queue.
            Assert.InRange(WaitingEntries(), 1, 2);
        }
        FileTransferSession.Tick();
        Assert.Equal(1, WaitingEntries());
        var tail = Start(1, 50000);
        Assert.Equal(2, WaitingEntries());
        FileTransferSession.CleanupConnection(active[0].Hub.connectionToClient);
        FileTransferSession.Tick();
        Assert.True(FileTransferSession.TryGetBulkSendScope(head.Hub.connectionToClient, out _, out _));
        Assert.False(FileTransferSession.TryGetBulkSendScope(tail.Hub.connectionToClient, out _, out _));
        Assert.Equal(1, WaitingEntries());
        FileTransferSession.CleanupConnection(active[1].Hub.connectionToClient);
        FileTransferSession.Tick();
        Assert.True(FileTransferSession.TryGetBulkSendScope(tail.Hub.connectionToClient, out _, out _));
        Assert.Equal(0, WaitingEntries());
        Assert.Empty(_failures);
    }

    [Fact]
    public void Cancelled_session_work_and_delayed_control_messages_cannot_mutate_its_replacement()
    {
        var first = Start(1, 50000);
        PumpUntil(() => Completions(first.Session).Count == 1);
        var oldAck = Completions(first.Session).Single();
        var replacement = Start(1, 50000, hub: first.Hub);
        Messaging.Sent.Clear();
        Ack(replacement.Session, oldAck);
        replacement.Session.HandleMessage(new FileCreditMessage
        { SessionId = first.Session.Manifest.SessionId, CreditSequence = 1 });
        PumpUntil(() => Completions(replacement.Session).Count == 1);
        Assert.All(Messaging.Sent.OfType<FileChunkMessage>(), chunk => Assert.Equal(replacement.Session.Manifest.SessionId, chunk.SessionId));
        Assert.False(replacement.Session.IsComplete); Assert.Empty(_failures);
        Ack(replacement.Session, Completions(replacement.Session).Single());
        Assert.Equal(1, _completed);
    }

    [Fact]
    public void Missing_link_information_rejects_uncached_work_but_does_not_block_an_empty_cached_request()
    {
        Messaging.BulkLinkMetrics = _ => (0, 0);
        var rejected = Start(1, 50000);
        Assert.Single(_failures);
        Assert.Empty(Chunks(rejected.Session));
        Assert.Equal(0, Reserved());
        Assert.False(rejected.Session.IsComplete);
        var cached = Start(0, 0);
        Assert.True(cached.Session.IsComplete);
        Assert.Equal(1, _completed);
    }

    [Fact]
    public void Queued_request_refreshes_physical_capacity_before_admission_and_sends_no_unachievable_content()
    {
        var active = Enumerable.Range(0, 8).Select(_ => Start(1, 50000)).ToArray();
        var target = new ReferenceHub();
        int rtt = 1;
        Messaging.BulkLinkMetrics = connection => (1024, ReferenceEquals(connection, target.connectionToClient) ? rtt : 100);
        var queued = Start(1, 4 * 1024 * 1024, 1UL << 6, target, () => 30);
        Assert.Empty(_failures);
        Assert.False(FileTransferSession.TryGetBulkSendScope(target.connectionToClient, out _, out _));
        rtt = 500;
        FileTransferSession.CleanupConnection(active[0].Hub.connectionToClient);
        FileTransferSession.Tick();
        Assert.Single(_failures);
        Assert.Contains("deadline", _failures[0]);
        Assert.Empty(Chunks(queued.Session));
        Assert.DoesNotContain(Messaging.Sent.OfType<FileTransferStatusMessage>(), message =>
            message.SessionId == queued.Session.Manifest.SessionId && message.Status == FileTransferStatus.Admitted);
    }

    private (ReferenceHub Hub, FileTransferSession Session) Start(int files, int size,
        ulong mask = ((1UL << 16) - 1) << 6, ReferenceHub hub = null, Func<double> remainingSeconds = null)
    {
        Directory.CreateDirectory(_root);
        hub ??= new ReferenceHub();
        if (!AuthenticatedTransportSessions.IsActiveConnection(hub.connectionToClient))
            AuthenticatedTransportSessions.RegisterConnection(hub.connectionToClient, new PlayerStorage
            {
                UserId = "bulk-tests@anomaly", AcceptedPeer = new object(),
                ClientToServerKey = new byte[32], ServerToClientKey = new byte[32], SessionId = new byte[16], TranscriptHash = new byte[32],
            });
        var snapshot = AssetPackSnapshot.Empty();
        for (int i = 0; i < files; i++)
        {
            byte[] data = Enumerable.Range(0, size).Select(index => (byte)((index + i) % 256)).ToArray();
            byte[] hash;
            using (var sha = SHA256.Create()) hash = sha.ComputeHash(data);
            string path = Path.Combine(_root, Guid.NewGuid().ToString("N")); File.WriteAllBytes(path, data);
            snapshot.Files.Add(Hex(hash), new ServerAssetManager.AssetInfo
            { FullPath = path, RelativePath = Hex(hash) + ".bundle", Hash = hash, Size = (uint)size, Kind = FileContentKind.Bundle });
        }
        var session = FileTransferSession.StartForConnection(hub.connectionToClient, snapshot,
            () => _completed++, reason => _failures.Add(reason), remainingSeconds);
        Assert.True(session.Approve());
        session.HandleMessage(new FileRequestMessage
        { SessionId = session.Manifest.SessionId, BulkLaneMask = mask, RequestedHashes = snapshot.Files.Values.Select(file => file.Hash).ToList() });
        return (hub, session);
    }

    private static List<FileChunkMessage> Chunks(FileTransferSession session) => Messaging.Sent.OfType<FileChunkMessage>()
        .Where(message => message.SessionId == session.Manifest.SessionId).ToList();
    private static List<FileTransferCompleteMessage> Completions(FileTransferSession session) => Messaging.Sent.OfType<FileTransferCompleteMessage>()
        .Where(message => message.SessionId == session.Manifest.SessionId).ToList();
    private static void Ack(FileTransferSession session, FileTransferCompleteMessage message, byte status = 0) =>
        session.HandleMessage(new FileAckMessage { SessionId = message.SessionId, Hash = message.Hash, Attempt = message.Attempt, Status = status });
    private static void PumpUntil(Func<bool> predicate)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (!predicate() && DateTime.UtcNow < until) { FileTransferSession.Tick(); Thread.Sleep(1); }
        Assert.True(predicate(), "Bulk scheduler did not reach the expected bounded state.");
    }
    private static int Reserved() => (int)typeof(FileTransferSession).GetField("_reservedTransferCount", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    private static int WaitingEntries() => ((System.Collections.ICollection)typeof(FileTransferSession)
        .GetField("WaitingTransfers", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null)).Count;
    private static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    public void Dispose()
    {
        FileTransferSession.Reset(); Messaging.ResetForTests();
        if (!Directory.Exists(_root)) return;
        Assert.True(SpinWait.SpinUntil(() =>
        {
            try { Directory.Delete(_root, true); return true; } catch (IOException) { return false; }
        }, 5000));
    }
}
