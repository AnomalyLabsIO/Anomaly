using System.Collections.Generic;

namespace Anomaly.Client.Api.Content;

/// <summary>
/// Declarative item definition. Active prefab hierarchies, scripts, roles, and
/// effects are deliberately absent from the public-v1 contract.
/// </summary>
public sealed class ItemDefinition
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string BuiltInPrefab { get; set; } = string.Empty;
    public List<string> AssetKeys { get; set; } = new();
}
