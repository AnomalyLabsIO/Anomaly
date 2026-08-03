using System;
using System.Collections.Generic;
using System.Linq;

namespace Anomaly.Client.Api.Content;

/// <summary>
/// Bounded, display-ready summary of the proposed catalog and the content that
/// differs from the currently active catalog.
/// </summary>
public sealed class ItemCatalogDiff
{
    public int FileCount { get; init; }
    public long AggregateBytes { get; init; }
    public long DownloadBytes { get; init; }
    public IReadOnlyList<string> AddedAssets { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ChangedAssets { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> RemovedAssets { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ChangedItems { get; init; } = Array.Empty<string>();
    public bool HasChanges => AddedAssets.Count != 0 || ChangedAssets.Count != 0 ||
                              RemovedAssets.Count != 0 || ChangedItems.Count != 0;

    public static ItemCatalogDiff Compare(ItemCatalog current, ItemCatalog proposed)
    {
        if (proposed == null) throw new ArgumentNullException(nameof(proposed));

        var proposedErrors = ItemCatalogValidator.Validate(proposed);
        if (proposedErrors.Count != 0)
            throw new ArgumentException("Proposed catalog is invalid: " + string.Join(" ", proposedErrors),
                nameof(proposed));
        if (current != null)
        {
            var currentErrors = ItemCatalogValidator.Validate(current);
            if (currentErrors.Count != 0)
                throw new ArgumentException("Current catalog is invalid: " + string.Join(" ", currentErrors),
                    nameof(current));
        }

        var currentAssets = AssetDictionary(current == null ? null : current.Assets);
        var proposedAssets = AssetDictionary(proposed.Assets);
        var currentItems = ItemDictionary(current == null ? null : current.Items);
        var proposedItems = ItemDictionary(proposed.Items);

        var addedAssets = proposedAssets.Keys.Except(currentAssets.Keys, StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var removedAssets = currentAssets.Keys.Except(proposedAssets.Keys, StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var changedAssets = proposedAssets.Keys.Intersect(currentAssets.Keys, StringComparer.Ordinal)
            .Where(key => !AssetEquals(currentAssets[key], proposedAssets[key]))
            .OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var changedItems = currentItems.Keys.Union(proposedItems.Keys, StringComparer.Ordinal)
            .Where(key => !currentItems.TryGetValue(key, out var oldItem) ||
                          !proposedItems.TryGetValue(key, out var newItem) ||
                          !ItemEquals(oldItem, newItem))
            .OrderBy(value => value, StringComparer.Ordinal).ToArray();

        return new ItemCatalogDiff
        {
            FileCount = proposedAssets.Count,
            AggregateBytes = proposedAssets.Values.Sum(value => value.SizeBytes),
            DownloadBytes = addedAssets.Concat(changedAssets).Sum(key => proposedAssets[key].SizeBytes),
            AddedAssets = addedAssets,
            ChangedAssets = changedAssets,
            RemovedAssets = removedAssets,
            ChangedItems = changedItems,
        };
    }

    private static bool AssetEquals(PassiveAssetReference first, PassiveAssetReference second) =>
        first.Kind == second.Kind && first.SizeBytes == second.SizeBytes &&
        string.Equals(first.Sha256, second.Sha256, StringComparison.OrdinalIgnoreCase);

    private static bool ItemEquals(ItemDefinition first, ItemDefinition second)
    {
        return string.Equals(first.DisplayName, second.DisplayName, StringComparison.Ordinal) &&
               string.Equals(first.BuiltInPrefab, second.BuiltInPrefab, StringComparison.Ordinal) &&
               (first.AssetKeys ?? new List<string>()).OrderBy(value => value, StringComparer.Ordinal)
               .SequenceEqual((second.AssetKeys ?? new List<string>()).OrderBy(value => value, StringComparer.Ordinal),
                   StringComparer.Ordinal);
    }

    private static Dictionary<string, PassiveAssetReference> AssetDictionary(
        IEnumerable<PassiveAssetReference> assets)
    {
        var result = new Dictionary<string, PassiveAssetReference>(StringComparer.Ordinal);
        if (assets == null) return result;
        foreach (var asset in assets)
            if (asset != null)
                result.Add(asset.Key, asset);
        return result;
    }

    private static Dictionary<string, ItemDefinition> ItemDictionary(IEnumerable<ItemDefinition> items)
    {
        var result = new Dictionary<string, ItemDefinition>(StringComparer.Ordinal);
        if (items == null) return result;
        foreach (var item in items)
            if (item != null)
                result.Add(item.Key, item);
        return result;
    }
}
