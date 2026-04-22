namespace Anomaly.Installer.Platform;

public sealed class LinuxRuntimeInfo
{
    private LinuxRuntimeInfo()
    {
    }

    public string? SteamRootPath { get; private init; }
    public IReadOnlyList<string> SteamLibraryRoots { get; private init; } = Array.Empty<string>();
    public ProtonTool? PreferredProton { get; private init; }
    public string? WineCommand { get; private init; }
    public bool IsSteamOs { get; private init; }
    public bool IsSteamDeckHardware { get; private init; }
    public string CompatDataPath { get; private init; } = string.Empty;

    public bool HasProton => PreferredProton != null;
    public bool HasWine => !string.IsNullOrWhiteSpace(WineCommand);
    public bool HasWindowsRuntime => HasProton || HasWine;

    public static LinuxRuntimeInfo Detect()
    {
        string? steamRoot = FindSteamRoot();
        IReadOnlyList<string> libraryRoots = steamRoot != null
            ? ReadSteamLibraryRoots(steamRoot)
            : Array.Empty<string>();

        List<ProtonTool> protonTools = steamRoot != null
            ? DiscoverProtonTools(steamRoot, libraryRoots)
            : new List<ProtonTool>();

        return new LinuxRuntimeInfo
        {
            SteamRootPath = steamRoot,
            SteamLibraryRoots = libraryRoots,
            PreferredProton = protonTools
                .OrderByDescending(tool => tool.Priority)
                .ThenBy(tool => tool.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(),
            WineCommand = ProbeWineCommand(),
            IsSteamOs = ReadOsReleaseValue("ID")?.Equals("steamos", StringComparison.OrdinalIgnoreCase) == true,
            IsSteamDeckHardware = IsDeckHardware(),
            CompatDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Anomaly",
                "Proton",
                "compatdata",
                "installer")
        };
    }

    public IEnumerable<string> GetGameInstallCandidates(string steamFolderName)
    {
        foreach (string libraryRoot in SteamLibraryRoots)
        {
            yield return Path.Combine(libraryRoot, "steamapps", "common", steamFolderName);
        }
    }

    private static string? FindSteamRoot()
    {
        foreach (string candidate in GetSteamRootCandidates())
        {
            if (!Directory.Exists(candidate))
                continue;

            if (File.Exists(Path.Combine(candidate, "steamapps", "libraryfolders.vdf")))
                return candidate;
        }

        foreach (string candidate in GetSteamRootCandidates())
        {
            if (Directory.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static IReadOnlyList<string> ReadSteamLibraryRoots(string steamRoot)
    {
        var roots = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        AddLibraryRoot(roots, seen, steamRoot);

        string libraryFile = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(libraryFile))
            return roots;

        try
        {
            Dictionary<string, string> data = SteamKeyValueParser.ParseFile(libraryFile);
            foreach ((string key, string value) in data)
            {
                if (!key.StartsWith("libraryfolders.", StringComparison.OrdinalIgnoreCase)
                    || !key.EndsWith(".path", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                AddLibraryRoot(roots, seen, value);
            }
        }
        catch
        {
            // Best-effort only — fall back to the default library root.
        }

        return roots;
    }

    private static void AddLibraryRoot(List<string> roots, HashSet<string> seen, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        string normalized;
        try
        {
            normalized = Path.GetFullPath(path);
        }
        catch
        {
            return;
        }

        if (!Directory.Exists(Path.Combine(normalized, "steamapps")))
            return;

        if (seen.Add(normalized))
            roots.Add(normalized);
    }

    private static List<ProtonTool> DiscoverProtonTools(string steamRoot, IReadOnlyList<string> libraryRoots)
    {
        var tools = new List<ProtonTool>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (string libraryRoot in libraryRoots)
        {
            string commonDir = Path.Combine(libraryRoot, "steamapps", "common");
            if (!Directory.Exists(commonDir))
                continue;

            foreach (string dir in Directory.GetDirectories(commonDir))
            {
                string launcherPath = Path.Combine(dir, "proton");
                if (!File.Exists(launcherPath))
                    continue;

                string normalized = Path.GetFullPath(dir);
                if (!seen.Add(normalized))
                    continue;

                tools.Add(new ProtonTool(Path.GetFileName(dir), normalized, isCustom: false));
            }
        }

        string customToolsDir = Path.Combine(steamRoot, "compatibilitytools.d");
        if (Directory.Exists(customToolsDir))
        {
            foreach (string dir in Directory.GetDirectories(customToolsDir))
            {
                string launcherPath = Path.Combine(dir, "proton");
                if (!File.Exists(launcherPath))
                    continue;

                string normalized = Path.GetFullPath(dir);
                if (!seen.Add(normalized))
                    continue;

                tools.Add(new ProtonTool(Path.GetFileName(dir), normalized, isCustom: true));
            }
        }

        return tools;
    }

    private static string? ProbeWineCommand()
    {
        foreach (string candidate in new[] { "wine", "wine64" })
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(candidate, "--version")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(psi);
                process?.WaitForExit(5000);
                if (process?.ExitCode == 0)
                    return candidate;
            }
            catch
            {
                // Try the next candidate.
            }
        }

        return null;
    }

    private static IEnumerable<string> GetSteamRootCandidates()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
            yield break;

        yield return Path.Combine(home, ".steam", "root");
        yield return Path.Combine(home, ".steam", "steam");
        yield return Path.Combine(home, ".local", "share", "Steam");
        yield return Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".steam", "root");
        yield return Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam");
    }

    private static string? ReadOsReleaseValue(string key)
    {
        foreach (string path in new[] { "/etc/os-release", "/usr/lib/os-release" })
        {
            if (!File.Exists(path))
                continue;

            try
            {
                foreach (string rawLine in File.ReadLines(path))
                {
                    string line = rawLine.Trim();
                    if (line.Length == 0 || line[0] == '#')
                        continue;

                    int separator = line.IndexOf('=');
                    if (separator <= 0)
                        continue;

                    if (!line[..separator].Equals(key, StringComparison.Ordinal))
                        continue;

                    return Unquote(line[(separator + 1)..]);
                }
            }
            catch
            {
                // Best-effort only.
            }
        }

        return null;
    }

    private static bool IsDeckHardware()
    {
        try
        {
            string vendorPath = "/sys/devices/virtual/dmi/id/sys_vendor";
            string productPath = "/sys/devices/virtual/dmi/id/product_name";
            if (!File.Exists(vendorPath) || !File.Exists(productPath))
                return false;

            string vendor = File.ReadAllText(vendorPath).Trim();
            string product = File.ReadAllText(productPath).Trim();

            return vendor.Equals("Valve", StringComparison.OrdinalIgnoreCase)
                && (product.Equals("Jupiter", StringComparison.OrdinalIgnoreCase)
                    || product.Equals("Galileo", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private static string Unquote(string value)
    {
        value = value.Trim();
        if (value.Length >= 2)
        {
            if (value[0] == '"' && value[^1] == '"')
                return value[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\");

            if (value[0] == '\'' && value[^1] == '\'')
                return value[1..^1];
        }

        return value;
    }
}
