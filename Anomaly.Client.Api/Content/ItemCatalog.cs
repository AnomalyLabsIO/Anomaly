using System.Collections.Generic;

namespace Anomaly.Client.Api.Content;

public sealed class ItemCatalog
{
    public int SchemaVersion { get; set; } = 1;
    public string ServerFingerprint { get; set; } = string.Empty;
    public string CatalogSha256 { get; set; } = string.Empty;
    public List<PassiveAssetReference> Assets { get; set; } = new();
    public List<ItemDefinition> Items { get; set; } = new();
}
