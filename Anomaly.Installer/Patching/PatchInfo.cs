namespace Anomaly.Installer.Patching;

/// <summary>
/// Root patchinfo.json schema. Holds one or more <see cref="VersionRange"/> entries; the installer
/// selects the range whose <c>[versionFrom, versionTo]</c> brackets the detected game version.
/// The original single-range schema is no longer supported — older installs should be reinstalled.
/// </summary>
public class PatchInfo
{
    public int schemaVersion = 2;
    public VersionRange[] versionRanges = Array.Empty<VersionRange>();
}

public class VersionRange
{
    public string versionLabel = string.Empty;
    public string versionFrom = string.Empty;
    public string? versionTo = null;       // null means "no upper bound — current and newer"
    public string unityVersion = string.Empty;
    public int metadataVersion;
    public string[] compatibleBuildIds = Array.Empty<string>();

    /// <summary>
    /// URL to a clean (Themida-free) SCPSL.exe matching <see cref="unityVersion"/>. The installer
    /// downloads this file when the range's Unity version differs from the one bundled in the
    /// installer's embedded resources, and caches it under %APPDATA%/Anomaly/clean_launchers/.
    /// May be null for the range whose Unity version matches the embedded launcher.
    /// </summary>
    public string? cleanLauncherUrl = null;

    /// <summary>
    /// Optional SHA-256 of the cleanLauncherUrl payload. When set, the installer verifies the
    /// downloaded bytes before using them. When null, the download is used as-is with a warning.
    /// </summary>
    public string? cleanLauncherSha256 = null;

    public PatchMethodInfo[] patchMethods = Array.Empty<PatchMethodInfo>();
    public GameVersionMethodInfo? gameVersionMethod;
}

public class PatchMethodInfo
{
    public string name = string.Empty;
    public int patchOffset;
    public string patchData = string.Empty;
    public int? patchSize = null;
    public bool nowarning = false;
    public string? patchifnotexists = null;
}

public class GameVersionMethodInfo
{
    public string name = string.Empty;
}
