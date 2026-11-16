using System;
using System.IO;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using Xunit;

namespace Anomaly.Shared.Tests;

public sealed class BulkTransferProtocolTests
{
    private const ulong Lanes = 0xFFFFUL << 6;

    [Fact]
    public void Admission_status_round_trips_and_cannot_extend_an_unbounded_queue()
    {
        var queued = new FileTransferStatusMessage { SessionId = 17, Status = FileTransferStatus.Queued,
            QueueWaitMilliseconds = 60000, QueuePosition = 3 };
        var decoded = RoundTrip(queued, () => new FileTransferStatusMessage());
        Assert.Equal((ushort)60000, decoded.QueueWaitMilliseconds);
        Assert.Equal((byte)3, decoded.QueuePosition);
        Assert.True(ContentBootstrapPolicy.IsServerMessage(decoded));
        Assert.False(ContentBootstrapPolicy.IsClientMessage(decoded));
        queued.QueueWaitMilliseconds = 60001;
        Assert.Throws<InvalidDataException>(() => Serialize(queued));
        queued.QueueWaitMilliseconds = 1; queued.QueuePosition = 0;
        Assert.Throws<InvalidDataException>(() => Serialize(queued));
        queued.Status = FileTransferStatus.Admitted;
        Assert.Throws<InvalidDataException>(() => Serialize(queued));
        queued.QueueWaitMilliseconds = 0;
        Assert.Equal(FileTransferStatus.Admitted, RoundTrip(queued, () => new FileTransferStatusMessage()).Status);
        AnomalyMessageRegistry.RegisterDefaults();
        uint hash = AnomalyMessageRegistry.GetHash(queued.MessageName);
        Assert.True(AnomalyMessageRegistry.TryGetSendContract(queued, AnomalyMessageDirection.ServerToClient,
            out uint actual, out var channel, out _));
        Assert.Equal(hash, actual); Assert.Equal(MessageChannel.ReliableOrdered, channel);
    }

    [Fact]
    public void Physical_pool_intersection_preserves_distinct_append_bases_and_domain_order()
    {
        ulong selected = BulkTransferProtocol.SelectLaneMask(Lanes, 0xFFFFUL << 8);
        Assert.Equal(14, BulkTransferProtocol.DomainCount(selected));
        Assert.Equal(8, BulkTransferProtocol.GetPhysicalChannel(selected, 0));
        Assert.Equal(21, BulkTransferProtocol.GetPhysicalChannel(selected, 13));
        Assert.True(BulkTransferProtocol.IsValidMask(1UL << 63, allowEmpty: false));
        Assert.Equal(63, BulkTransferProtocol.GetPhysicalChannel(1UL << 63, 0));
        Assert.Equal(0UL, BulkTransferProtocol.SelectLaneMask(1UL << 6, 1UL << 7));
        Assert.False(BulkTransferProtocol.IsValidMask(1UL << 5));
        Assert.False(BulkTransferProtocol.IsValidMask((0x1FFFFUL << 6)));
        Assert.False(BulkTransferProtocol.IsValidMask(0, allowEmpty: false));
        Assert.Throws<ArgumentOutOfRangeException>(() => BulkTransferProtocol.GetPhysicalChannel(selected, 14));
    }

    [Fact]
    public void Manifest_profile_round_trip_preserves_transport_offer_without_changing_content_digest()
    {
        var manifest = new FileManifestMessage { SessionId = 42, BulkLaneMask = Lanes };
        manifest.Files.Add(new FileManifestMessage.FileEntry("fixture.bundle", new byte[32], 64, FileContentKind.Bundle));
        manifest.ManifestSha256 = FileManifestMessage.ComputeManifestSha256(manifest.Files);
        var decoded = RoundTrip(manifest, () => new FileManifestMessage());
        Assert.Equal(BulkTransferProtocol.ProfileVersion, decoded.BulkProfileVersion);
        Assert.Equal(Lanes, decoded.BulkLaneMask);
        Assert.Equal(manifest.ManifestSha256, decoded.ManifestSha256);
        manifest.BulkLaneMask = 1UL << 63;
        Assert.Equal(manifest.ManifestSha256, RoundTrip(manifest, () => new FileManifestMessage()).ManifestSha256);
        manifest.BulkLaneMask = 1;
        Assert.Throws<InvalidDataException>(() => Serialize(manifest));
    }

