namespace Anomaly.Shared.Scripting;

/// <summary>Limits applied before source decoding, publication, transfer and execution.</summary>
public static class ScriptPackLimits
{
    public const int ApiVersion = 1;
    public const int MaxScripts = 8;
    public const int MaxScriptBytesPerFile = 64 * 1024;
    public const int MaxScriptBytes = 256 * 1024;
    public const int MaxIndexBytes = 64 * 1024;
}
