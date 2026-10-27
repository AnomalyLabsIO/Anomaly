namespace Anomaly.Shared.Content.Packs;

/// <summary>Typed presentation data consumed by the server before its item catalog freezes.</summary>
public sealed class PackItemDefinition
{
    public string Key = string.Empty;
    /// <summary>Canonical stock ItemType name; the server validates the actual supported enum.</summary>
    public string BaseTemplate = string.Empty;
    public string DisplayName = string.Empty;
    public string Description = string.Empty;
    public string IconAddress;
}
