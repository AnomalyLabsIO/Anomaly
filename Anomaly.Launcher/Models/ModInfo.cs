namespace Anomaly.Launcher.Models;

public class ModInfo
{
    public required string FileName { get; init; }
    public required string FilePath { get; set; }
    public required bool IsActive { get; set; }
    public string? AssemblyName { get; init; }
    public string? Version { get; init; }
    public string? TargetGameVersion { get; init; }
    public IReadOnlyList<string> AssemblyReferences { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> MissingReferences { get; set; } = Array.Empty<string>();
}
