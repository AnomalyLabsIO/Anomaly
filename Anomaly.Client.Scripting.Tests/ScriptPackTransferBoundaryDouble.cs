using Anomaly.Shared.Assets;

namespace Anomaly.Client.FileTransfer;

// ScriptPackManager ownership tests use an empty inventory. Reading an actual
// approved cache entry is covered by ScriptPackReader and transfer-flow tests.
internal static class FileTransferClient
{
    internal static bool IsFileApproved(byte[] hash, FileContentKind kind) =>
        throw new InvalidOperationException("A snapshot ownership test attempted to access file transfer.");
}
