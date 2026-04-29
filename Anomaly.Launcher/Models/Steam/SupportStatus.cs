namespace Anomaly.Launcher.Models.Steam;

/// <summary>
/// Compatibility classification for a specific SCP:SL build against the installed Anomaly patch set.
/// </summary>
public enum SupportStatus
{
    /// <summary>Game version hasn't been determined yet — we can't classify compatibility.</summary>
    Unknown,

    /// <summary>Exact match against one of patchinfo.json's versionRanges — tested & working.</summary>
    FullySupported,

    /// <summary>Above the minimum patch level but not in any explicit range — patches may still apply.</summary>
    Patchable,

    /// <summary>Below the minimum patch level, or known to be incompatible.</summary>
    Unsupported,
}
