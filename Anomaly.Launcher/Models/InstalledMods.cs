using Newtonsoft.Json;

namespace Anomaly.Launcher.Models;

/// <summary>
/// Disk-persisted record of mods installed via the Anomaly platform (as opposed to those
/// loose-dropped by the user into <c>Mods/</c>). Tracks per-file paths + hashes so uninstall
/// can clean up exactly what install wrote, and so <see cref="UpdateAsync"/> can diff against
/// the new version's manifest before deleting anything.
/// </summary>
public sealed class InstalledMods
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonProperty("mods")]
    public List<InstalledMod> Mods { get; set; } = new();
}

public sealed class InstalledMod
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("version")]
    public string Version { get; set; } = string.Empty;

    [JsonProperty("installedAt")]
    public long InstalledAt { get; set; }

    [JsonProperty("files")]
    public List<InstalledFile> Files { get; set; } = new();
}

public sealed class InstalledFile
{
    /// <summary>'mod' | 'userlib' | 'userdata' (labapi files are skipped on the client).</summary>
    [JsonProperty("target")]
    public string Target { get; set; } = string.Empty;

    [JsonProperty("relPath")]
    public string RelPath { get; set; } = string.Empty;

    [JsonProperty("sha256")]
    public string Sha256 { get; set; } = string.Empty;
}
