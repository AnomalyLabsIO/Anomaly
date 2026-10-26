namespace Anomaly.Shared.Scripting;

/// <summary>Common allocation limits for approved script message records.</summary>
public static class ScriptMessageLimits
{
    public const int MaxPayloadBytes = 4096;
    public const int MaxFields = 32;
    public const int MaxSchemasPerPack = 32;
    public const int MaxSchemasPerCatalog = 256;
    public const int MaxStringBytes = 1024;
}
