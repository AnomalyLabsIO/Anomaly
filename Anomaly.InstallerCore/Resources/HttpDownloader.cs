namespace Anomaly.InstallerCore.Resources;

/// <summary>
/// Streaming HTTP download with optional progress reporting. Extracted from the original
/// MelonLoaderInstaller download loop so resource resolvers (clean launcher, unlicense,
/// patchinfo refresh, ...) and the MelonLoader installer can share one implementation.
/// </summary>
internal static class HttpDownloader
{
    /// <summary>
    /// Downloads <paramref name="url"/> to <paramref name="destPath"/>. Reports progress via
    /// <see cref="ConsoleUI.PrintProgressBar"/> when the response advertises Content-Length.
    /// </summary>
    public static async Task DownloadFileAsync(
        string url,
        string destPath,
        TimeSpan timeout,
        string? progressLabel = null,
        CancellationToken ct = default)
    {
        using var client = new HttpClient { Timeout = timeout };

        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        long? totalBytes = response.Content.Headers.ContentLength;
        await using var contentStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
        await using var fileStream = new FileStream(
            destPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, useAsync: true);

        byte[] buffer = new byte[8192];
        long totalRead = 0;
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct).ConfigureAwait(false);
            totalRead += bytesRead;
            if (progressLabel != null && totalBytes.HasValue)
            {
                ConsoleUI.PrintProgressBar(progressLabel,
                    totalRead, totalBytes.Value,
                    ConsoleUI.FormatBytes(totalRead));
            }
        }

        if (progressLabel != null && !totalBytes.HasValue)
            Console.WriteLine($"  Downloaded {ConsoleUI.FormatBytes(totalRead)}");
    }
}
