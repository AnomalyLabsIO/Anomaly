using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Anomaly.Server.Auth;
using Anomaly.Server.Content;
using Anomaly.Server.FileTransfer;
using Anomaly.Server.Networking;
using Anomaly.Shared.Content;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;

namespace Anomaly.Shared.Tests;

/// <summary>Exercises real connection admission/transfer source against inert transport and scheduler boundaries.</summary>
internal static class ServerBootstrapFixture
{
    internal static void Start(ReferenceHub hub)
    {
        ConnectionContentBootstrap.Cleanup(hub.connectionToClient);
        AuthenticatedTransportSessions.RemoveConnection(hub.connectionToClient);
        AuthenticatedTransportSessions.RegisterConnection(hub.connectionToClient, new PlayerStorage
        {
            UserId = "transfer-fixture@anomaly", AcceptedPeer = new object(),
            ClientToServerKey = Enumerable.Repeat((byte)1, 32).ToArray(),
            ServerToClientKey = Enumerable.Repeat((byte)2, 32).ToArray(),
            SessionId = Enumerable.Repeat((byte)3, 16).ToArray(),
            TranscriptHash = Enumerable.Repeat((byte)4, 32).ToArray(),
        });
        ConnectionContentBootstrap.Begin(hub.connectionToClient);
        ConnectionContentBootstrap.OnSnapshotDelivered(hub.connectionToClient, 1);
    }

    internal static ContentCatalog Catalog => AnomalyMessaging.Sent.OfType<ContentCatalogMessage>().Last().Catalog;
    internal static FileManifestMessage Manifest => AnomalyMessaging.Sent.OfType<FileManifestMessage>().Last();
    internal static FileTransferSession Session(ReferenceHub hub) => (FileTransferSession)((IDictionary)typeof(FileTransferSession)
        .GetField("ActiveSessions", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null))[hub.connectionToClient];
    internal static void Handle(ReferenceHub hub, IAnomalyMessage message) =>
        ConnectionContentBootstrap.HandleMessage(hub.connectionToClient, message);
    internal static void Approve(ReferenceHub hub) => Handle(hub, new ContentCatalogDecisionMessage
    { Decision = ContentCatalogDecision.Approved, CatalogSha256 = Catalog.CatalogSha256, ManifestSha256 = Catalog.ManifestSha256 });
    internal static bool ActivateAndAdopt(ReferenceHub hub)
    {
        Handle(hub, new ContentCatalogAckMessage
        { Status = ContentCatalogActivationStatus.Activated, CatalogSha256 = Catalog.CatalogSha256, ManifestSha256 = Catalog.ManifestSha256 });
        return ContentCatalogBridge.AdoptForPlayer(hub);
    }
}
