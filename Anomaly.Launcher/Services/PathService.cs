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
    public string VanillaPathFile { get; }
    public string InstalledModsFile { get; }
    public string CatalogCacheFile { get; }
    public string GameBuildsCacheFile { get; }
    public string GameLogPath { get; }

    public PathService()
    {
        LauncherDir = AppContext.BaseDirectory;
        ModsDir = Path.Combine(LauncherDir, "Mods");
        UserLibsDir = Path.Combine(LauncherDir, "UserLibs");
        MelonLoaderDir = Path.Combine(LauncherDir, "MelonLoader");
        // Per-mod runtime data (config, assets) lives under MelonLoader's per-namespace UserData root.
        UserDataDir = Path.Combine(MelonLoaderDir, "UserData", "Anomaly");
        AnomalyDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Anomaly");
        CrashReportsDir = Path.Combine(AnomalyDataDir, "CrashReports");
        UserJsonPath = Path.Combine(AnomalyDataDir, "user.json");
        VanillaPathFile = Path.Combine(AnomalyDataDir, "vanilla_path.txt");
        InstalledModsFile = Path.Combine(AnomalyDataDir, "installed_mods.json");
        CatalogCacheFile = Path.Combine(AnomalyDataDir, "catalog_cache.json");
        GameBuildsCacheFile = Path.Combine(AnomalyDataDir, "game_builds_cache.json");
        GameLogPath = Path.Combine(LauncherDir, "Latest.log");

        MigrateLegacyVanillaPath();
    }

    /// <summary>
    /// Older Launcher/Installer builds stored vanilla_path.txt next to the launcher
    /// executable. If the legacy file exists and the new one doesn't, carry it forward.
    /// </summary>
    private void MigrateLegacyVanillaPath()
    {
        var legacy = Path.Combine(LauncherDir, "vanilla_path.txt");
        if (!File.Exists(legacy) || File.Exists(VanillaPathFile))
            return;

        try
        {
            Directory.CreateDirectory(AnomalyDataDir);
            File.Copy(legacy, VanillaPathFile);
        }
        catch
        {
            // Best-effort migration — if it fails, the user will be re-prompted for the path.
        }
    }

    // --- Vanilla / Steam paths (computed on access, not constructor) ---

    public string? VanillaDir
    {
        get
        {
            var exe = GetVanillaExePath();
            return exe != null ? Path.GetDirectoryName(exe) : null;
        }
    }

    /// <summary>
    /// Derives the steamapps directory from the vanilla path.
    /// Vanilla lives in steamapps/common/SCP Secret Laboratory/ — go up two levels.
    /// </summary>
    public string? SteamappsDir
    {
        get
        {
            var vanillaDir = VanillaDir;
            if (vanillaDir == null) return null;

            // Go up past "SCP Secret Laboratory" and "common" to reach "steamapps"
            var commonDir = Path.GetDirectoryName(vanillaDir);
            var steamappsDir = commonDir != null ? Path.GetDirectoryName(commonDir) : null;

            // Validate it looks right
            if (steamappsDir != null && Directory.Exists(steamappsDir))
                return steamappsDir;

            return null;
        }
    }

    public string? AppManifestPath
    {
        get
        {
            var steamapps = SteamappsDir;
            if (steamapps == null) return null;

            var path = Path.Combine(steamapps, "appmanifest_700330.acf");
            return File.Exists(path) ? path : null;
        }
    }

    public string? GetVanillaGameAssemblyPath()
    {
        var dir = VanillaDir;
        if (dir == null) return null;

        var path = Path.Combine(dir, "GameAssembly.dll");
        return File.Exists(path) ? path : null;
    }

    public string? GetVanillaExePath()
    {
        if (!File.Exists(VanillaPathFile))
            return null;

        var path = File.ReadAllText(VanillaPathFile).Trim();
        if (string.IsNullOrEmpty(path))
            return null;

        // vanilla_path.txt may contain the directory or the exe path
        if (File.Exists(path))
            return path;

        var exePath = Path.Combine(path, "SCPSL.exe");
        return File.Exists(exePath) ? exePath : null;
    }

    public string GetModdedExePath()
    {
        return Path.Combine(LauncherDir, "SCPSL.exe");
    }

    public void SaveVanillaPath(string directoryOrExePath)
    {
        // Normalize: always store the directory
        string dir;
        if (File.Exists(directoryOrExePath) &&
            directoryOrExePath.EndsWith("SCPSL.exe", StringComparison.OrdinalIgnoreCase))
        {
            dir = Path.GetDirectoryName(directoryOrExePath)!;
        }
        else
        {
            dir = directoryOrExePath;
        }

        Directory.CreateDirectory(AnomalyDataDir);
        File.WriteAllText(VanillaPathFile, dir);
    }
}
