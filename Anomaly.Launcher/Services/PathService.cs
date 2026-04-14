namespace Anomaly.Launcher.Services;

public class PathService
{
    public string LauncherDir { get; }
    public string ModsDir { get; }
    public string AnomalyDataDir { get; }
    public string CrashReportsDir { get; }
    public string UserJsonPath { get; }
    public string GameLogPath { get; }

    public PathService()
    {
        LauncherDir = AppContext.BaseDirectory;
        ModsDir = Path.Combine(LauncherDir, "Mods");
        AnomalyDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Anomaly");
        CrashReportsDir = Path.Combine(AnomalyDataDir, "CrashReports");
        UserJsonPath = Path.Combine(AnomalyDataDir, "user.json");
        GameLogPath = Path.Combine(LauncherDir, "Latest.log");
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
        var vanillaPathFile = Path.Combine(LauncherDir, "vanilla_path.txt");
        if (!File.Exists(vanillaPathFile))
            return null;

        var path = File.ReadAllText(vanillaPathFile).Trim();
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

        var vanillaPathFile = Path.Combine(LauncherDir, "vanilla_path.txt");
        File.WriteAllText(vanillaPathFile, dir);
    }
}
