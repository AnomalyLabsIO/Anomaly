using Anomaly.Server.Content;
using Anomaly.Shared.Content;
using Xunit;

namespace Anomaly.Server.Tests;

public sealed class AssetActivationTests
{
    [Fact]
    public void Asset_publication_waits_for_transfer_and_successful_readiness_publication()
    {
        ContentGrantGate.Reset();
        var catalog = new ContentCatalog();
        var events = new List<string>();
        Action<uint> handler = _ => events.Add("assets");
        ContentGrantGate.Activated += handler;
        try
        {
            ContentGrantGate.RecordOffer(1, catalog);
            Assert.True(ContentGrantGate.TryRecordDecision(1, catalog.CatalogSha256, catalog.ManifestSha256, true));
            Assert.False(ContentActivationCoordinator.TryCommit(1, catalog.CatalogSha256, catalog.ManifestSha256, true, () => true, out _));
            Assert.True(ContentGrantGate.TryBeginTransfer(1));
            Assert.True(ContentGrantGate.TryCompleteTransfer(1));
            Assert.Empty(events);
            Assert.True(ContentActivationCoordinator.TryCommit(1, catalog.CatalogSha256, catalog.ManifestSha256, true, () => { events.Add("ready"); return true; }, out bool published));
            Assert.True(published);
            Assert.Equal(new[] { "ready", "assets" }, events);
            Assert.False(ContentActivationCoordinator.TryCommit(1, catalog.CatalogSha256, catalog.ManifestSha256, true, () => true, out _));
        }
        finally { ContentGrantGate.Activated -= handler; ContentGrantGate.Reset(); }
    }
    [Fact]
    public void Failed_ready_publication_never_replays_asset_state()
    {
        ContentGrantGate.Reset();
        var catalog = new ContentCatalog();
        int assets = 0;
        Action<uint> handler = _ => assets++;
        ContentGrantGate.Activated += handler;
        try
        {
            ContentGrantGate.RecordOffer(2, catalog);
            ContentGrantGate.TryRecordDecision(2, catalog.CatalogSha256, catalog.ManifestSha256, true);
            ContentGrantGate.TryBeginTransfer(2); ContentGrantGate.TryCompleteTransfer(2);
            Assert.True(ContentActivationCoordinator.TryCommit(2, catalog.CatalogSha256, catalog.ManifestSha256, true, () => false, out bool published));
            Assert.False(published); Assert.Equal(0, assets);
            ContentGrantGate.Remove(2);
        }
        finally { ContentGrantGate.Activated -= handler; ContentGrantGate.Reset(); }
    }
}
