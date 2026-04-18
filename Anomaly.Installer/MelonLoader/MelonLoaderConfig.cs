namespace Anomaly.Installer.MelonLoader;

/// <summary>
/// Pinned MelonLoader release metadata. Loaded from the embedded Resources/melonloader.json
/// so that version bumps don't require code changes — edit the JSON and rebuild.
/// </summary>
public sealed class MelonLoaderConfig
{
    public string version { get; set; } = string.Empty;
    public string downloadUrl { get; set; } = string.Empty;

    /// <summary>
    /// Expected SHA-256 of the downloaded ZIP (hex, case-insensitive). If null or empty,
    /// the installer still downloads and extracts but emits a warning that no integrity
    /// check is being performed. Fill this in once you've downloaded and hashed the release.
    /// </summary>
    public string? sha256 { get; set; }

    public int downloadTimeoutMinutes { get; set; } = 5;
}
