namespace Anomaly.Shared.Scripting;

/// <summary>One required primitive field in a pack-owned script message.</summary>
public sealed class ScriptMessageField
{
    public string Name = string.Empty;
    public string Type = string.Empty;
    public int MaxBytes;
}
