using System.Reflection;
using Anomaly.Server.Auth;
using Anomaly.Server.Networking;
using Anomaly.Shared.Auth;
using Anomaly.Shared.Networking;
using Mirror;
using Xunit;
using ClientEvents = Anomaly.Server.Api.Events.Handlers.ClientEvents;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Anomaly.Server.Tests;

public sealed class MessageDispatchAdmissionTests : IDisposable
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QuarantineStopsLaterInternalAndPublicMessageSubscribers(bool throwAfterQuarantine)
    {
        var hub = CreateHub();
        int first = 0, later = 0;
        Action<ReferenceHub, IAnomalyMessage> revoke = (sender, _) =>
        {
            first++;
            AuthenticatedTransportSessions.Quarantine(sender);
            if (throwAfterQuarantine) throw new InvalidOperationException("handler failed after revocation");
        };
        Action<ReferenceHub, IAnomalyMessage> observe = (_, __) => later++;
        AnomalyMessaging.MessageReceived += revoke;
        AnomalyMessaging.MessageReceived += observe;
        try
        {
            AnomalyMessaging.OnReceived(hub, Payload(), encryptedAndAuthenticated: true, localTransport: false);
            Assert.Equal(1, first);
            Assert.Equal(0, later);
            Assert.Equal(0, ClientEvents.Messages);
            Assert.False(AuthenticatedTransportSessions.IsActive(hub));
        }
        finally
        {
            AnomalyMessaging.MessageReceived -= revoke;
            AnomalyMessaging.MessageReceived -= observe;
        }
    }

    [Fact]
    public void ExceptionWithoutRevocationAllowsLaterInternalAndPublicSubscribers()
    {
        var hub = CreateHub();
        int later = 0;
        Action<ReferenceHub, IAnomalyMessage> fail = (_, __) => throw new InvalidOperationException("isolated handler");
        Action<ReferenceHub, IAnomalyMessage> observe = (_, __) => later++;
        AnomalyMessaging.MessageReceived += fail;
        AnomalyMessaging.MessageReceived += observe;
        try
        {
            AnomalyMessaging.OnReceived(hub, Payload(), encryptedAndAuthenticated: true, localTransport: false);
            Assert.Equal(1, later);
            Assert.Equal(1, ClientEvents.Messages);
        }
        finally
        {
            AnomalyMessaging.MessageReceived -= fail;
            AnomalyMessaging.MessageReceived -= observe;
        }
    }

    [Fact]
    public void ReadySubscriberQuarantineStopsLaterReadyAndConnectedEvents()
    {
        var hub = CreateHub();
        int later = 0;
        Action<ReferenceHub> revoke = AuthenticatedTransportSessions.Quarantine;
        Action<ReferenceHub> observe = _ => later++;
        AnomalyMessaging.ClientReady += revoke;
        AnomalyMessaging.ClientReady += observe;
        try
        {
            AnomalyMessaging.RaiseClientReady(hub);
            Assert.Equal(0, later);
            Assert.Equal(0, ClientEvents.Connections);
        }
        finally
        {
            AnomalyMessaging.ClientReady -= revoke;
            AnomalyMessaging.ClientReady -= observe;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BootstrapRejectionRevokesBeforeDisconnectAndRetainsOnlyUnresolvedConnection(bool disconnectThrows)
    {
        var connection = new NetworkConnectionToClient { ThrowOnDisconnect = disconnectThrows };
        var player = CreatePlayer();
        player.EnrollmentReservation = EnrollmentSessionRegistry.Reserve(player.UserId, false);
        AuthenticatedTransportSessions.RegisterConnection(connection, player);
        bool disconnectObserved = false, activeAtDisconnect = true, encryptionAtDisconnect = true;
        int reservationsAtDisconnect = -1;
        connection.BeforeDisconnect = () =>
        {
            disconnectObserved = true;
            activeAtDisconnect = AuthenticatedTransportSessions.IsActiveConnection(connection);
            encryptionAtDisconnect = AuthenticatedTransportSessions.TryEncryptToConnection(connection, new byte[] { 1 }, out _);
            reservationsAtDisconnect = EnrollmentSessionRegistry.Count;
        };

        // There is deliberately no NetworkServer registry or disconnect callback in this test.
        typeof(ConnectionBootstrap).GetMethod("Reject", BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, new object[] { connection, "test bootstrap rejection" });

        Assert.True(disconnectObserved);
        Assert.False(activeAtDisconnect);
        Assert.False(encryptionAtDisconnect);
        Assert.Equal(0, reservationsAtDisconnect);
        Assert.Equal(!disconnectThrows, connection.Disconnected);
        Assert.Equal(disconnectThrows, AuthenticatedTransportSessions.SnapshotConnections().Contains(connection));
        Assert.False(AuthenticatedTransportSessions.TryAdoptConnection(new ReferenceHub { connectionToClient = connection }, out _));
    }

    private static ReferenceHub CreateHub()
    {
        var hub = new ReferenceHub();
        AuthenticatedTransportSessions.Register(hub, CreatePlayer());
        return hub;
    }

    private static PlayerStorage CreatePlayer() => new()
    {
        UserId = "dispatch-test@anomaly",
        AcceptedPeer = new object(),
        ClientToServerKey = Enumerable.Repeat((byte)0x11, 32).ToArray(),
        ServerToClientKey = Enumerable.Repeat((byte)0x22, 32).ToArray(),
        SessionId = Enumerable.Repeat((byte)0x33, AuthTransportCrypto.SessionIdSize).ToArray(),
        TranscriptHash = Enumerable.Repeat((byte)0x44, AuthTransportCrypto.TranscriptHashSize).ToArray(),
    };

    private static byte[] Payload()
    {
        AnomalyMessageRegistry.Register("test:dispatch", () => new DispatchMessage());
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(1U);
        writer.Write(AnomalyMessageRegistry.GetHash("test:dispatch"));
        return stream.ToArray();
    }

    public void Dispose()
    {
        foreach (var hub in AuthenticatedTransportSessions.SnapshotHubs()) AuthenticatedTransportSessions.Remove(hub);
        foreach (var connection in AuthenticatedTransportSessions.SnapshotConnections()) AuthenticatedTransportSessions.RemoveConnection(connection);
        AnomalyReplayTable.Reset();
        EnrollmentSessionRegistry.Reset();
        ClientEvents.Messages = 0;
        ClientEvents.Connections = 0;
    }

    private sealed class DispatchMessage : IAnomalyMessage, IDirectionalAnomalyMessage, IAuthPhaseAnomalyMessage, IBoundedAnomalyMessage
    {
        public string MessageName => "test:dispatch";
        public MessageChannel TransportChannel => MessageChannel.ReliableOrdered;
        public AnomalyMessageDirection Direction => AnomalyMessageDirection.ClientToServer;
        public AnomalyMessageAuthPhase AuthPhase => AnomalyMessageAuthPhase.AuthenticatedSession;
        public int MaxPayloadBytes => 0;
        public void Serialize(BinaryWriter writer) { }
        public void Deserialize(BinaryReader reader) { }
    }
}
