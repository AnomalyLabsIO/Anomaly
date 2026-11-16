using System.Security.Cryptography;
using Anomaly.Server.Auth;
using Anomaly.Server.FileTransfer;
using Anomaly.Server.Networking;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Auth;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using HarmonyLib;
using LiteNetLib;
using Mirror;
using Mirror.LiteNetLib4Mirror;
using Xunit;

namespace Anomaly.Server.Tests;

public sealed class BulkSendCounterTests : IDisposable
{
    private static Action _afterEncryption;
    private readonly string _path = Path.GetTempFileName();
    private readonly NetworkConnectionToClient _connection = new() { connectionId = 77 };
    private readonly PlayerStorage _player = new() { UserId = "counter-tests@anomaly", AcceptedPeer = new object(),
        ClientToServerKey = new byte[32], ServerToClientKey = new byte[32], SessionId = new byte[16], TranscriptHash = new byte[32] };
    private readonly Peer _peer = new();
    private readonly FileChunkMessage _chunk;

    public BulkSendCounterTests()
    {
        FileTransferSession.Reset();
        AnomalyMessageRegistry.RegisterDefaults();
        BulkTransportRuntime.Disable();
        var transport = LiteNetLib4MirrorTransport.Singleton;
        transport.channels = new[] { DeliveryMethod.ReliableOrdered, DeliveryMethod.Unreliable,
            DeliveryMethod.Sequenced, DeliveryMethod.ReliableSequenced, DeliveryMethod.ReliableUnordered,
            DeliveryMethod.ReliableOrdered };
        Transport.active = transport;
        LiteNetLib4MirrorCore.Host = new NetManager { ChannelsCount = 6 };
        LiteNetLib4MirrorServer.Peers.Clear(); LiteNetLib4MirrorServer.Peers[77] = _peer;
        BulkTransportRuntime.Initialize();
        AuthenticatedTransportSessions.RegisterConnection(_connection, _player);
        var snapshot = AssetPackSnapshot.Empty();
        byte[] bytes = { 7 }; File.WriteAllBytes(_path, bytes);
        byte[] hash; using (var sha = SHA256.Create()) hash = sha.ComputeHash(bytes);
        snapshot.Files.Add(ServerAssetManager.ToHex(hash), new ServerAssetManager.AssetInfo
        { FullPath = _path, RelativePath = "fixture.bundle", Hash = hash, Size = 1, Kind = FileContentKind.Bundle });
        var session = FileTransferSession.StartForConnection(_connection, snapshot, () => { }, _ => { });
        Assert.True(session.Approve());
        session.HandleMessage(new FileRequestMessage { SessionId = session.Manifest.SessionId,
            BulkLaneMask = 1UL << 6, RequestedHashes = new List<byte[]> { hash } });
        Assert.True(FileTransferSession.TryGetBulkSendScope(_connection, out _, out _));
        _chunk = new FileChunkMessage { SessionId = session.Manifest.SessionId, Domain = 0, Hash = hash,
            TotalChunks = 1, Data = bytes };
    }

    [Fact]
    public void Backpressure_after_encryption_does_not_spend_a_domain_counter()
    {
        var harmony = new Harmony("anomaly.tests.bulk.counterbackpressure");
        _afterEncryption = () => _peer.QueuedByChannel[6] = 64;
        try
        {
            harmony.Patch(AccessTools.Method(typeof(AuthenticatedTransportSessions), "TryEncryptToConnection"),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(BulkSendCounterTests), nameof(AfterEncryption))));
            Assert.Equal(BulkSendResult.Backpressured, AnomalyMessaging.TrySendBulkChunkToConnection(_connection, _chunk, 0));
            Assert.Equal(0u, AuthenticatedTransportSessions.GetBulkReplayState(_connection).GetTxCounterBases()[0]);
        }
        finally { harmony.UnpatchAll(harmony.Id); _afterEncryption = null; }
        _peer.QueuedByChannel[6] = 0;
        Assert.Equal(BulkSendResult.Sent, AnomalyMessaging.TrySendBulkChunkToConnection(_connection, _chunk, 0));
        Assert.Equal(1u, _chunk.FrameCounter);
        byte[] plain = AuthTransportCrypto.Decrypt(new byte[32], new byte[16], new byte[32],
            AuthTransportDirection.ServerToClient, _connection.Sent.Last().Data);
        Assert.Equal(AnomalyChannel.ChannelId, plain[0]);
        Assert.Equal(1u, BitConverter.ToUInt32(plain, 1));
    }

    [Fact]
    public void Send_exception_after_commit_quarantines_keys_instead_of_allowing_a_counter_hole_to_continue()
    {
        _connection.ThrowOnSend = true;
        Assert.Equal(BulkSendResult.Failed, AnomalyMessaging.TrySendBulkChunkToConnection(_connection, _chunk, 0));
        Assert.True(_connection.Disconnected);
        Assert.False(AuthenticatedTransportSessions.IsActiveConnection(_connection));
        Assert.False(AuthenticatedTransportSessions.TryEncryptToConnection(_connection, new byte[] { 1 }, out _));
    }

    private static void AfterEncryption() => _afterEncryption?.Invoke();

    public void Dispose()
    {
        FileTransferSession.Reset(); AuthenticatedTransportSessions.Reset(); BulkTransportRuntime.Disable();
        Assert.True(SpinWait.SpinUntil(() => { try { File.Delete(_path); return true; } catch (IOException) { return false; } }, 5000));
    }
}
