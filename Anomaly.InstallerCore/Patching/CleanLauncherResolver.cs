using System.Net.Http;

namespace Anomaly.InstallerCore.Patching;

/// <summary>
/// Places the correct clean (Themida-free) SCPSL.exe into the installer directory based on the
/// selected <see cref="VersionRange"/>'s Unity version. For the Unity version matching the
/// embedded launcher, the installer's own <c>SCPSL.exe</c> resource is extracted. For any other
/// Unity version, the clean launcher is downloaded from <see cref="VersionRange.cleanLauncherUrl"/>
/// and cached under <c>%APPDATA%/Anomaly/clean_launchers/&lt;unityVersion&gt;/SCPSL.exe</c> so
/// repeat installs don't hit the network.
/// </summary>
public static class CleanLauncherResolver
{
    /// <summary>
    /// Unity version the installer's embedded SCPSL.exe was built against. Keep in sync with
    /// the actual resource under <c>Anomaly.Installer/Resources/SCPSL.exe</c>.
    /// </summary>
    public const string EmbeddedUnityVersion = "6000.0.43f1";

    /// <summary>
    /// Writes the correct clean SCPSL.exe to <paramref name="installerDir"/>. Returns the path
    /// written to (for logging).
    /// </summary>
    public static string PlaceCleanLauncher(VersionRange range, string installerDir)
    {
        string destPath = Path.Combine(installerDir, "SCPSL.exe");

        if (string.Equals(range.unityVersion, EmbeddedUnityVersion, StringComparison.OrdinalIgnoreCase))
        {
            EmbeddedResources.ExtractToFile("SCPSL.exe", destPath);
            Console.WriteLine($"  Using embedded clean launcher (Unity {EmbeddedUnityVersion}).");
            return destPath;
        }

        if (string.IsNullOrWhiteSpace(range.cleanLauncherUrl))
        {
            throw new InstallerException(
                $"patchinfo.json is missing cleanLauncherUrl for Unity {range.unityVersion} " +
                $"(range {range.versionLabel}). Installer can't produce a clean SCPSL.exe for " +
                "this version — re-run with --no-anomaly to skip clean-launcher substitution.");
        }

        string cachePath = GetCachePath(range.unityVersion);

        if (!TryUseCache(cachePath, range.cleanLauncherSha256))
        {
            DownloadToCache(range.cleanLauncherUrl!, cachePath, range.cleanLauncherSha256, range.unityVersion);
        }
        else
        {
            Console.WriteLine($"  Using cached clean launcher (Unity {range.unityVersion}): {cachePath}");
        }

        File.Copy(cachePath, destPath, overwrite: true);
        return destPath;
    }

    private static string GetCachePath(string unityVersion)
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string dir = Path.Combine(appData, "Anomaly", "clean_launchers", unityVersion);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "SCPSL.exe");
    }

    private static bool TryUseCache(string cachePath, string? expectedSha256)
    {
        if (!File.Exists(cachePath))
            return false;

        if (string.IsNullOrWhiteSpace(expectedSha256))
            return true;  // No hash to verify against — trust the cache.

        try
        {
            string actualHash = ResourceIntegrity.ComputeFileSha256(cachePath);
            return string.Equals(actualHash, expectedSha256, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void DownloadToCache(string url, string cachePath, string? expectedSha256, string unityVersion)
    {
        Console.WriteLine($"  Downloading clean launcher for Unity {unityVersion}...");
        Console.WriteLine($"    {url}");

        byte[] bytes;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            bytes = http.GetByteArrayAsync(url).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            throw new InstallerException(
                $"Failed to download clean launcher for Unity {unityVersion}: {ex.Message}. " +
                $"Check your connection, or download {url} manually into {cachePath}.");
        }

        if (!string.IsNullOrWhiteSpace(expectedSha256))
        {
            string actualHash;
            using (var sha = System.Security.Cryptography.SHA256.Create())
                actualHash = Convert.ToHexString(sha.ComputeHash(bytes));

            if (!string.Equals(actualHash, expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InstallerException(
                    $"Clean launcher hash mismatch (Unity {unityVersion}).\n" +
                    $"  Expected: {expectedSha256}\n" +
                    $"  Actual:   {actualHash}\n" +
                    "Refusing to use the downloaded binary.");
            }
        }
        else
        {
            ConsoleUI.PrintWarning(
                $"No SHA-256 pinned for the clean launcher (Unity {unityVersion}). " +
                "Using the download as-is; consider pinning a hash in patchinfo.json.");
        }

        File.WriteAllBytes(cachePath, bytes);
        Console.WriteLine($"  Cached at {cachePath} ({ConsoleUI.FormatBytes(bytes.Length)}).");
    }
}
