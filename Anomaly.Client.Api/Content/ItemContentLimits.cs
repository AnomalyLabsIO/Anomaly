using System.Collections.Generic;

namespace Anomaly.Client.Api.Content;

public static class ItemContentLimits
{
    public const int MaxItems = 256;
    public const int MaxAssets = 512;
    public const long MaxAssetBytes = 64L * 1024 * 1024;
    public const long MaxCatalogBytes = 256L * 1024 * 1024;
    public const int MaxTextLength = 256;
    public const int MaxKeyLength = 1319;
    public const int MaxAssetsPerItem = 8;
    public const int MaxTransferChunkBytes = 64 * 1024;

    /// <summary>
    /// Public-v1 may decorate only these passive, framework-resolved stock item
    /// families. The wire schema cannot name an arbitrary Unity prefab.
    /// </summary>
    public static IReadOnlySet<string> ApprovedBuiltInPrefabs { get; } =
        new HashSet<string>(new[] { "Coin", "Keycard", "Medkit", "Flashlight", "Radio" },
            StringComparer.Ordinal);
}