    [Fact]
    public void Cold_request_requires_a_bounded_profile_but_empty_cache_request_needs_no_pool_or_credit()
    {
        var empty = new FileRequestMessage { SessionId = 1, ReceiveWindowBytes = 0, ReceiveWindowFrames = 0 };
        var cached = RoundTrip(empty, () => new FileRequestMessage());
        Assert.Empty(cached.RequestedHashes);
        Assert.Equal(0UL, cached.BulkLaneMask);
        Assert.Equal(0u, cached.ReceiveWindowBytes);
        empty.RequestedHashes.Add(new byte[32]);
        Assert.Throws<InvalidDataException>(() => Serialize(empty));
        empty.BulkLaneMask = Lanes;
        empty.ReceiveWindowBytes = BulkTransferProtocol.MaxWindowBytes;
        empty.ReceiveWindowFrames = BulkTransferProtocol.MaxWindowFrames;
        Assert.Single(RoundTrip(empty, () => new FileRequestMessage()).RequestedHashes);
        empty.ReceiveWindowBytes++;
        Assert.Throws<InvalidDataException>(() => Serialize(empty));
        empty.ReceiveWindowBytes = BulkTransferProtocol.MaxWindowBytes;
        empty.ReceiveWindowFrames++;
        Assert.Throws<InvalidDataException>(() => Serialize(empty));
        empty.ReceiveWindowFrames = BulkTransferProtocol.MaxWindowFrames;
        empty.BulkProfileVersion++;
        Assert.Throws<InvalidDataException>(() => Serialize(empty));
    }

    [Fact]
    public void Chunk_prefix_is_authenticated_domain_metadata_and_full_attempt_round_trips()
    {
        var chunk = new FileChunkMessage { SessionId = 1, Hash = new byte[32], Domain = 15, Attempt = 2,
            ChunkIndex = 0, TotalChunks = 1, Data = new byte[] { 7 } };
        byte[] encoded = Serialize(chunk);
        Assert.True(BulkTransferProtocol.TryReadChunkDomain(encoded, 0, encoded.Length, out byte domain));
        Assert.Equal(15, domain);
        var decoded = RoundTrip(chunk, () => new FileChunkMessage());
        Assert.Equal(2, decoded.Attempt);
        Assert.Equal(15, decoded.Domain);
        Assert.Equal(chunk.Data, decoded.Data);
        Assert.Equal(MessageChannel.ReliableUnordered, decoded.TransportChannel);
        encoded[0]++;
        Assert.False(BulkTransferProtocol.TryReadChunkDomain(encoded, 0, encoded.Length, out _));
        encoded[0]--;
        encoded[1] = 16;
        Assert.False(BulkTransferProtocol.TryReadChunkDomain(encoded, 0, encoded.Length, out _));
        Assert.False(BulkTransferProtocol.TryReadChunkDomain(encoded, int.MaxValue, int.MaxValue, out _));
        Assert.False(BulkTransferProtocol.TryReadChunkDomain(encoded, -1, 3, out _));
        Assert.False(BulkTransferProtocol.TryReadChunkDomain(null, 0, 3, out _));
        chunk.Attempt = 3;
        Assert.Throws<InvalidDataException>(() => Serialize(chunk));
    }

    [Fact]
    public void Completion_and_acknowledgement_bind_retry_attempt()
    {
        var complete = new FileTransferCompleteMessage { SessionId = 1, Hash = new byte[32], Attempt = 2 };
        Assert.Equal(2, RoundTrip(complete, () => new FileTransferCompleteMessage()).Attempt);
        var ack = new FileAckMessage { SessionId = 1, Hash = new byte[32], Attempt = 2, Status = 1 };
        Assert.Equal(2, RoundTrip(ack, () => new FileAckMessage()).Attempt);
        Assert.Equal(1, RoundTrip(ack, () => new FileAckMessage()).Status);
        complete.Attempt = ack.Attempt = 3;
        Assert.Throws<InvalidDataException>(() => Serialize(complete));
        Assert.Throws<InvalidDataException>(() => Serialize(ack));
    }

