using Anomaly.Server.Auth;
using Anomaly.Server.Networking;
using Anomaly.Shared.Auth;
using Anomaly.Shared.Networking;
using Mirror.LiteNetLib4Mirror;
using Xunit;

namespace Anomaly.Server.Tests;

public sealed class MessagePayloadBudgetTests : IDisposable
{
    private static readonly byte[] ServerKey = Enumerable.Repeat((byte)0x22, 32).ToArray();
    private static readonly byte[] SessionId = Enumerable.Repeat((byte)0x33, 16).ToArray();
    private static readonly byte[] TranscriptHash = Enumerable.Repeat((byte)0x44, 32).ToArray();

    [Fact]
    public void Budget_uses_the_selected_recipient_without_serializing_or_advancing_counters()
    {
        var first = CreateHub(1, 1400);
        var second = CreateHub(2, 1000);
        var contract = new BudgetMessage { ThrowDuringSerialization = true };

        Assert.True(AnomalyMessaging.TryGetPayloadBudget(first, contract, out var firstBudget));
        Assert.True(AnomalyMessaging.TryGetPayloadBudget(second, contract, out var secondBudget));
        Assert.Equal(1294, firstBudget.MaxUnfragmentedPayloadBytes);
        Assert.Equal(894, secondBudget.MaxUnfragmentedPayloadBytes);
        Assert.Equal(0, contract.SerializeCalls);
        Assert.Empty(first.connectionToClient.Sent);
        Assert.Empty(second.connectionToClient.Sent);
        Assert.Equal(1U, AnomalyReplayTable.GetOrCreate(first).NextTxCounter(MessageChannel.Unreliable));
        Assert.False(first.connectionToClient.Disconnected);
        Assert.True(AuthenticatedTransportSessions.IsActive(first));
    }

    [Fact]
    public void Optional_send_fits_actual_recipient_and_preserves_authentication()
    {
        var hub = CreateHub(1, 1200);
        var contract = new BudgetMessage();
        Assert.True(AnomalyMessaging.TryGetPayloadBudget(hub, contract, out var budget));
        contract.PayloadBytes = budget.MaxPayloadBytes;
        Assert.True(AnomalyMessaging.TrySendOptionalToClient(hub, contract));
        var peer = LiteNetLib4MirrorServer.Peers[1];
        byte[] packet = Assert.Single(peer.SentPackets);
        Assert.True(packet.Length <= peer.GetMaxSinglePacketSize(LiteNetLib.DeliveryMethod.Unreliable));
        // The test's inert Mirror boundary adds a 12-byte batch and 7-byte message prefix.
        byte[] encrypted = packet.Skip(19).ToArray();
        Assert.Empty(hub.connectionToClient.Sent);
        Assert.True(AuthTransportCrypto.IsVersion2Envelope(encrypted));
        byte[] plaintext = AuthTransportCrypto.Decrypt(ServerKey, SessionId, TranscriptHash,
            AuthTransportDirection.ServerToClient, encrypted);
        Assert.Equal(contract.PayloadBytes + TransportPacketPolicy.AnomalyEnvelopeBytes, plaintext.Length);

        contract.PayloadBytes++;
        Assert.False(AnomalyMessaging.TrySendOptionalToClient(hub, contract));
        Assert.Single(peer.SentPackets);
        Assert.False(hub.connectionToClient.Disconnected);
        Assert.True(AuthenticatedTransportSessions.IsActive(hub));
        Assert.Equal(2U, AnomalyReplayTable.GetOrCreate(hub).NextTxCounter(MessageChannel.Unreliable));
    }

