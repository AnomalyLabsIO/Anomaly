using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Anomaly.Server.Api.Content;
using Anomaly.Server.Content;
using Anomaly.Server.FileTransfer;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Networking.Messages;
using Xunit;
using ServerMessaging = Anomaly.Server.Networking.AnomalyMessaging;

namespace Anomaly.Shared.Tests;

public sealed class ServerOfferFailureTests : IDisposable
{
    private readonly string _source = Path.GetTempFileName();

    [Theory]
    [InlineData("missing-icon")]
    [InlineData("catalog-send")]
    [InlineData("manifest-send")]
    public void Failed_real_catalog_callback_or_manifest_revokes_once_and_releases_its_snapshot(string failure)
    {
        byte[] bytes = { 1, 2, 3 };
        File.WriteAllBytes(_source, bytes);
        byte[] hash;
        using (var sha = SHA256.Create()) hash = sha.ComputeHash(bytes);
        ServerAssetManager.SetAssets(new ServerAssetManager.AssetInfo
        {
            FullPath = _source, Hash = hash, Size = (uint)bytes.Length, Kind = FileContentKind.Bundle,
        });
        if (failure == "missing-icon")
            ContentRegistry.Add(new CustomItemDefinition
            {
                Key = "fixture:item", BaseTemplate = InventorySystem.ItemType.Coin, DisplayName = "Fixture",
                IconAddress = AssetAddress.Parse("mods/fixture/missing-icon"),
            });
        ServerMessaging.TrySendResult = message =>
            !(failure == "catalog-send" && message is ContentCatalogMessage) &&
            !(failure == "manifest-send" && message is FileManifestMessage);
        var hub = new ReferenceHub { netId = 36 };
        int assetPublications = 0;
        Action<uint> publication = _ => assetPublications++;
        ContentGrantGate.Activated += publication;
        try
        {
            using (var snapshot = ServerAssetManager.AcquireSnapshot())
            {
                int references = SnapshotReferences(snapshot);
                ServerBootstrapFixture.Start(hub);

                Assert.Equal(1, Anomaly.Server.Plugin.QuarantineCount);
                Assert.Same(hub, Anomaly.Server.Plugin.LastQuarantinedHub);
                Assert.Equal(1, hub.connectionToClient.DisconnectCount);
                Assert.False(hub.connectionToClient.isReady);
                Assert.Equal(ContentReadiness.Unknown, ContentGrantGate.GetReadiness(hub.netId));
                Assert.False(ContentGrantGate.CanGrant(hub.netId, "fixture:item"));
                Assert.Equal(0, CustomItems.ReadyPublications);
                Assert.Equal(0, assetPublications);
                Assert.Empty(ServerMessaging.Sent.OfType<FileManifestMessage>());
                Assert.Empty(State("ActiveSessions"));
                Assert.Empty(State("ApprovedManifests"));
                Assert.Equal(references, SnapshotReferences(snapshot));
                FileTransferSession.CleanupPlayer(hub);
                Assert.Equal(1, Anomaly.Server.Plugin.QuarantineCount);
                Assert.Equal(1, hub.connectionToClient.DisconnectCount);
                Assert.Equal(references, SnapshotReferences(snapshot));
            }
            // The publisher waits for actual asynchronous spool deletion, detecting
            // any remaining session or reader lease after its last reference drops.
            ServerAssetManager.SetAssets();
        }
        finally { ContentGrantGate.Activated -= publication; }
    }

    private static IDictionary State(string name) => (IDictionary)typeof(FileTransferSession)
        .GetField(name, BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    private static int SnapshotReferences(AssetPackSnapshot snapshot) => (int)typeof(AssetPackSnapshot)
        .GetField("_references", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(snapshot);

    public void Dispose()
    {
        FileTransferSession.Reset();
        ContentCatalogBridge.Disable();
        ContentRegistry.Reset();
        ServerMessaging.ResetForTests();
        ServerAssetManager.SetAssets();
        Anomaly.Server.Plugin.ResetForTests();
        CustomItems.ReadyPublications = 0;
        File.Delete(_source);
    }
}