    [Fact]
    public void Striped_replay_domains_are_independent_and_reject_repeated_or_invalid_counters()
    {
        var state = new BulkReplayState();
        Assert.Equal(1u, state.NextTxCounter(0));
        Assert.Equal(1u, state.NextTxCounter(1));
        Assert.Equal(2u, state.NextTxCounter(0));
        Assert.Equal(0u, state.NextTxCounter(16));
        Assert.Equal(AnomalyReplayState.RxResult.Accepted, state.TryAcceptRx(0, 2, out _));
        Assert.Equal(AnomalyReplayState.RxResult.Accepted, state.TryAcceptRx(0, 1, out _));
        Assert.Equal(AnomalyReplayState.RxResult.Accepted, state.TryAcceptRx(1, 1, out _));
        Assert.Equal(AnomalyReplayState.RxResult.RejectedReplay, state.TryAcceptRx(0, 1, out _));
        Assert.Equal(AnomalyReplayState.RxResult.RejectedInvalid, state.TryAcceptRx(16, 1, out _));
        Assert.Equal(AnomalyReplayState.RxResult.RejectedInvalid, state.TryAcceptRx(0, 0, out _));
        Assert.Equal(AnomalyReplayState.RxResult.RejectedInvalid, state.TryAcceptRx(0, uint.MaxValue, out _));
        Assert.Equal(AnomalyReplayState.RxResult.Accepted, state.TryAcceptRx(0, 200, out _));
        Assert.Equal(AnomalyReplayState.RxResult.RejectedStale, state.TryAcceptRx(0, 2, out _));
    }

    [Fact]
    public void Credits_bound_total_bytes_frames_and_each_domain_until_disk_consumption()
    {
        var credits = new BulkCreditWindow(BulkTransferProtocol.MaxWindowBytes, BulkTransferProtocol.MaxWindowFrames, 16);
        for (int domain = 0; domain < 16; domain++)
        for (int index = 0; index < 8; index++) credits.RecordSent(domain, FileChunkMessage.MaxChunkBytes);
        Assert.Equal((ulong)BulkTransferProtocol.MaxWindowBytes, credits.OutstandingBytes);
        Assert.Equal(128u, credits.OutstandingFrames);
        Assert.False(credits.CanSend(0, 1));
        Assert.Throws<InvalidOperationException>(() => credits.RecordSent(0, 1));
        var update = new FileCreditMessage { SessionId = 1, CreditSequence = 1,
            ReleasedBytes = FileChunkMessage.MaxChunkBytes, ReleasedFrames = new uint[16] };
        update.ReleasedFrames[0] = 1;
        update.ConsumedCounters[0] = 1;
        Assert.True(credits.TryApplyCredit(update, out _));
        Assert.True(credits.CanSend(0, FileChunkMessage.MaxChunkBytes));
        Assert.False(credits.CanSend(1, 1));
        credits.RecordSent(0, FileChunkMessage.MaxChunkBytes);
        Assert.False(credits.CanSend(0, 1));
        Assert.Equal(9u, credits.GetSentFrames(0));
        Assert.Equal(1u, credits.GetReleasedFrames(0));
        Assert.Equal(8u, credits.GetOutstandingFrames(0));
    }

