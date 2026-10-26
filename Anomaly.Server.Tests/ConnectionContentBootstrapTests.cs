using System.Reflection;
using Anomaly.Server.Auth;
using Anomaly.Server.FileTransfer;
using Anomaly.Server.Networking;
using Anomaly.Shared.Auth;
using Anomaly.Shared.Content;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using Mirror;
using Xunit;

namespace Anomaly.Server.Tests;

public sealed class ConnectionContentBootstrapTests : IDisposable
{
    private static readonly byte[] ClientKey = Enumerable.Repeat((byte)0x11, 32).ToArray();
    private static readonly byte[] ServerKey = Enumerable.Repeat((byte)0x22, 32).ToArray();
    private static readonly byte[] SessionId = Enumerable.Repeat((byte)0x33, 16).ToArray();
    private static readonly byte[] Transcript = Enumerable.Repeat((byte)0x44, 32).ToArray();

    [Fact]
    public void ExactConsentCacheRequestAndActivationCompleteBeforePlayerReadiness()
    {
        var connection = Start();
        Assert.False(connection.isReady);
        Assert.False(ConnectionContentBootstrap.IsComplete(connection));
        var offer = Offer(connection);
        Send(connection, Decision(offer.Catalog), 1);
        Send(connection, new FileRequestMessage { SessionId = offer.Manifest.SessionId }, 2);
        Assert.False(ConnectionContentBootstrap.IsComplete(connection));
        Assert.DoesNotContain(Messages(connection), message => message is ContentBootstrapReadyMessage);
        Send(connection, Activation(offer.Catalog), 3);
        Assert.True(ConnectionContentBootstrap.IsComplete(connection));
        Assert.False(connection.isReady);
        Assert.True(ConnectionContentBootstrap.RequireComplete(connection));
        var ready = Assert.Single(Messages(connection).OfType<ContentBootstrapReadyMessage>());
        Assert.Equal(offer.Manifest.SessionId, ready.SessionId);
        Assert.Equal(offer.Catalog.CatalogSha256, ready.CatalogSha256);
        Assert.Equal(offer.Manifest.ManifestSha256, ready.ManifestSha256);
        Assert.True(AuthenticatedTransportSessions.TryAdoptConnection(new ReferenceHub { connectionToClient = connection }, out _));
    }

    [Theory]
    [InlineData("decline")]
    [InlineData("request-before-consent")]
    [InlineData("activation-before-request")]
    [InlineData("wrong-catalog")]
    [InlineData("wrong-session")]
    [InlineData("failed-activation")]
    public void InvalidRequiredContentResponseRevokesAndDisconnectsOnce(string violation)
    {
        var connection = Start();
        var offer = Offer(connection);
        uint counter = 1;
        if (violation == "activation-before-request" || violation == "wrong-session" || violation == "failed-activation")
            Send(connection, Decision(offer.Catalog), counter++);
        IAnomalyMessage invalid;
        switch (violation)
        {
            case "decline":
                var declined = Decision(offer.Catalog); declined.Decision = ContentCatalogDecision.Declined; invalid = declined; break;
            case "request-before-consent": invalid = new FileRequestMessage { SessionId = offer.Manifest.SessionId }; break;
            case "activation-before-request": invalid = Activation(offer.Catalog); break;
            case "wrong-catalog":
                var wrong = Decision(offer.Catalog); wrong.CatalogSha256 = Enumerable.Repeat((byte)0x99, 32).ToArray(); invalid = wrong; break;
            case "wrong-session": invalid = new FileRequestMessage { SessionId = offer.Manifest.SessionId + 1 }; break;
            default:
                Send(connection, new FileRequestMessage { SessionId = offer.Manifest.SessionId }, counter++);
                var failed = Activation(offer.Catalog); failed.Status = ContentCatalogActivationStatus.Failed; invalid = failed; break;
        }
        bool keysAtDisconnect = true;
        connection.BeforeDisconnect = () => keysAtDisconnect = AuthenticatedTransportSessions.IsActiveConnection(connection);
        Send(connection, invalid, counter);
        Assert.False(keysAtDisconnect);
        Assert.True(connection.Disconnected);
        Assert.False(ConnectionContentBootstrap.IsComplete(connection));
        Send(connection, invalid, counter + 1);
        Assert.Equal(1, connection.DisconnectCount);
        Assert.DoesNotContain(Messages(connection), message => message is ContentBootstrapReadyMessage);
    }

