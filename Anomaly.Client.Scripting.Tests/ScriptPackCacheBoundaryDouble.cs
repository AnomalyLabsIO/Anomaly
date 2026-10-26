namespace Anomaly.Client.Api.FileTransfer;

// Snapshot ownership tests never read cache files. The linked manager needs the
// client-only API boundary without widening the production API's visibility.
internal static class FileCache
{
    internal static string GetFilePath(byte[] hash) =>
        throw new InvalidOperationException("A snapshot ownership test attempted to access the file cache.");
}