    [Fact]
    public void Nonfragmenting_messages_use_separate_batches_but_reliable_controls_keep_native_fifo()
    {
        var hub = CreateHub(1, 1200);
        Assert.True(AnomalyMessaging.TrySendOptionalToClient(hub, new BudgetMessage { PayloadBytes = 700 }));
        Assert.True(AnomalyMessaging.TrySendOptionalToClient(hub, new BudgetMessage { PayloadBytes = 700 }));
        Assert.Equal(2, LiteNetLib4MirrorServer.Peers[1].SentPackets.Count);
        Assert.Empty(hub.connectionToClient.Sent);

        AnomalyMessageRegistry.Register("test:payload-control", () => new ControlMessage());
        Assert.True(AnomalyMessaging.TrySendOptionalToClient(hub, new ControlMessage()));
        Assert.Single(hub.connectionToClient.Sent);
        Assert.Equal(2, LiteNetLib4MirrorServer.Peers[1].SentPackets.Count);
    }

    [Fact]
    public void Optional_transport_rejection_does_not_disconnect()
    {
        var hub = CreateHub(1, 1200);
        LiteNetLib4MirrorServer.Peers[1].AcceptSends = false;
        Assert.False(AnomalyMessaging.TrySendOptionalToClient(hub, new BudgetMessage()));
        Assert.True(AuthenticatedTransportSessions.IsActive(hub));
        Assert.False(hub.connectionToClient.Disconnected);
        Assert.Empty(LiteNetLib4MirrorServer.Peers[1].SentPackets);
    }

    [Fact]
    public void Budget_also_honors_a_smaller_Mirror_admission_ceiling()
    {
        var hub = CreateHub(1, 1400);
        LiteNetLib4MirrorTransport.Singleton.MaximumPacketBytes = 1000;
        Assert.True(AnomalyMessaging.TryGetPayloadBudget(hub, new BudgetMessage(), out var budget));
        Assert.Equal(895, budget.MaxPayloadBytes);
        Assert.Equal(895, budget.MaxUnfragmentedPayloadBytes);
    }

    [Fact]
    public void Optional_send_rechecks_MTU_after_serialization_without_consuming_a_counter()
    {
        var hub = CreateHub(1, 1200);
        var contract = new BudgetMessage
        {
            PayloadBytes = 900,
            DuringSerialization = () => LiteNetLib4MirrorServer.Peers[1].Mtu = 800,
        };
        Assert.False(AnomalyMessaging.TrySendOptionalToClient(hub, contract));
        Assert.Empty(LiteNetLib4MirrorServer.Peers[1].SentPackets);
        Assert.Equal(1U, AnomalyReplayTable.GetOrCreate(hub).NextTxCounter(MessageChannel.Unreliable));
        Assert.False(hub.connectionToClient.Disconnected);
        Assert.True(AuthenticatedTransportSessions.IsActive(hub));
    }

    [Fact]
    public void Optional_send_rejects_congestion_missing_peer_and_lost_auth_without_disconnect()
    {
        var hub = CreateHub(1, 1200);
        var contract = new BudgetMessage();
        LiteNetLib4MirrorServer.Peers[1].ReliableQueuedPackets = 128;
        Assert.False(AnomalyMessaging.TrySendOptionalToClient(hub, contract));
        Assert.Equal(0, contract.SerializeCalls);
        LiteNetLib4MirrorServer.Peers.Clear();
        Assert.False(AnomalyMessaging.TryGetPayloadBudget(hub, contract, out _));
        Assert.False(AnomalyMessaging.TrySendOptionalToClient(hub, contract));
        Assert.Equal(1U, AnomalyReplayTable.GetOrCreate(hub).NextTxCounter(MessageChannel.Unreliable));
        Assert.False(hub.connectionToClient.Disconnected);

        LiteNetLib4MirrorServer.Peers[1] = new Peer();
        AuthenticatedTransportSessions.Remove(hub);
        Assert.False(AnomalyMessaging.TryGetPayloadBudget(hub, contract, out _));
        Assert.False(AnomalyMessaging.TrySendOptionalToClient(hub, contract));
        Assert.Empty(hub.connectionToClient.Sent);
        Assert.False(hub.connectionToClient.Disconnected);
    }