    [Theory]
    [InlineData("unsecured")]
    [InlineData("trailing")]
    [InlineData("unknown")]
    [InlineData("replay")]
    [InlineData("wrong-direction")]
    public void PrePlayerDispatchHasClosedAuthenticatedContract(string violation)
    {
        var connection = Start();
        var offer = Offer(connection);
        var message = Decision(offer.Catalog);
        byte[] data = Plaintext(message, 1);
        if (violation == "trailing") data = data.Concat(new byte[] { 1 }).ToArray();
        if (violation == "unknown") BitConverter.GetBytes(0x12345678U).CopyTo(data, 5);
        if (violation == "wrong-direction") data = Plaintext(new ContentBootstrapReadyMessage { SessionId = 123 }, 1);
        if (violation == "replay") Send(connection, message, 1);
        var packed = violation == "unsecured"
            ? new EncryptedChannelManager.EncryptedMessageOutside(EncryptedChannelManager.SecurityLevel.Unsecured, data)
            : Pack(data);
        ConnectionContentBootstrap.HandlePacked(connection, packed);
        Assert.True(connection.Disconnected);
        Assert.False(AuthenticatedTransportSessions.IsActiveConnection(connection));
    }

    [Fact]
    public void PrematureReadyAndLowerLevelSpawnGuardsDenyAndRevoke()
    {
        foreach (string name in new[] { "ReadyMessagePatch", "AddPlayerMessagePatch", "SetReadyPatch", "AddPlayerPatch" })
        {
            var connection = Start();
            var target = typeof(ContentReadinessPatches).GetNestedType(name, BindingFlags.NonPublic);
            var method = target.GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static);
            object[] arguments = name == "AddPlayerPatch" ? new object[] { connection, true } : new object[] { connection };
            Assert.False((bool)method.Invoke(null, arguments));
            Assert.True(connection.Disconnected);
            Assert.False(AuthenticatedTransportSessions.IsActiveConnection(connection));
            if (name == "AddPlayerPatch") Assert.False((bool)arguments[1]);
        }
    }

    [Fact]
    public void SuccessorGenerationRevokesOldApprovalAndUsesNewSessionWithoutReofferOnAdoption()
    {
        var connection = Start();
        var first = Offer(connection);
        Send(connection, Decision(first.Catalog), 1);
        Send(connection, new FileRequestMessage { SessionId = first.Manifest.SessionId }, 2);
        Send(connection, Activation(first.Catalog), 3);
        Assert.True(ConnectionContentBootstrap.IsComplete(connection));
        ConnectionContentBootstrap.OnGenerationStarted();
        Assert.False(ConnectionContentBootstrap.IsComplete(connection));
        int count = connection.Sent.Count;
        MEC.Timing.Advance(); MEC.Timing.Advance();
        Assert.Equal(count, connection.Sent.Count);
        ConnectionContentBootstrap.OnSnapshotDelivered(connection, 2);
        var second = Offer(connection);
        Assert.NotEqual(first.Manifest.SessionId, second.Manifest.SessionId);
        Assert.False(ConnectionContentBootstrap.IsComplete(connection));
        Send(connection, new FileRequestMessage { SessionId = first.Manifest.SessionId }, 4);
        Assert.True(connection.Disconnected);
    }

    [Fact]
    public void FirstPendingFacilityDoesNotDiscardItsEarlyOffer()
    {
        var connection = Start(deliverGeneration: false);
        var offer = Offer(connection);
        Send(connection, Decision(offer.Catalog), 1);
        Send(connection, new FileRequestMessage { SessionId = offer.Manifest.SessionId }, 2);
        Send(connection, Activation(offer.Catalog), 3);
        Assert.False(ConnectionContentBootstrap.IsComplete(connection));
        int count = connection.Sent.Count;
        ConnectionContentBootstrap.OnSnapshotDelivered(connection, 1);
        Assert.True(ConnectionContentBootstrap.IsComplete(connection));
        Assert.Equal(count, connection.Sent.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ContentCompleteButFacilityPendingStillExpires(bool lateFacilityDelivery)
    {
        var connection = Start(deliverGeneration: false);
        var offer = Offer(connection);
        Send(connection, Decision(offer.Catalog), 1);
        Send(connection, new FileRequestMessage { SessionId = offer.Manifest.SessionId }, 2);
        Send(connection, Activation(offer.Catalog), 3);
        Assert.False(ConnectionContentBootstrap.IsComplete(connection));
        SetElapsed(connection, 120);
        if (lateFacilityDelivery) ConnectionContentBootstrap.OnSnapshotDelivered(connection, 1);
        else MEC.Timing.Advance();
        Assert.True(connection.Disconnected);
        Assert.False(AuthenticatedTransportSessions.IsActiveConnection(connection));
    }

    [Fact]
    public void DeadlineIsAbsoluteAndSurvivesIncompleteScopeReplacement()
    {
        double elapsed = 0;
        var state = new ConnectionContentState(() => elapsed);
        byte[] hash = new byte[32];
        Assert.True(state.Offer(1, hash, hash));
        elapsed = 119;
        Assert.True(state.Approve(hash, hash));
        var replacement = state.PreserveDeadline();
        Assert.True(replacement.Offer(2, hash, hash));
        elapsed = 120;
        Assert.True(state.Expired);
        Assert.True(replacement.Expired);
        Assert.False(state.BeginTransfer(1));
        Assert.False(replacement.Approve(hash, hash));
        Assert.True(replacement.Reject());
        Assert.False(replacement.Reject());
    }

    [Fact]
    public void RateBudgetInstancesSurviveHubAdoptionAndGenerationReplacement()
    {
        var connection = Start();
        var raw = AuthenticatedTransportSessions.GetRateLimiter(connection, raw: true);
        var messages = AuthenticatedTransportSessions.GetRateLimiter(connection, raw: false);
        var hub = new ReferenceHub { connectionToClient = connection };
        Assert.True(AuthenticatedTransportSessions.TryAdoptConnection(hub, out _));
        Assert.Same(raw, AnomalyReplayTable.GetRawRateLimiterOrCreate(hub));
        Assert.Same(messages, AnomalyReplayTable.GetRateLimiterOrCreate(hub));
        ConnectionContentBootstrap.OnGenerationStarted();
        Assert.Same(raw, AuthenticatedTransportSessions.GetRateLimiter(connection, raw: true));
        Assert.Same(messages, AuthenticatedTransportSessions.GetRateLimiter(connection, raw: false));
    }

    [Fact]
    public void OldActivationCannotReadyIdenticalHashSuccessorAfterItsNewSessionRequest()
    {
        var connection = Start();
        var first = Offer(connection);
        Send(connection, Decision(first.Catalog), 1);
        Send(connection, new FileRequestMessage { SessionId = first.Manifest.SessionId }, 2);
        var stale = Pack(Plaintext(Activation(first.Catalog), 3));
        ConnectionContentBootstrap.HandlePacked(connection, stale);
        Assert.True(ConnectionContentBootstrap.IsComplete(connection));
        ConnectionContentBootstrap.OnGenerationStarted();
        ConnectionContentBootstrap.OnSnapshotDelivered(connection, 2);
        var second = Offer(connection);
        Assert.Equal(first.Catalog.CatalogSha256, second.Catalog.CatalogSha256);
        Send(connection, Decision(second.Catalog), 4);
        Send(connection, new FileRequestMessage { SessionId = second.Manifest.SessionId }, 5);
        ConnectionContentBootstrap.HandlePacked(connection, stale);
        Assert.True(connection.Disconnected);
        Assert.False(ConnectionContentBootstrap.IsComplete(connection));
        Assert.Single(Messages(connection).OfType<ContentBootstrapReadyMessage>());
    }

    private static NetworkConnectionToClient Start(bool deliverGeneration = true)
    {
        AnomalyMessageRegistry.RegisterDefaults();
        ServerAssetManager.Init(null);
        Assert.True(SpinWait.SpinUntil(() => ServerAssetManager.IsReady, 5000));
        var connection = new NetworkConnectionToClient();
        AuthenticatedTransportSessions.RegisterConnection(connection, new PlayerStorage
        {
            UserId = "content-bootstrap@anomaly", AcceptedPeer = new object(),
            ClientToServerKey = (byte[])ClientKey.Clone(), ServerToClientKey = (byte[])ServerKey.Clone(),
            SessionId = (byte[])SessionId.Clone(), TranscriptHash = (byte[])Transcript.Clone(),
        });
        ConnectionContentBootstrap.Begin(connection);
        if (deliverGeneration) ConnectionContentBootstrap.OnSnapshotDelivered(connection, 1);
        MEC.Timing.Advance(); MEC.Timing.Advance();
        return connection;
    }

    private static void SetElapsed(NetworkConnectionToClient connection, double seconds)
    {
        var admissions = (System.Collections.IDictionary)typeof(ConnectionContentBootstrap)
            .GetField("Admissions", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        object admission = admissions[connection];
        var gate = (ConnectionContentState)admission.GetType().GetField("Gate", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(admission);
        typeof(ConnectionContentState).GetField("_elapsed", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(gate, new Func<double>(() => seconds));
    }

    private static (ContentCatalog Catalog, FileManifestMessage Manifest) Offer(NetworkConnectionToClient connection)
    {
        var messages = Messages(connection).ToList();
        return (messages.OfType<ContentCatalogMessage>().Last().Catalog, messages.OfType<FileManifestMessage>().Last());
    }
    private static IEnumerable<IAnomalyMessage> Messages(NetworkConnectionToClient connection)
    {
        foreach (var packed in connection.Sent)
        {
            byte[] data = AuthTransportCrypto.Decrypt(ServerKey, SessionId, Transcript, AuthTransportDirection.ServerToClient, packed.Data);
            uint hash = BitConverter.ToUInt32(data, 5);
            Assert.True(AnomalyMessageRegistry.TryCreate(hash, out var message));
            using var stream = new MemoryStream(data, 9, data.Length - 9);
            using var reader = new BinaryReader(stream);
            message.Deserialize(reader);
            yield return message;
        }
    }
    private static ContentCatalogDecisionMessage Decision(ContentCatalog catalog) => new()
    { Decision = ContentCatalogDecision.Approved, CatalogSha256 = catalog.CatalogSha256, ManifestSha256 = catalog.ManifestSha256 };
    private static ContentCatalogAckMessage Activation(ContentCatalog catalog) => new()
    { Status = ContentCatalogActivationStatus.Activated, CatalogSha256 = catalog.CatalogSha256, ManifestSha256 = catalog.ManifestSha256 };
    private static void Send(NetworkConnectionToClient connection, IAnomalyMessage message, uint counter) =>
        ConnectionContentBootstrap.HandlePacked(connection, Pack(Plaintext(message, counter)));
    private static byte[] Plaintext(IAnomalyMessage message, uint counter)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(AnomalyChannel.ChannelId); writer.Write(counter); writer.Write(AnomalyMessageRegistry.GetHash(message.MessageName));
        message.Serialize(writer); return stream.ToArray();
    }
    private static EncryptedChannelManager.EncryptedMessageOutside Pack(byte[] plaintext) => new(
        EncryptedChannelManager.SecurityLevel.EncryptedAndAuthenticated,
        AuthTransportCrypto.Encrypt(ClientKey, SessionId, Transcript, AuthTransportDirection.ClientToServer, plaintext));

    public void Dispose()
    {
        ConnectionContentBootstrap.Reset(); FileTransferSession.Reset(); MEC.Timing.Reset();
        AuthenticatedTransportSessions.Reset(); AnomalyReplayTable.Reset(); ServerAssetManager.Reset();
    }
}
