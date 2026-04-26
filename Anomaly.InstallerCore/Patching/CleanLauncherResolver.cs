using Anomaly.InstallerCore.Resources;

namespace Anomaly.InstallerCore.Patching;

/// <summary>
/// Places the correct clean (Themida-free) SCPSL.exe into the installer directory based on the
/// selected <see cref="VersionRange"/>'s Unity version. Always pulls through the shared
/// <see cref="AnomalyResourcesClient"/> cache at
/// <c>%APPDATA%/Anomaly/cache/clean_launchers/&lt;unityVersion&gt;/SCPSL.exe</c>; SHA-256 is verified
/// against the active <see cref="RemoteManifest"/> when available, then against the per-range
/// <see cref="VersionRange.cleanLauncherSha256"/> as a secondary check.
/// </summary>
public static class CleanLauncherResolver
{
    /// <summary>
    /// Writes the correct clean SCPSL.exe to <paramref name="installerDir"/>. Returns the path
    /// written to (for logging).
    /// </summary>
    public static string PlaceCleanLauncher(
        VersionRange range,
        string installerDir,
        AnomalyResourcesClient client,
        RemoteManifest manifest,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(range.cleanLauncherUrl))
        {
            throw new InstallerException(
                $"patchinfo.json is missing cleanLauncherUrl for Unity {range.unityVersion} " +
                $"(range {range.versionLabel}). Installer can't produce a clean SCPSL.exe for " +
                "this version — re-run with --no-anomaly to skip clean-launcher substitution.");
        }

        string relativePath = $"clean_launchers/{range.unityVersion}/SCPSL.exe";
        string? expectedSha = manifest.GetSha256(relativePath) ?? range.cleanLauncherSha256;

        string cachePath;
        if (LooksLikeAnomalyResourcesUrl(range.cleanLauncherUrl, client.BaseUrl))
        {
            cachePath = client.GetOrDownloadAsync(
                    relativePath,
                    expectedSha,
                    timeout: TimeSpan.FromSeconds(60),
                    progressLabel: "Downloading SCPSL",
                    ct)
                .GetAwaiter().GetResult();
        }
        else
        {
            // Custom mirror in patchinfo.json — bypass the AnomalyResources base URL but
            // still cache the result and verify the hash if one is pinned.
            cachePath = DownloadCustomUrl(range.cleanLauncherUrl, range.unityVersion, expectedSha, ct);
        }

        string destPath = Path.Combine(installerDir, "SCPSL.exe");
        File.Copy(cachePath, destPath, overwrite: true);
        return destPath;
    }

    /// <summary>
    /// True when <paramref name="url"/> would resolve to the same file the AnomalyResources
    /// client serves for <c>clean_launchers/&lt;unityVersion&gt;/SCPSL.exe</c>. Lets us route
    /// AnomalyLabs URLs through the shared cache while still honouring custom mirrors.
    /// </summary>
    private static bool LooksLikeAnomalyResourcesUrl(string url, string baseUrl) =>
        url.StartsWith(baseUrl, StringComparison.OrdinalIgnoreCase);

    private static string DownloadCustomUrl(string url, string unityVersion, string? expectedSha, CancellationToken ct)
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string cacheDir = Path.Combine(appData, "Anomaly", "cache", "clean_launchers", unityVersion);
        Directory.CreateDirectory(cacheDir);
        string cachePath = Path.Combine(cacheDir, "SCPSL.exe");

        if (File.Exists(cachePath) && !string.IsNullOrWhiteSpace(expectedSha))
        {
            try
            {
                string actual = ResourceIntegrity.ComputeFileSha256(cachePath);
                if (string.Equals(actual, expectedSha, StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"  Using cached clean launcher (Unity {unityVersion}): {cachePath}");
                    return cachePath;
                }
            }
            catch
            {
                // fall through and re-download
            }
        }

        Console.WriteLine($"  Downloading clean launcher for Unity {unityVersion} from {url}");
        try
        {
            HttpDownloader.DownloadFileAsync(url, cachePath, TimeSpan.FromSeconds(60), "Downloading SCPSL", ct)
                .GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            throw new InstallerException(
                $"Failed to download clean launcher for Unity {unityVersion}: {ex.Message}. " +
                $"Check your connection, or download {url} manually into {cachePath}.");
        }

        if (!string.IsNullOrWhiteSpace(expectedSha))
        {
            string actual = ResourceIntegrity.ComputeFileSha256(cachePath);
            ResourceIntegrity.VerifyHash($"clean launcher (Unity {unityVersion})", expectedSha, actual);
        }
        else
        {
            ConsoleUI.PrintWarning(
                $"No SHA-256 pinned for the clean launcher (Unity {unityVersion}). " +
                "Using the download as-is; consider pinning a hash in patchinfo.json.");
        }

        return cachePath;
    }
}
