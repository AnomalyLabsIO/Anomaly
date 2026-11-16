using System.Linq;
using Anomaly.Client.Api.Networking;
using Anomaly.Client.Assets;
using Anomaly.Client.Components;
using Anomaly.Client.Content;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Content;
using Anomaly.Shared.Networking.Messages;
using Xunit;

namespace Anomaly.Shared.Tests;

public sealed class ContentBundleBudgetTests : IDisposable
{
    public ContentBundleBudgetTests()
    {
        ContentCatalogClient.Init();
        ContentConsentFixture.Reset();
        ContentConsentFixture.Begin();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Local_and_server_copy_conflict_rejects_before_consent_or_any_file_request(bool remembered)
    {
        var offer = MaximumOffer();
        AssetBundleManager.BudgetLocalCopies.Add("personal:skins", 3309850);
        if (remembered) ContentConsentStore.TryRemember(Anomaly.Client.Auth.AuthHandler.CurrentServerFingerprint,
            offer.Catalog.CatalogSha256, offer.Catalog.ManifestSha256);
        ContentConsentFixture.Deliver(offer);
        Assert.Equal(0, PasswordPrompt.Shown);
        AssertNoAcquisition();
        Assert.Equal("CONTENT-BUNDLE-BUDGET", ConnectionInfoOverlay.FailureCode);
        Assert.Contains("256 MiB", ConnectionInfoOverlay.FailureReason);
        Assert.Contains("Local packs retain", ConnectionInfoOverlay.FailureReason);
    }

    [Fact]
    public void Exact_local_copy_reuse_does_not_reject_a_server_offer_that_fits()
    {
        var offer = MaximumOffer();
        var shared = offer.Catalog.Packs[0];
        AssetBundleManager.BudgetLocalCopies.Add(AssetBundleManager.BudgetKey(shared), shared.BundleSize);
        ContentConsentFixture.Deliver(offer);
        Assert.Equal(1, PasswordPrompt.Shown);
        Assert.Equal(0, AnomalyMessaging.TransportFailures);
        Assert.Empty(AnomalyMessaging.Sent.OfType<FileRequestMessage>());
    }

    [Fact]
    public void Local_ownership_changes_while_the_consent_prompt_is_open_are_checked_before_approval()
    {
        ContentConsentFixture.Deliver(MaximumOffer());
        Assert.Equal(1, PasswordPrompt.Shown);
        AssetBundleManager.BudgetLocalCopies.Add("personal:skins", 3309850);
        ContentConsentFixture.Approve();
        AssertNoAcquisition();
        Assert.Equal("CONTENT-BUNDLE-BUDGET", ConnectionInfoOverlay.FailureCode);
    }

    [Fact]
    public void Prewarm_rechecks_capacity_after_approval_before_any_file_request()
    {
        ContentConsentFixture.Deliver(MaximumOffer());
        AnomalyMessaging.TrySendResult = message =>
        {
            if (message is ContentCatalogDecisionMessage decision && decision.Decision == ContentCatalogDecision.Approved)
                AssetBundleManager.BudgetLocalCopies.Add("personal:skins", 3309850);
            return true;
        };
        ContentConsentFixture.Approve();
        Assert.Empty(AnomalyMessaging.Sent.OfType<FileRequestMessage>());
        Assert.Equal(0, AssetBundleManager.PrewarmOffers);
        Assert.Equal("CONTENT-BUNDLE-BUDGET", ConnectionInfoOverlay.FailureCode);
        Assert.False(ContentCatalogClient.IsActivated);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Decoded_conflict_rejects_a_small_encoded_offer_before_any_acquisition(bool remembered)
    {
        var offer = ContentConsentFixture.CreateOffer(802, 0);
        offer.Catalog.Packs[0].DecodedBytes = 267745256;
        offer.Catalog.CatalogSha256 = CatalogBuilder.ComputeCatalogHash(offer.Catalog);
        AssetBundleManager.BudgetLocalDecoded.Add(new BundleCopyBudget.DecodedDemand("skin", "skin:index", 41697280));
        if (remembered) ContentConsentStore.TryRemember(Anomaly.Client.Auth.AuthHandler.CurrentServerFingerprint,
            offer.Catalog.CatalogSha256, offer.Catalog.ManifestSha256);
        ContentConsentFixture.Deliver(offer);
        Assert.Equal(0, PasswordPrompt.Shown);
        AssertNoAcquisition();
        Assert.Equal("CONTENT-BUNDLE-BUDGET", ConnectionInfoOverlay.FailureCode);
        Assert.Contains("decoded asset capacity", ConnectionInfoOverlay.FailureReason);
    }

    private static void AssertNoAcquisition()
    {
        Assert.Empty(AnomalyMessaging.Sent.OfType<FileRequestMessage>());
        Assert.DoesNotContain(AnomalyMessaging.Sent.OfType<ContentCatalogDecisionMessage>(),
            decision => decision.Decision == ContentCatalogDecision.Approved);
        Assert.Equal(0, AssetBundleManager.PrewarmOffers);
        Assert.False(ContentCatalogClient.IsActivated);
        Assert.Equal(1, AnomalyMessaging.TransportFailures);
    }

    private static ContentConsentFixture.Offer MaximumOffer()
    {
        const long bundleBytes = 267679241;
        var manifest = new FileManifestMessage { SessionId = 801 };
        var packs = new System.Collections.Generic.List<CatalogPack>();
        for (int index = 0; index < 8; index++)
        {
            var bundle = Enumerable.Repeat((byte)(0x70 + index), 32).ToArray();
            var metadata = Enumerable.Repeat((byte)(0x80 + index), 32).ToArray();
            uint size = (uint)(bundleBytes / 8 + (index == 0 ? bundleBytes % 8 : 0));
            packs.Add(new CatalogPack { PackId = "fixture:bulk" + index, BundleHash = bundle, BundleSize = size,
                IndexHash = metadata, IndexSize = 512, DecodedBytes = 33423360 });
            manifest.Files.Add(new FileManifestMessage.FileEntry("bulk" + index + ".bundle", bundle, size, FileContentKind.Bundle));
            manifest.Files.Add(new FileManifestMessage.FileEntry("bulk" + index + ".index.json", metadata, 512, FileContentKind.PackIndex));
        }
        manifest.ManifestSha256 = FileManifestMessage.ComputeManifestSha256(manifest.Files);
        var catalog = CatalogBuilder.Build(Array.Empty<CatalogEntry>(), 1, 0, manifest.ManifestSha256, packs);
        return new ContentConsentFixture.Offer(catalog, manifest);
    }

    public void Dispose() => ContentConsentFixture.Reset();
}
