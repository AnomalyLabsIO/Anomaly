using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Anomaly.Server.Auth;
using Anomaly.Server.Content;
using Anomaly.Server.FileTransfer;
using Anomaly.Server.Networking;
using Anomaly.Shared.Auth;
using Anomaly.Shared.Content;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using Anomaly.Shared.Scripting;
using Mirror;
using Mirror.LiteNetLib4Mirror;
using Xunit;
using ClientEvents = Anomaly.Server.Api.Events.Handlers.ClientEvents;

namespace Anomaly.Server.Tests;

public sealed class ScriptMessagingTests : IDisposable
{
    private static readonly byte[] ClientKey = Enumerable.Repeat((byte)0x11, 32).ToArray();
    private static readonly byte[] ServerKey = Enumerable.Repeat((byte)0x22, 32).ToArray();
    private static readonly byte[] SessionId = Enumerable.Repeat((byte)0x33, 16).ToArray();
    private static readonly byte[] Transcript = Enumerable.Repeat((byte)0x44, 32).ToArray();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "anomaly-script-messages-" + Guid.NewGuid().ToString("N"));
    private uint _counter = 4;

    [Fact]
    public void SendBindsApprovedIndexAndActivationWithoutChangingReusablePluginMessage()
    {
        var hub = CreateActivatedHub();
        var outgoing = ScriptToClientMessage.Create("example:panel", "GetState", new Query { RequestId = 42 });
        int before = hub.connectionToClient.Sent.Count;
        Assert.True(AnomalyMessaging.TrySendToClient(hub, outgoing));
        var sent = Assert.IsType<ScriptToClientMessage>(Messages(hub.connectionToClient).Last());
        Assert.Equal(IndexHash(), sent.IndexSha256);
        Assert.Equal(Manifest(hub.connectionToClient).SessionId, sent.ActivationId);
        Assert.True(sent.TryRead<Query>(out var query));
        Assert.Equal(42U, query.RequestId);
        Assert.Equal(before + 1, hub.connectionToClient.Sent.Count);
        Assert.Equal(0UL, outgoing.ActivationId);
        Assert.True(string.IsNullOrEmpty(outgoing.IndexSha256));
    }

    [Theory]
    [InlineData("pack")]
    [InlineData("name")]
    [InlineData("direction")]
    [InlineData("payload")]
    [InlineData("revoked")]
    [InlineData("no-adoption")]
    public void SendRejectsUnapprovedOrMismatchedMessagesWithoutConsumingCounter(string failure)
    {
        var hub = CreateActivatedHub(adoptContent: failure != "no-adoption");
        string packId = failure == "pack" ? "example:other" : "example:panel";
        string name = failure == "name" ? "Unknown" : failure == "direction" ? "State" : "GetState";
        var payload = failure == "payload" ? ScriptMessagePayload.FromValues(new Dictionary<string, object> { ["RequestId"] = "invalid" })
            : ScriptMessagePayload.Create(new Query { RequestId = 1 });
        if (failure == "revoked") ContentGrantGate.Remove(hub.netId);
        int before = hub.connectionToClient.Sent.Count;
        Assert.False(AnomalyMessaging.TrySendToClient(hub, ScriptToClientMessage.Create(packId, name, payload)));
        Assert.Equal(before, hub.connectionToClient.Sent.Count);
        Assert.False(hub.connectionToClient.Disconnected);
        // Catalog and ready used ordered counters 1..2; manifest uses its own unordered channel.
        Assert.Equal(3U, AnomalyReplayTable.GetOrCreate(hub).NextTxCounter(MessageChannel.ReliableOrdered));
    }

    [Fact]
    public void AuthenticatedReplyReachesExistingInternalAndPublicEvents()
    {
        var hub = CreateActivatedHub();
        int received = 0;
        Action<ReferenceHub, IAnomalyMessage> handler = (sender, message) =>
        {
            Assert.Same(hub, sender);
            var reply = Assert.IsType<ScriptToServerMessage>(message);
            Assert.True(reply.TryRead<Reply>(out var dto));
            Assert.Equal(7U, dto.RequestId);
            Assert.True(dto.Enabled);
            received++;
        };
        AnomalyMessaging.MessageReceived += handler;
        try
        {
            Receive(hub, ReplyMessage(hub));
            Assert.Equal(1, received);
            Assert.Equal(1, ClientEvents.Messages);
        }
        finally { AnomalyMessaging.MessageReceived -= handler; }
    }

    [Theory]
    [InlineData("hash")]
    [InlineData("activation")]
    [InlineData("pack")]
    [InlineData("name")]
    [InlineData("payload")]
    [InlineData("revoked")]
    public void InvalidRepliesNeverReachExistingMessageEvents(string failure)
    {
        var hub = CreateActivatedHub();
        string hash = failure == "hash" ? new string('a', 64) : IndexHash();
        ulong activation = Manifest(hub.connectionToClient).SessionId + (failure == "activation" ? 1UL : 0UL);
        string packId = failure == "pack" ? "example:other" : "example:panel";
        string name = failure == "name" ? "GetState" : "State";
        var payload = failure == "payload" ? ScriptMessagePayload.FromValues(new Dictionary<string, object> { ["RequestId"] = 7U, ["Enabled"] = "invalid" })
            : ScriptMessagePayload.Create(new Reply { RequestId = 7, Enabled = true });
        if (failure == "revoked") ContentGrantGate.Remove(hub.netId);
        int received = 0;
        Action<ReferenceHub, IAnomalyMessage> handler = (_, __) => received++;
        AnomalyMessaging.MessageReceived += handler;
        try
        {
            Receive(hub, ScriptToServerMessage.Create(packId, name, payload).Bind(hash, activation));
            Assert.Equal(0, received);
            Assert.Equal(0, ClientEvents.Messages);
        }
        finally { AnomalyMessaging.MessageReceived -= handler; }
    }

    [Fact]
    public void RevocationByOneSubscriberStopsLaterSubscribersAndPublicPublication()
    {
        var hub = CreateActivatedHub();
        int later = 0;
        Action<ReferenceHub, IAnomalyMessage> revoke = (sender, _) => ContentGrantGate.Remove(sender.netId);
        Action<ReferenceHub, IAnomalyMessage> observer = (_, __) => later++;
        AnomalyMessaging.MessageReceived += revoke;
        AnomalyMessaging.MessageReceived += observer;
        try
        {
            Receive(hub, ReplyMessage(hub));
            Assert.Equal(0, later);
            Assert.Equal(0, ClientEvents.Messages);
            Assert.True(AuthenticatedTransportSessions.IsActive(hub));
        }
        finally
        {
            AnomalyMessaging.MessageReceived -= revoke;
            AnomalyMessaging.MessageReceived -= observer;
        }
    }

    [Fact]
    public void SchemaRetainsOfferedSnapshotAfterServerRescan()
    {
        var hub = CreateActivatedHub();
        string approvedHash = IndexHash();
        WritePack("Renamed");
        ServerAssetManager.Rescan();
        Assert.True(SpinWait.SpinUntil(() =>
        {
            using var latest = ServerAssetManager.AcquireSnapshot();
            return latest.TryGetScriptSchema("example:panel", "Renamed", AnomalyMessageDirection.ServerToClient, out _, out _);
        }, 5000));
        Assert.True(AnomalyMessaging.TrySendToClient(hub,
            ScriptToClientMessage.Create("example:panel", "GetState", new Query { RequestId = 1 })));
        Assert.False(AnomalyMessaging.TrySendToClient(hub,
            ScriptToClientMessage.Create("example:panel", "Renamed", new Query { RequestId = 1 })));
        Assert.Equal(approvedHash, Assert.IsType<ScriptToClientMessage>(Messages(hub.connectionToClient).Last()).IndexSha256);
    }

    [Fact]
    public void SnapshotSchemaResultsAreDefensiveCopies()
    {
        WritePack();
        using var snapshot = AssetPackSnapshot.Build(_root);
        Assert.True(snapshot.TryGetScriptSchema("example:panel", "GetState", AnomalyMessageDirection.ServerToClient, out var first, out var hash));
        first.Fields.Clear();
        Assert.True(snapshot.TryGetScriptSchema("example:panel", "GetState", AnomalyMessageDirection.ServerToClient, out var second, out var secondHash));
        Assert.Single(second.Fields);
        Assert.Equal(hash, secondHash);
        Assert.Equal(IndexHash(), hash);
    }

    [Fact]
    public void ScriptBurstIsBoundedAndDirectionsHaveIndependentBudgets()
    {
        var hub = CreateActivatedHub();
        var outgoing = ScriptToClientMessage.Create("example:panel", "GetState", new Query());
        for (int i = 0; i < 10; i++) Assert.True(AnomalyMessaging.TrySendToClient(hub, outgoing));
        Assert.False(AnomalyMessaging.TrySendToClient(hub, outgoing));
        int received = 0;
        Action<ReferenceHub, IAnomalyMessage> observe = (_, __) => received++;
        AnomalyMessaging.MessageReceived += observe;
        try
        {
            for (int i = 0; i < 11; i++) Receive(hub, ReplyMessage(hub));
            Assert.Equal(10, received);
            Assert.Equal(10, ClientEvents.Messages);
            Assert.False(hub.connectionToClient.Disconnected);
        }
        finally { AnomalyMessaging.MessageReceived -= observe; }
    }

    [Fact]
    public void BroadcastSkipsClientsWithoutTheApprovedPack()
    {
        var hub = CreateActivatedHub();
        var ineligible = new ReferenceHub();
        AuthenticatedTransportSessions.Register(ineligible, Player());
        ReferenceHub.AllHubs.Add(hub);
        ReferenceHub.AllHubs.Add(ineligible);
        AnomalyMessaging.SendToAll(ScriptToClientMessage.Create("example:panel", "GetState", new Query()));
        Assert.IsType<ScriptToClientMessage>(Messages(hub.connectionToClient).Last());
        Assert.Empty(ineligible.connectionToClient.Sent);
        Assert.False(ineligible.connectionToClient.Disconnected);
    }

    [Fact]
    public void IdenticalPackSuccessorRejectsOldRepliesAndGetsFreshTrafficBudgets()
    {
        var hub = CreateActivatedHub();
        var oldReply = ReplyMessage(hub);
        var outgoing = ScriptToClientMessage.Create("example:panel", "GetState", new Query());
        for (int i = 0; i < 10; i++) Assert.True(AnomalyMessaging.TrySendToClient(hub, outgoing));
        Assert.False(AnomalyMessaging.TrySendToClient(hub, outgoing));

        ConnectionContentBootstrap.OnGenerationStarted();
        Assert.False(AnomalyMessaging.TrySendToClient(hub, outgoing));
        ConnectionContentBootstrap.OnSnapshotDelivered(hub.connectionToClient, 2);
        var catalog = Messages(hub.connectionToClient).OfType<ContentCatalogMessage>().Last().Catalog;
        ulong nextActivation = Manifest(hub.connectionToClient).SessionId;
        Assert.NotEqual(oldReply.ActivationId, nextActivation);
        Assert.Equal(oldReply.IndexSha256, IndexHash());
        SendBootstrap(hub.connectionToClient, new ContentCatalogDecisionMessage
        { Decision = ContentCatalogDecision.Approved, CatalogSha256 = catalog.CatalogSha256, ManifestSha256 = catalog.ManifestSha256 }, _counter++);
        SendBootstrap(hub.connectionToClient, new FileRequestMessage { SessionId = nextActivation }, _counter++);
        SendBootstrap(hub.connectionToClient, new ContentCatalogAckMessage
        { Status = ContentCatalogActivationStatus.Activated, CatalogSha256 = catalog.CatalogSha256, ManifestSha256 = catalog.ManifestSha256 }, _counter++);
        ActivateGrant(hub, catalog);
        Assert.True(FileTransferSession.AdoptForPlayer(hub));

        Assert.True(AnomalyMessaging.TrySendToClient(hub, outgoing));
        Receive(hub, oldReply);
        Assert.Equal(0, ClientEvents.Messages);
        Receive(hub, ReplyMessage(hub));
        Assert.Equal(1, ClientEvents.Messages);
    }

    [Fact]
    public void NativeReliableQueueBackpressureDoesNotSpendScriptBudgetOrDisconnect()
    {
        var hub = CreateActivatedHub();
        var peer = LiteNetLib4MirrorServer.Peers[hub.connectionToClient.connectionId];
        var outgoing = ScriptToClientMessage.Create("example:panel", "GetState", new Query());
        peer.ReliableQueuedPackets = 128;
        int before = hub.connectionToClient.Sent.Count;
        for (int i = 0; i < 20; i++) Assert.False(AnomalyMessaging.TrySendToClient(hub, outgoing));
        Assert.Equal(before, hub.connectionToClient.Sent.Count);
        Assert.False(hub.connectionToClient.Disconnected);
        peer.ReliableQueuedPackets = 127;
        for (int i = 0; i < 10; i++) Assert.True(AnomalyMessaging.TrySendToClient(hub, outgoing));
        Assert.False(AnomalyMessaging.TrySendToClient(hub, outgoing));
        Assert.Equal(before + 10, hub.connectionToClient.Sent.Count);
        Assert.Equal(13U, AnomalyReplayTable.GetOrCreate(hub).NextTxCounter(MessageChannel.ReliableOrdered));
    }

    private ReferenceHub CreateActivatedHub(bool adoptContent = true)
    {
        AnomalyMessageRegistry.RegisterDefaults();
        WritePack();
        ServerAssetManager.Init(_root);
        Assert.True(SpinWait.SpinUntil(() => ServerAssetManager.IsReady, 5000));
        var connection = new NetworkConnectionToClient { connectionId = 17 };
        LiteNetLib4MirrorServer.Peers[connection.connectionId] = new Peer();
        AuthenticatedTransportSessions.RegisterConnection(connection, Player());
        ConnectionContentBootstrap.Begin(connection);
        ConnectionContentBootstrap.OnSnapshotDelivered(connection, 1);
        MEC.Timing.Advance(); MEC.Timing.Advance();
        var catalog = Messages(connection).OfType<ContentCatalogMessage>().Last().Catalog;
        SendBootstrap(connection, new ContentCatalogDecisionMessage
        { Decision = ContentCatalogDecision.Approved, CatalogSha256 = catalog.CatalogSha256, ManifestSha256 = catalog.ManifestSha256 }, 1);
        SendBootstrap(connection, new FileRequestMessage { SessionId = Manifest(connection).SessionId }, 2);
        SendBootstrap(connection, new ContentCatalogAckMessage
        { Status = ContentCatalogActivationStatus.Activated, CatalogSha256 = catalog.CatalogSha256, ManifestSha256 = catalog.ManifestSha256 }, 3);
        var hub = new ReferenceHub { netId = 7, connectionToClient = connection };
        Assert.True(AuthenticatedTransportSessions.TryAdoptConnection(hub, out _));
        Assert.True(ConnectionContentBootstrap.IsComplete(connection));
        ActivateGrant(hub, catalog);
        if (adoptContent) Assert.True(FileTransferSession.AdoptForPlayer(hub));
        return hub;
    }

    private static void ActivateGrant(ReferenceHub hub, ContentCatalog catalog)
    {
        ContentGrantGate.RecordOffer(hub.netId, catalog);
        Assert.True(ContentGrantGate.TryRecordDecision(hub.netId, catalog.CatalogSha256, catalog.ManifestSha256, true));
        Assert.True(ContentGrantGate.TryBeginTransfer(hub.netId));
        Assert.True(ContentGrantGate.TryCompleteTransfer(hub.netId));
        Assert.True(ContentGrantGate.TryRecordActivation(hub.netId, catalog.CatalogSha256, catalog.ManifestSha256, true));
    }

    private static PlayerStorage Player() => new()
    {
        UserId = "script-messages@anomaly", AcceptedPeer = new object(),
        ClientToServerKey = (byte[])ClientKey.Clone(), ServerToClientKey = (byte[])ServerKey.Clone(),
        SessionId = (byte[])SessionId.Clone(), TranscriptHash = (byte[])Transcript.Clone(),
    };

    private ScriptToServerMessage ReplyMessage(ReferenceHub hub) =>
        ScriptToServerMessage.Create("example:panel", "State", new Reply { RequestId = 7, Enabled = true })
            .Bind(IndexHash(), Manifest(hub.connectionToClient).SessionId);

    private void Receive(ReferenceHub hub, IAnomalyMessage message) =>
        AnomalyMessaging.OnReceived(hub, Plaintext(message, _counter++).Skip(1).ToArray(), true, false);

    private void WritePack(string outgoingName = "GetState")
    {
        string pack = Path.Combine(_root, "example", "panel");
        Directory.CreateDirectory(pack);
        File.WriteAllText(Path.Combine(pack, "pack.json"), "{\"SchemaVersion\":1,\"PackId\":\"example:panel\",\"Version\":\"1.0.0\",\"Scripts\":{\"PackId\":\"example:panel\",\"EntryPoint\":\"main.lua\",\"Messages\":\"messages.json\"}}", new UTF8Encoding(false));
        File.WriteAllBytes(Path.Combine(pack, "main.lua"), Array.Empty<byte>());
        string descriptor = "{\"Messages\":[{\"Name\":\"" + outgoingName + "\",\"Direction\":\"ServerToClient\",\"Version\":1,\"Fields\":[{\"Name\":\"RequestId\",\"Type\":\"UInt32\"}]}," +
            "{\"Name\":\"State\",\"Direction\":\"ClientToServer\",\"Version\":1,\"Fields\":[{\"Name\":\"RequestId\",\"Type\":\"UInt32\"},{\"Name\":\"Enabled\",\"Type\":\"Boolean\"}]}]}";
        File.WriteAllText(Path.Combine(pack, "messages.json"), descriptor, new UTF8Encoding(false));
    }

    private string IndexHash()
    {
        using var snapshot = AssetPackSnapshot.Build(_root);
        return ServerAssetManager.ToHex(Assert.Single(snapshot.CopyScriptPacks()).IndexHash);
    }
    private static string Hash(byte[] bytes)
    { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    private static FileManifestMessage Manifest(NetworkConnectionToClient connection) => Messages(connection).OfType<FileManifestMessage>().Last();
    private static IEnumerable<IAnomalyMessage> Messages(NetworkConnectionToClient connection)
    {
        foreach (var packed in connection.Sent)
        {
            byte[] data = AuthTransportCrypto.Decrypt(ServerKey, SessionId, Transcript, AuthTransportDirection.ServerToClient, packed.Data);
            Assert.True(AnomalyMessageRegistry.TryCreate(BitConverter.ToUInt32(data, 5), out var message));
            using var stream = new MemoryStream(data, 9, data.Length - 9);
            using var reader = new BinaryReader(stream);
            message.Deserialize(reader);
            yield return message;
        }
    }
    private static byte[] Plaintext(IAnomalyMessage message, uint counter)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(AnomalyChannel.ChannelId); writer.Write(counter); writer.Write(AnomalyMessageRegistry.GetHash(message.MessageName));
        message.Serialize(writer); return stream.ToArray();
    }
    private static void SendBootstrap(NetworkConnectionToClient connection, IAnomalyMessage message, uint counter) =>
        ConnectionContentBootstrap.HandlePacked(connection, new EncryptedChannelManager.EncryptedMessageOutside(
            EncryptedChannelManager.SecurityLevel.EncryptedAndAuthenticated,
            AuthTransportCrypto.Encrypt(ClientKey, SessionId, Transcript, AuthTransportDirection.ClientToServer, Plaintext(message, counter))));

    public sealed class Query { public uint RequestId { get; set; } }
    public sealed class Reply { public uint RequestId { get; set; } public bool Enabled { get; set; } }

    public void Dispose()
    {
        ConnectionContentBootstrap.Reset(); FileTransferSession.Reset(); MEC.Timing.Reset();
        ContentGrantGate.Reset(); AuthenticatedTransportSessions.Reset(); AnomalyReplayTable.Reset(); ServerAssetManager.Reset();
        LiteNetLib4MirrorServer.Peers.Clear();
        ReferenceHub.AllHubs.Clear(); ClientEvents.Messages = 0;
        string absolute = Path.GetFullPath(_root);
        string temporary = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!absolute.StartsWith(temporary, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(absolute).StartsWith("anomaly-script-messages-", StringComparison.Ordinal))
            throw new InvalidOperationException("Refusing to remove an unexpected fixture path.");
        if (Directory.Exists(absolute))
        {
            string spool = Path.Combine(absolute, ".anomaly-spool");
            Assert.True(SpinWait.SpinUntil(() => !Directory.Exists(spool) || !Directory.EnumerateDirectories(spool).Any(), 5000));
            Directory.Delete(absolute, true);
        }
    }
}
