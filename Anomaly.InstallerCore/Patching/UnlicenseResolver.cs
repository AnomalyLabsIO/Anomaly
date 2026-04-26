using Anomaly.InstallerCore.Resources;

namespace Anomaly.InstallerCore.Patching;

/// <summary>
/// Resolves a verified local <c>unlicense.exe</c> for the Themida unpacking step. The binary
/// used to ship embedded in the InstallerCore assembly (~49 MB), but is now hosted in the
/// AnomalyLabs resource repo and downloaded on first use. Cached at
/// <c>%APPDATA%/Anomaly/cache/patching/unlicense.exe</c> for repeat installs.
/// </summary>
public static class UnlicenseResolver
{
    private const string ResourceRelativePath = "patching/unlicense.exe";

    /// <summary>
    /// Returns the path to a verified <c>unlicense.exe</c>. Throws <see cref="InstallerException"/>
    /// with a manual-download hint if the cache is empty and the network fetch fails.
    /// </summary>
    public static string GetOrDownload(AnomalyResourcesClient client, RemoteManifest manifest, CancellationToken ct = default)
    {
        string? expectedSha = manifest.GetSha256(ResourceRelativePath);
        if (string.IsNullOrWhiteSpace(expectedSha))
        {
            ConsoleUI.PrintWarning(
                $"No SHA-256 pinned for {ResourceRelativePath} in the active manifest. " +
                "The downloaded copy will not be integrity-checked.");
        }

        return client.GetOrDownloadAsync(
                ResourceRelativePath,
                expectedSha,
                timeout: TimeSpan.FromMinutes(5),
                progressLabel: "Downloading unlicense",
                ct)
            .GetAwaiter().GetResult();
    }
}
