using System;
using System.Linq;

namespace Anomaly.Client.Api.Content;

/// <summary>
/// Two-phase activation coordinator. Prepare validates the entire catalog and
/// every cache artifact without changing active state; Commit replaces the
/// journal in one operation. Rollback restores the prior complete catalog.
/// </summary>
public sealed class ItemCatalogActivationManager
{
    private readonly IItemCatalogActivationStore _store;
    private ItemCatalogActivationState _prepared;

    public ItemCatalogActivationManager(IItemCatalogActivationStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public ItemCatalogActivationSnapshot Current => _store.Load();

    public ItemCatalogActivationState Prepare(
        ItemCatalog catalog,
        string authenticatedServerFingerprint,
        ItemCatalogConsentRecord consent,
        Func<ItemContentCacheKey, bool> contentAvailable,
        DateTimeOffset preparedAtUtc)
    {
        if (contentAvailable == null) throw new ArgumentNullException(nameof(contentAvailable));
        ItemCatalogGuard.ValidateAuthenticatedCatalog(catalog, authenticatedServerFingerprint, consent);

        var keys = catalog.Assets
            .OrderBy(asset => asset.Key, StringComparer.Ordinal)
            .Select(asset => ItemContentCacheKey.Create(
                authenticatedServerFingerprint,
                catalog.CatalogSha256,
                asset.Sha256))
            .ToList();
        var missing = keys.Where(key => !contentAvailable(key)).ToArray();
        if (missing.Length != 0)
            throw new InvalidOperationException($"Catalog activation is missing {missing.Length} validated asset(s).");

        _prepared = new ItemCatalogActivationState
        {
            ServerFingerprint = authenticatedServerFingerprint.ToLowerInvariant(),
            CatalogSha256 = catalog.CatalogSha256.ToLowerInvariant(),
            ActivatedAtUtc = preparedAtUtc.ToUniversalTime(),
            Catalog = CloneCatalog(catalog),
            Assets = keys,
        };
        return _prepared;
    }

    public ItemCatalogActivationState Commit()
    {
        if (_prepared == null) throw new InvalidOperationException("No catalog is prepared for activation.");
        var current = _store.Load();
        var next = new ItemCatalogActivationSnapshot
        {
            Active = _prepared,
            Previous = current.Active,
        };
        _store.SaveAtomically(next);
        var committed = _prepared;
        _prepared = null;
        return committed;
    }

    public bool Rollback()
    {
        var current = _store.Load();
        if (current.Previous == null) return false;
        _store.SaveAtomically(new ItemCatalogActivationSnapshot
        {
            Active = current.Previous,
            Previous = null,
        });
        _prepared = null;
        return true;
    }

    public void CancelPrepared() => _prepared = null;

    private static ItemCatalog CloneCatalog(ItemCatalog catalog)
    {
        return new ItemCatalog
        {
            SchemaVersion = catalog.SchemaVersion,
            ServerFingerprint = catalog.ServerFingerprint,
            CatalogSha256 = catalog.CatalogSha256,
            Assets = catalog.Assets.Select(asset => new PassiveAssetReference
            {
                Key = asset.Key,
                Kind = asset.Kind,
                Sha256 = asset.Sha256,
                SizeBytes = asset.SizeBytes,
            }).ToList(),
            Items = catalog.Items.Select(item => new ItemDefinition
            {
                Key = item.Key,
                DisplayName = item.DisplayName,
                BuiltInPrefab = item.BuiltInPrefab,
                AssetKeys = item.AssetKeys.ToList(),
            }).ToList(),
        };
    }
}
