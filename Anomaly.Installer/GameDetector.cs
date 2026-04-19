using System.Runtime.Versioning;
using Anomaly.Installer.Platform;
using Microsoft.Win32;

namespace Anomaly.Installer;

/// <summary>
/// Best-effort auto-detection of the vanilla SCPSL install. Checks (in order):
/// the remembered path at %APPDATA%/Anomaly/vanilla_path.txt, the Steam registry
/// entry on Windows, and a handful of common filesystem locations.
/// Returns the path to SCPSL.exe, or null if nothing sensible was found.
/// </summary>
internal static class GameDetector
{
    private const string SteamFolderName = "SCP Secret Laboratory";
    private const string ScpslExe = "SCPSL.exe";

    public static string? TryFindVanilla(PlatformInfo platform)
    {
        // 1. Remembered from a previous install.
        string? remembered = TryRememberedPath();
        if (remembered != null) return remembered;

        // 2. Steam registry on Windows (supports both native and WOW6432Node).
        // Using OperatingSystem.IsWindows() rather than the PlatformInfo flag so the
        // CA1416 analyzer can see the guard and we avoid platform-support warnings.
        if (OperatingSystem.IsWindows())
        {
            string? viaRegistry = TryWindowsSteamRegistry();
            if (viaRegistry != null) return viaRegistry;
        }

        // 3. Common filesystem fallbacks per platform.
        foreach (string candidate in CandidatePaths(platform))
        {
            string exe = Path.Combine(candidate, ScpslExe);
            if (File.Exists(exe)) return exe;
        }

        return null;
    }

    private static string? TryRememberedPath()
    {
        try
        {
            string file = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Anomaly", "vanilla_path.txt");
            if (!File.Exists(file)) return null;

            string dir = File.ReadAllText(file).Trim();
            if (string.IsNullOrEmpty(dir)) return null;

            string exe = Path.Combine(dir, ScpslExe);
            return File.Exists(exe) ? exe : null;
        }
        catch
        {
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static string? TryWindowsSteamRegistry()
    {
        // Registry access is Windows-only; guarded by the caller, but we additionally
        // wrap in try/catch because the registry lib throws PlatformNotSupported on non-Windows.
        try
        {
            string?[] roots =
            {
                ReadSteamInstallPath(RegistryHive.LocalMachine, RegistryView.Registry64),
                ReadSteamInstallPath(RegistryHive.LocalMachine, RegistryView.Registry32),
                ReadSteamInstallPath(RegistryHive.CurrentUser,  RegistryView.Default),
            };

            foreach (string? steamPath in roots)
            {
                if (string.IsNullOrEmpty(steamPath)) continue;
                string exe = Path.Combine(steamPath, "steamapps", "common", SteamFolderName, ScpslExe);
                if (File.Exists(exe)) return exe;
            }
        }
        catch
        {
            // Registry unavailable — fall through.
        }
        return null;
    }

    [SupportedOSPlatform("windows")]
    private static string? ReadSteamInstallPath(RegistryHive hive, RegistryView view)
    {
        using var baseKey = RegistryKey.OpenBaseKey(hive, view);
        // HKLM\SOFTWARE\Valve\Steam is the native 64-bit location; HKLM\SOFTWARE\WOW6432Node\Valve\Steam
        // is the 32-bit view. HKCU\SOFTWARE\Valve\Steam is the per-user fallback.
        string[] subKeys = { @"SOFTWARE\Valve\Steam", @"SOFTWARE\WOW6432Node\Valve\Steam" };
        foreach (string sub in subKeys)
        {
            using var key = baseKey.OpenSubKey(sub);
            string? path = key?.GetValue("InstallPath") as string
                ?? key?.GetValue("SteamPath") as string;
            if (!string.IsNullOrEmpty(path)) return path;
        }
        return null;
    }

    private static IEnumerable<string> CandidatePaths(PlatformInfo platform)
    {
        if (platform.IsWindows)
        {
            string? pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string? pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            foreach (string root in new[] { pf86, pf })
            {
                if (string.IsNullOrEmpty(root)) continue;
                yield return Path.Combine(root, "Steam", "steamapps", "common", SteamFolderName);
            }
        }
        else if (platform.IsLinux)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);

            if (platform.LinuxRuntime != null)
            {
                foreach (string candidate in platform.LinuxRuntime.GetGameInstallCandidates(SteamFolderName))
                {
                    if (seen.Add(candidate))
                        yield return candidate;
                }
            }

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (string fallback in new[]
                     {
                         Path.Combine(home, ".steam", "steam", "steamapps", "common", SteamFolderName),
                         Path.Combine(home, ".local", "share", "Steam", "steamapps", "common", SteamFolderName),
                     })
            {
                if (seen.Add(fallback))
                    yield return fallback;
            }
        }
    }
}