    [Fact]
    public void Duplicate_and_stale_credit_never_double_grants_and_invalid_new_credit_is_atomic()
    {
        var credits = new BulkCreditWindow(BulkTransferProtocol.MaxWindowBytes, 128, 2);
        credits.RecordSent(0, 20);
        credits.RecordSent(1, 30);
        var first = new FileCreditMessage { SessionId = 1, CreditSequence = 1, ReleasedBytes = 20 };
        first.ReleasedFrames[0] = 1;
        first.ConsumedCounters[0] = 1;
        Assert.True(credits.TryApplyCredit(first, out _));
        Assert.True(credits.TryApplyCredit(first, out _));
        Assert.Equal(30ul, credits.OutstandingBytes);
        first.ReleasedFrames[1] = 1;
        first.ReleasedBytes = 50;
        Assert.False(credits.TryApplyCredit(first, out _));
        Assert.Equal(30ul, credits.OutstandingBytes);
        first.CreditSequence = 2;
        Assert.True(credits.TryApplyCredit(first, out _));
        first.CreditSequence = 1;
        first.ReleasedBytes = 20;
        first.ReleasedFrames[1] = 0;
        Assert.True(credits.TryApplyCredit(first, out _));
        Assert.Equal(0ul, credits.OutstandingBytes);
        first.CreditSequence = 3;
        Assert.False(credits.TryApplyCredit(first, out _));
        Assert.Equal(50ul, credits.ReleasedBytes);
        Assert.Equal(2u, credits.ReleasedFrames);
        first.ReleasedBytes = 51;
        first.ReleasedFrames[1] = 1;
        Assert.False(credits.TryApplyCredit(first, out _));
        Assert.Equal(50ul, credits.ReleasedBytes);
    }

    [Fact]
    public void Credit_wire_is_fixed_bounded_and_closed_to_bootstrap_client_direction()
    {
        var update = new FileCreditMessage { SessionId = 42, CreditSequence = 3, ReleasedBytes = 17 };
        update.ReleasedFrames[15] = 1;
        update.ConsumedCounters[15] = 1;
        var decoded = RoundTrip(update, () => new FileCreditMessage());
        Assert.Equal(update.MaxPayloadBytes, Serialize(update).Length);
        Assert.Equal(update.ReleasedFrames, decoded.ReleasedFrames);
        Assert.Equal(update.ConsumedCounters, decoded.ConsumedCounters);
        Assert.True(ContentBootstrapPolicy.IsClientMessage(decoded));
        Assert.False(ContentBootstrapPolicy.IsServerMessage(decoded));
        Assert.False(ContentBootstrapPolicy.IsClientMessage(new DerivedCredit()));
        AnomalyMessageRegistry.RegisterDefaults();
        uint hash = AnomalyMessageRegistry.GetHash(update.MessageName);
        Assert.True(AnomalyMessageRegistry.TryCreate(hash, out var message));
        Assert.IsType<FileCreditMessage>(message);
        update.ReleasedFrames = new uint[17];
        Assert.Throws<InvalidDataException>(() => Serialize(update));
        update.ReleasedFrames = new uint[16];
        update.ReleasedBytes = 1;
        Assert.Throws<InvalidDataException>(() => Serialize(update));
    }

    private sealed class DerivedCredit : FileCreditMessage { }

    [Fact]
    public void Bulk_receive_allowances_are_separate_and_bounded_before_and_after_decryption()
    {
        var bulk = AnomalyInboundRateLimiter.CreateBulkTraffic();
        Assert.True(bulk.TryAccept(1, 2 * 1024 * 1024).Allowed);
        Assert.False(bulk.TryAccept(1, 2 * 1024 * 1024).Allowed);
        var raw = AnomalyInboundRateLimiter.CreateClientBulkRaw();
        for (int index = 0; index < 5; index++) Assert.True(raw.TryAccept(1, 2 * 1024 * 1024).Allowed);
        Assert.False(raw.TryAccept(1, 2 * 1024 * 1024).Allowed);
        Assert.False(raw.TryAccept(1, AnomalyChannel.MaxEnvelopeBytes + 1).Allowed);
        var ordinary = new AnomalyInboundRateLimiter();
        for (int index = 0; index < 4; index++) Assert.True(ordinary.TryAccept(1, 2 * 1024 * 1024).Allowed);
    }

    private static byte[] Serialize(IAnomalyMessage message)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        message.Serialize(writer);
        writer.Flush();
        return stream.ToArray();
    }

    private static T RoundTrip<T>(IAnomalyMessage message, Func<T> create) where T : IAnomalyMessage
    {
        byte[] bytes = Serialize(message);
        var copy = create();
        using var reader = new BinaryReader(new MemoryStream(bytes));
        copy.Deserialize(reader);
        Assert.Equal(reader.BaseStream.Length, reader.BaseStream.Position);
        return copy;
    }
}
