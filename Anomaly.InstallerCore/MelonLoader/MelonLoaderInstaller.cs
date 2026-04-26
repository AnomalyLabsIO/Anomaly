using System.IO.Compression;
using Anomaly.InstallerCore.Resources;
using Newtonsoft.Json;

namespace Anomaly.InstallerCore.MelonLoader;

public static class MelonLoaderInstaller
{
    private const string ConfigResource = "melonloader.json";
    private const string ZipFileName = "MelonLoader.x64.zip";
    private static readonly string[] RequiredFiles =
    {
        Path.Combine("MelonLoader", "net6", "MelonLoader.dll"),
        "version.dll"
    };

    /// <summary>
    /// Downloads and installs MelonLoader to the game directory, using the version and URL
    /// pinned in the embedded melonloader.json. Always uses the Windows x64 build since
    /// SCPSL runs through Proton on Linux.
    /// </summary>
    public static void Install(string gameDir)
    {
        MelonLoaderConfig config = LoadConfig();

        if (HasRequiredFiles(gameDir))
        {
            Console.WriteLine("  MelonLoader files already exist, skipping download.");
            return;
        }

        string melonLoaderDir = Path.Combine(gameDir, "MelonLoader");
        if (Directory.Exists(melonLoaderDir))
            ConsoleUI.PrintWarning("Incomplete MelonLoader install detected. Re-installing required files.");

        string zipPath = Path.Combine(gameDir, ZipFileName);

        HttpDownloader.DownloadFileAsync(
                config.downloadUrl,
                zipPath,
                TimeSpan.FromMinutes(config.downloadTimeoutMinutes),
                progressLabel: "Downloading")
            .GetAwaiter().GetResult();

        if (!string.IsNullOrWhiteSpace(config.sha256))
        {
            Console.WriteLine("  Verifying download integrity...");
            string actual = ResourceIntegrity.ComputeFileSha256(zipPath);
            ResourceIntegrity.VerifyHash("MelonLoader ZIP", config.sha256, actual);
        }
        else
        {
            ConsoleUI.PrintWarning(
                "No SHA-256 pinned for the MelonLoader release — skipping integrity check. " +
                "Populate melonloader.json::sha256 to enable verification.");
        }

        Console.WriteLine("  Extracting...");
        ZipFile.ExtractToDirectory(zipPath, gameDir, overwriteFiles: true);

        File.Delete(zipPath);

        VerifyInstalledFiles(gameDir);
    }

    /// <summary>
    /// Returns the pinned MelonLoader version string (e.g. "0.7.2"). Used by the
    /// caller to format step messages.
    /// </summary>
    public static string PinnedVersion()
    {
        return LoadConfig().version;
    }

    private static MelonLoaderConfig LoadConfig()
    {
        string json = EmbeddedResources.ReadText(ConfigResource);
        MelonLoaderConfig? config = JsonConvert.DeserializeObject<MelonLoaderConfig>(json);
        if (config == null || string.IsNullOrWhiteSpace(config.downloadUrl))
            throw new InstallerException("Embedded melonloader.json is missing or invalid.");
        return config;
    }

    private static bool HasRequiredFiles(string gameDir)
    {
        return RequiredFiles.All(relativePath => File.Exists(Path.Combine(gameDir, relativePath)));
    }

    private static void VerifyInstalledFiles(string gameDir)
    {
        string[] missing = RequiredFiles
            .Where(relativePath => !File.Exists(Path.Combine(gameDir, relativePath)))
            .ToArray();

        if (missing.Length == 0)
            return;

        throw new InstallerException(
            "MelonLoader install is incomplete. Missing files: " + string.Join(", ", missing));
    }

}
