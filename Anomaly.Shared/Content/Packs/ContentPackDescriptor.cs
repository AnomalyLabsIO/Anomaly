namespace Anomaly.Shared.Content.Packs;

/// <summary>Authoring metadata lowered to existing typed content components; never an archive.</summary>
public sealed class ContentPackDescriptor
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion = CurrentSchemaVersion;
    public string PackId = string.Empty;
    /// <summary>Authoring/diagnostic version, not an execution-order or client compatibility policy.</summary>
    public string Version = string.Empty;
    public ContentPackScripts Scripts;
    public ContentPackAssets Assets;
    public ContentPackData Data;
}

public sealed class ContentPackScripts
{
    public string PackId = string.Empty;
    public string EntryPoint = string.Empty;
    public string Messages;
}

public sealed class ContentPackAssets
{
    public string PackId = string.Empty;
    public string Index = string.Empty;
}

public sealed class ContentPackData
{
    public string Items = string.Empty;
}
