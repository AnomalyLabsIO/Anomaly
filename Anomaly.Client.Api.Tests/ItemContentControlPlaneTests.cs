using System.Security.Cryptography;
using Anomaly.Client.Api.Content;
using Xunit;

namespace Anomaly.Client.Api.Tests;

public sealed class ItemContentControlPlaneTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Anomaly.ItemContent.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Canonical_hash_is_independent_of_manifest_collection_order()
    {
        var first = Catalog(("example.first", new byte[] { 1 }), ("example.second", new byte[] { 2 }));
        var second = Catalog(("example.second", new byte[] { 2 }), ("example.first", new byte[] { 1 }));

        Assert.Equal(first.CatalogSha256, second.CatalogSha256);
        Assert.True(ItemCatalogCanonicalizer.MatchesDeclaredHash(first));
        Assert.Empty(ItemCatalogValidator.Validate(first));
    }

    [Fact]
    public void Consent_is_invalidated_by_catalog_or_authenticated_server_change()
    {
        var first = Catalog(("example.asset", new byte[] { 1, 2, 3 }));
        var changed = Catalog(("example.asset", new byte[] { 1, 2, 4 }));
        var consent = Consent(first);

        Assert.True(consent.Allows(Fingerprint, first.CatalogSha256));
        Assert.False(consent.Allows(Fingerprint, changed.CatalogSha256));
        Assert.False(consent.Allows(new string('e', 64), first.CatalogSha256));
        Assert.Throws<InvalidOperationException>(() => new ItemContentTransferSession(
            changed, Fingerprint, consent, Path.Combine(_root, "cache")));
    }

    [Fact]
    public void Catalog_diff_reports_files_bytes_and_changed_content()
    {
        var first = Catalog(("example.first", new byte[] { 1, 2 }));
        var changed = Catalog(
            ("example.first", new byte[] { 3, 4 }),
            ("example.second", new byte[] { 5, 6, 7 }));

        var diff = ItemCatalogDiff.Compare(first, changed);

        Assert.Equal(2, diff.FileCount);
        Assert.Equal(5, diff.AggregateBytes);
        Assert.Equal(5, diff.DownloadBytes);
        Assert.Equal(new[] { "example.second" }, diff.AddedAssets);
        Assert.Equal(new[] { "example.first" }, diff.ChangedAssets);
        Assert.True(diff.HasChanges);
    }

    [Fact]
    public void Unsolicited_or_mismatched_chunks_are_rejected()
    {
        var catalog = Catalog(("example.asset", new byte[] { 1, 2, 3 }));
        using var session = Session(catalog);
        var request = session.RequestAsset("example.asset");

        Assert.Throws<InvalidOperationException>(() => session.AcceptChunk(
            Guid.NewGuid(), "example.asset", 0, new byte[] { 1 }));
        Assert.Throws<InvalidOperationException>(() => session.AcceptChunk(
            request.RequestId, "example.not_declared", 0, new byte[] { 1 }));
        Assert.Throws<InvalidOperationException>(() => session.RequestAsset("example.not_declared"));
    }

    [Fact]
    public void Hash_mismatch_discards_partial_content_and_does_not_activate_cache_entry()
    {
        var expected = new byte[] { 1, 2, 3, 4 };
        var catalog = Catalog(("example.asset", expected));
        using var session = Session(catalog);
        var request = session.RequestAsset("example.asset");
        session.AcceptChunk(request.RequestId, request.AssetKey, 0, new byte[] { 4, 3, 2, 1 });

        var status = session.CompleteAsset(request.RequestId, request.AssetKey);

        Assert.Equal(ItemContentCompletionStatus.HashMismatch, status);
        Assert.False(session.IsAssetAvailable("example.asset"));
        Assert.False(File.Exists(session.GetCachePath("example.asset") + ".part"));
    }

    [Fact]
    public void Partial_file_resumes_at_validated_offset_then_commits_by_hash()
    {
        var expected = Enumerable.Range(0, ItemContentLimits.MaxTransferChunkBytes + 17)
            .Select(value => (byte)(value % 251)).ToArray();
        var catalog = Catalog(("example.asset", expected));
        var firstChunk = expected.AsMemory(0, 4096);

        using (var firstSession = Session(catalog))
        {
            var request = firstSession.RequestAsset("example.asset");
            Assert.Equal(4096, firstSession.AcceptChunk(
                request.RequestId, request.AssetKey, request.ResumeOffset, firstChunk));
        }

        using var resumedSession = Session(catalog);
        var resumed = resumedSession.RequestAsset("example.asset");
        Assert.Equal(4096, resumed.ResumeOffset);
        var offset = resumed.ResumeOffset;
        while (offset < expected.Length)
        {
            var count = (int)Math.Min(ItemContentLimits.MaxTransferChunkBytes, expected.Length - offset);
            offset = resumedSession.AcceptChunk(
                resumed.RequestId,
                resumed.AssetKey,
                offset,
                expected.AsMemory((int)offset, count));
        }

        Assert.Equal(ItemContentCompletionStatus.Complete,
            resumedSession.CompleteAsset(resumed.RequestId, resumed.AssetKey));
        Assert.True(resumedSession.IsAssetAvailable("example.asset"));
        Assert.Equal(expected, File.ReadAllBytes(resumedSession.GetCachePath("example.asset")));
    }

    [Fact]
    public void Catalog_activation_is_all_or_nothing_and_can_roll_back()
    {
        var first = Catalog(("example.first", new byte[] { 1 }));
        var second = Catalog(("example.second", new byte[] { 2 }));
        var statePath = Path.Combine(_root, "activation", "state.json");
        var manager = new ItemCatalogActivationManager(new FileItemCatalogActivationStore(statePath));
        var available = new HashSet<ItemContentCacheKey>();
        available.Add(CacheKey(first, first.Assets[0]));

        manager.Prepare(first, Fingerprint, Consent(first), available.Contains, DateTimeOffset.UnixEpoch);
        manager.Commit();
        Assert.Equal(first.CatalogSha256, manager.Current.Active.CatalogSha256);
        Assert.Equal("example.item", manager.Current.Active.Catalog.Items.Single().Key);

        Assert.Throws<InvalidOperationException>(() => manager.Prepare(
            second, Fingerprint, Consent(second), available.Contains, DateTimeOffset.UnixEpoch.AddSeconds(1)));
        Assert.Equal(first.CatalogSha256, manager.Current.Active.CatalogSha256);

        available.Add(CacheKey(second, second.Assets[0]));
        manager.Prepare(second, Fingerprint, Consent(second), available.Contains, DateTimeOffset.UnixEpoch.AddSeconds(2));
        manager.Commit();
        Assert.Equal(second.CatalogSha256, manager.Current.Active.CatalogSha256);
        Assert.Equal(first.CatalogSha256, manager.Current.Previous.CatalogSha256);

        Assert.True(manager.Rollback());
        Assert.Equal(first.CatalogSha256, manager.Current.Active.CatalogSha256);
        Assert.Equal(first.CatalogSha256, manager.Current.Active.Catalog.CatalogSha256);
        Assert.Null(manager.Current.Previous);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private ItemContentTransferSession Session(ItemCatalog catalog) => new(
        catalog,
        Fingerprint,
        Consent(catalog),
        Path.Combine(_root, "cache"));

    private static ItemCatalogConsentRecord Consent(ItemCatalog catalog) =>
        ItemCatalogConsentRecord.Create(Fingerprint, catalog, DateTimeOffset.UnixEpoch);

    private static ItemContentCacheKey CacheKey(ItemCatalog catalog, PassiveAssetReference asset) =>
        ItemContentCacheKey.Create(Fingerprint, catalog.CatalogSha256, asset.Sha256);

    private static ItemCatalog Catalog(params (string Key, byte[] Content)[] assets)
    {
        var catalog = new ItemCatalog
        {
            ServerFingerprint = Fingerprint,
        };
        foreach (var (key, content) in assets)
        {
            catalog.Assets.Add(new PassiveAssetReference
            {
                Key = key,
                Kind = PassiveAssetKind.TexturePng,
                Sha256 = Sha256(content),
                SizeBytes = content.Length,
            });
        }
        catalog.Items.Add(new ItemDefinition
        {
            Key = "example.item",
            DisplayName = "Example item",
            BuiltInPrefab = "Coin",
            AssetKeys = assets.Select(value => value.Key).ToList(),
        });
        catalog.CatalogSha256 = ItemCatalogCanonicalizer.ComputeSha256(catalog);
        return catalog;
    }

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private const string Fingerprint = "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";
}
