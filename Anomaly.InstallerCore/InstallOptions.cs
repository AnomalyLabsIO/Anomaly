namespace Anomaly.InstallerCore;

/// <summary>
/// Host-facing parameters for a single install run. Built from <c>InstallerArgs</c> by the
/// CLI, or directly constructed by the Launcher from its user-selected state.
/// </summary>
public sealed record InstallOptions
{
    /// <summary>Target directory that will receive the modded install.</summary>
    public required string InstallerDir { get; init; }

    /// <summary>Path to the vanilla SCPSL.exe (file or containing directory). Null to auto-detect and prompt.</summary>
    public string? VanillaPath { get; init; }

    /// <summary>When set, skip version detection and use this version directly. Format: "X.Y.Z".</summary>
    public string? TargetVersion { get; init; }

    public bool DryRun { get; init; }
    public bool SkipPatches { get; init; }
    public bool NoAnomaly { get; init; }
    public bool NoMelon { get; init; }

    /// <summary>Themida unpacker timeout in milliseconds. Null uses Unpacker's default.</summary>
    public int? UnpackTimeoutMs { get; init; }

    /// <summary>
    /// Where the vanilla source came from, recorded in the post-install sidecar. The launcher
    /// passes "steam" or "local"; the standalone CLI leaves this null and the sidecar records
    /// "unknown".
    /// </summary>
    public string? SourceKind { get; init; }

    /// <summary>
    /// Steam manifest ID (depot 700331) the source files came from. Only set when
    /// <see cref="SourceKind"/> is "steam". Persisted into the sidecar so Reinstall can
    /// re-pin the same manifest.
    /// </summary>
    public string? SourceManifestId { get; init; }

    /// <summary>
    /// Steam user ID that authenticated the depot download. SteamKit-downloaded installs need
    /// this to generate the same Unity bootstrap marker the official install script creates.
    /// </summary>
    public ulong? SourceSteamId { get; init; }
}