    [Fact]
    public void Optional_serialization_failure_isolated_but_required_oversize_still_disconnects()
    {
        var hub = CreateHub(1, 1200);
        Assert.False(AnomalyMessaging.TrySendOptionalToClient(hub, new BudgetMessage { ThrowDuringSerialization = true }));
        Assert.False(hub.connectionToClient.Disconnected);
        Assert.False(AnomalyMessaging.TrySendToClient(hub, new BudgetMessage { PayloadBytes = 1200 }));
        Assert.True(hub.connectionToClient.Disconnected);
        Assert.False(AuthenticatedTransportSessions.IsActive(hub));
    }

    [Fact]
    public void Budget_rejects_mutated_contract_dummy_and_disconnected_peer()
    {
        var hub = CreateHub(1, 1200);
        Assert.False(AnomalyMessaging.TryGetPayloadBudget(hub,
            new BudgetMessage { TransportChannel = MessageChannel.ReliableOrdered }, out _));
        Assert.False(AnomalyMessaging.TryGetPayloadBudget(hub, null, out _));
        LiteNetLib4MirrorServer.Peers[1].ConnectionState = LiteNetLib.ConnectionState.Disconnected;
        Assert.False(AnomalyMessaging.TryGetPayloadBudget(hub, new BudgetMessage(), out _));
        hub.IsDummy = true;
        Assert.False(AnomalyMessaging.TryGetPayloadBudget(hub, new BudgetMessage(), out _));
        Assert.False(hub.connectionToClient.Disconnected);
    }

    private static ReferenceHub CreateHub(int connectionId, int mtu)
    {
        AnomalyMessageRegistry.Register("test:payload-budget", () => new BudgetMessage());
        var hub = new ReferenceHub();
        hub.connectionToClient.connectionId = connectionId;
        LiteNetLib4MirrorServer.Peers[connectionId] = new Peer { Mtu = mtu };
        AuthenticatedTransportSessions.Register(hub, new PlayerStorage
        {
            UserId = "budget-test@anomaly",
            AcceptedPeer = new object(),
            ClientToServerKey = Enumerable.Repeat((byte)0x11, 32).ToArray(),
            ServerToClientKey = (byte[])ServerKey.Clone(),
            SessionId = (byte[])SessionId.Clone(),
            TranscriptHash = (byte[])TranscriptHash.Clone(),
        });
        return hub;
    }

    public void Dispose()
    {
        foreach (var hub in AuthenticatedTransportSessions.SnapshotHubs()) AuthenticatedTransportSessions.Remove(hub);
        foreach (var connection in AuthenticatedTransportSessions.SnapshotConnections()) AuthenticatedTransportSessions.RemoveConnection(connection);
        LiteNetLib4MirrorServer.Peers.Clear();
        LiteNetLib4MirrorTransport.Singleton.MaximumPacketBytes = int.MaxValue;
        AnomalyReplayTable.Reset();
    }

    private sealed class BudgetMessage : AuthenticatedServerToClientMessage, IAnomalyMessage
    {
        public string MessageName => "test:payload-budget";
        public MessageChannel TransportChannel { get; set; } = MessageChannel.Unreliable;
        public override int MaxPayloadBytes => 2000;
        public int PayloadBytes { get; set; }
        public int SerializeCalls { get; private set; }
        public bool ThrowDuringSerialization { get; set; }
        public Action DuringSerialization { get; set; }
        public void Serialize(BinaryWriter writer)
        {
            SerializeCalls++;
            if (ThrowDuringSerialization) throw new InvalidOperationException("Serializer must not run in a budget query.");
            DuringSerialization?.Invoke();
            writer.Write(new byte[PayloadBytes]);
        }
        public void Deserialize(BinaryReader reader) { }
    }

    private sealed class ControlMessage : AuthenticatedServerToClientMessage, IAnomalyMessage
    {
        public string MessageName => "test:payload-control";
        public MessageChannel TransportChannel => MessageChannel.ReliableOrdered;
        public override int MaxPayloadBytes => 0;
        public void Serialize(BinaryWriter writer) { }
        public void Deserialize(BinaryReader reader) { }
    }
}
