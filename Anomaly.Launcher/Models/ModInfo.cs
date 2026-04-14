namespace Anomaly.Launcher.Models;

public class ModInfo
{
    public required string FileName { get; init; }
    public required string FilePath { get; set; }
    public required bool IsActive { get; set; }
    public string? AssemblyName { get; init; }
    public string? Version { get; init; }
    public string? TargetGameVersion { get; init; }
}
