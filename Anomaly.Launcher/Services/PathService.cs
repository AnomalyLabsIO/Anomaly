using Anomaly.InstallerCore;

namespace Anomaly.Launcher.Services;

public class PathService
{
    public string LauncherDir { get; }
    public string ModsDir { get; }
    public string UserLibsDir { get; }
    public string UserDataDir { get; }
    public string MelonLoaderDir { get; }
    public string AnomalyDataDir { get; }
    public string CrashReportsDir { get; }
    public string UserJsonPath { get; }
    public string InstalledModsFile { get; }
    public string CatalogCacheFile { get; }
    public string GameBuildsCacheFile { get; }
    public string GameLogPath { get; }
    public string InstallStateFile { get; }

    public PathService()
    {
        // Environment.ProcessPath returns the user-launched exe (e.g. <ModdedGameDir>/Anomaly.Launcher.exe)
        // even for single-file apps with IncludeAllContentForSelfExtract=true. AppContext.BaseDirectory
        // returns the bundle self-extract dir under %TEMP%/.net/Anomaly.Launcher/<bundleHash>/ in that
        // mode, which is wrong for everything path-relative (Mods, MelonLoader, modded SCPSL.exe).
        // Falling through to BaseDirectory only protects against the (unlikely) ProcessPath==null case.
        LauncherDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        ModsDir = Path.Combine(LauncherDir, "Mods");
        UserLibsDir = Path.Combine(LauncherDir, "UserLibs");
        MelonLoaderDir = Path.Combine(LauncherDir, "MelonLoader");
        // Per-mod runtime data (config, assets) lives under MelonLoader's per-namespace UserData root.
        UserDataDir = Path.Combine(MelonLoaderDir, "UserData", "Anomaly");
        AnomalyDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Anomaly");
        CrashReportsDir = Path.Combine(AnomalyDataDir, "CrashReports");
        UserJsonPath = Path.Combine(AnomalyDataDir, "user.json");
        InstalledModsFile = Path.Combine(AnomalyDataDir, "installed_mods.json");
        CatalogCacheFile = Path.Combine(AnomalyDataDir, "catalog_cache.json");
        GameBuildsCacheFile = Path.Combine(AnomalyDataDir, "game_builds_cache.json");
        GameLogPath = Path.Combine(LauncherDir, "Latest.log");
        InstallStateFile = InstallStateSidecar.PathFor(LauncherDir);

        CleanupLegacyVanillaPath();
    }

    /// <summary>
    /// Older launchers tracked the user's vanilla SCP:SL directory in vanilla_path.txt. The
    /// Steam-first install flow no longer needs it — the sidecar in the launcher dir carries
    /// install state. Best-effort delete of the stale file from both legacy locations.
    /// </summary>
    private void CleanupLegacyVanillaPath()
    {
        TryDelete(Path.Combine(AnomalyDataDir, "vanilla_path.txt"));
        TryDelete(Path.Combine(LauncherDir, "vanilla_path.txt"));
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best-effort.
        }
    }

    public string GetModdedExePath()
    {
        return Path.Combine(LauncherDir, "SCPSL.exe");
    }
}
