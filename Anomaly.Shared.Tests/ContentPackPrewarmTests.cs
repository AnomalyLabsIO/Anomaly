using System.Linq;
using Anomaly.Client.Api.FileTransfer;
using Anomaly.Client.Api.Networking;
using Anomaly.Client.Assets;
using Anomaly.Client.Content;
using Anomaly.Client.FileTransfer;
using Anomaly.Client.Internal.Bootstrap;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Networking.Messages;
using Xunit;

namespace Anomaly.Shared.Tests;

public sealed class ContentPackPrewarmTests : IDisposable
{
    public ContentPackPrewarmTests()
    {
        ContentCatalogClient.Init();
        ContentConsentFixture.Reset();
        ContentConsentFixture.Begin();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Approved_bundle_validation_overlaps_remaining_script_downloads_only_when_both_pack_files_are_verified(bool indexCached)
    {
        var offer = ContentConsentFixture.CreateOffer(701, scriptCount: 1);
        var pack = Assert.Single(offer.Catalog.Packs);
        FileCache.HasFileResult = hash => hash.SequenceEqual(pack.BundleHash) || indexCached && hash.SequenceEqual(pack.IndexHash);
        ContentConsentFixture.Deliver(offer);
        Assert.Equal(0, AssetBundleManager.PrewarmOffers);
        Assert.Empty(AssetBundleManager.PrewarmedPacks);
        ContentConsentFixture.Approve();
        Assert.Equal(1, AssetBundleManager.PrewarmOffers);
        Assert.Equal(indexCached ? new[] { pack.PackId } : Array.Empty<string>(), AssetBundleManager.PrewarmedPacks);
        Assert.True(FileTransferClient.IsBulkReceiving);
        Assert.False(FileTransferClient.IsFileApproved(pack.BundleHash, FileContentKind.Bundle));
        Assert.False(ContentCatalogClient.IsActivated);
        Assert.Equal(0, ClientContentRegistrar.Activations);
        Assert.Equal(0, ScriptingBootstrap.PrepareCalls);
        Assert.Empty(AnomalyMessaging.Sent.OfType<ContentCatalogAckMessage>());
        ContentCatalogClient.Reset();
        Assert.Empty(AssetBundleManager.PrewarmedPacks);
    }

    public void Dispose() => ContentConsentFixture.Reset();
}
