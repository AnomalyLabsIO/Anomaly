using System.IO.Compression;

namespace Anomaly.Installer.MelonLoader;

internal static class MelonLoaderInstaller
{
    private const string MelonLoaderUrl =
        "https://github.com/LavaGang/MelonLoader/releases/download/v0.7.2/MelonLoader.x64.zip";

    private const string ZipFileName = "MelonLoader.x64.zip";

    /// <summary>
    /// Downloads and installs MelonLoader 0.7.2 to the game directory.
    /// Always uses the Windows x64 build since SCPSL runs through Proton on Linux.
    /// </summary>
    public static void Install(string gameDir)
    {
        string melonLoaderDir = Path.Combine(gameDir, "MelonLoader");
        if (Directory.Exists(melonLoaderDir))
        {
            Console.WriteLine("  MelonLoader directory already exists, skipping download.");
            return;
        }

        string zipPath = Path.Combine(gameDir, ZipFileName);

        DownloadFile(MelonLoaderUrl, zipPath);

        Console.WriteLine("  Extracting...");
        ZipFile.ExtractToDirectory(zipPath, gameDir, overwriteFiles: true);

        File.Delete(zipPath);
    }

    private static void DownloadFile(string url, string destPath)
    {
        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromMinutes(5);

        using var response = client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).Result;
        response.EnsureSuccessStatusCode();

        long? totalBytes = response.Content.Headers.ContentLength;
        using var contentStream = response.Content.ReadAsStreamAsync().Result;
        using var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192);

        byte[] buffer = new byte[8192];
        long totalRead = 0;
        int bytesRead;

        while ((bytesRead = contentStream.Read(buffer, 0, buffer.Length)) > 0)
        {
            fileStream.Write(buffer, 0, bytesRead);
            totalRead += bytesRead;
            if (totalBytes.HasValue)
            {
                ConsoleUI.PrintProgressBar("Downloading",
                    totalRead, totalBytes.Value,
                    ConsoleUI.FormatBytes(totalRead));
            }
        }

        // If no content-length header was available, print final size
        if (!totalBytes.HasValue)
            Console.WriteLine($"  Downloaded {ConsoleUI.FormatBytes(totalRead)}");
    }
}
