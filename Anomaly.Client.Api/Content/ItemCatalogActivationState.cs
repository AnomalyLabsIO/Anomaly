using System;
using System.Collections.Generic;

namespace Anomaly.Client.Api.Content;

/// <summary>
/// Passive catalog state selected for framework-specific materialization.
/// It contains provenance only; it cannot instantiate Unity objects.
/// </summary>
public sealed class ItemCatalogActivationState
{
    public int SchemaVersion { get; set; } = 1;
    public string ServerFingerprint { get; set; } = string.Empty;
    public string CatalogSha256 { get; set; } = string.Empty;
    public DateTimeOffset ActivatedAtUtc { get; set; }
    public ItemCatalog Catalog { get; set; }
    public List<ItemContentCacheKey> Assets { get; set; } = new();
}
