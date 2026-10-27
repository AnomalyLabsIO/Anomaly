#nullable disable
using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.FileTransfer;
using Anomaly.Client.FileTransfer;
using Anomaly.Client.Scripting;
using Anomaly.Shared.Assets;
using Anomaly.Shared.Content;
using Anomaly.Shared.Scripting;

namespace Anomaly.Client.Content;

/// <summary>Owns prepared source snapshots for one connection; exposes no file access to Lua.</summary>
internal static class ScriptPackManager
{
    private static IReadOnlyList<ScriptSource> _remote;

    internal static void PrepareRemote(ContentCatalog catalog)
    {
        _remote = null;
        _remote = ScriptPackReader.Read(catalog, (hash, kind) =>
        {
            if (!FileTransferClient.IsFileApproved(hash, kind))
                throw new InvalidDataException("Script file was outside the approved transfer.");
            return ContentPackDirectoryReader.ReadBounded(FileCache.GetFilePath(hash), kind == FileContentKind.ScriptIndex
                ? ScriptPackLimits.MaxIndexBytes : ScriptPackLimits.MaxScriptBytesPerFile);
        });
    }

    internal static IReadOnlyList<ScriptSource> TakeRemoteScripts()
    {
        var sources = _remote ?? throw new InvalidOperationException("Script packs have not been verified for this connection.");
        _remote = null;
        return sources;
    }

    internal static void Reset() => _remote = null;

    internal static IReadOnlyList<ScriptSource> ReadLocalScripts() =>
        ContentPackDirectoryReader.Read(Paths.ContentPacksPath);
}
