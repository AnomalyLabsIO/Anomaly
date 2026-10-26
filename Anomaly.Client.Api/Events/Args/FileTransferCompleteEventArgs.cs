using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

[ScriptType]
public class FileTransferCompleteEventArgs : EventArgs
{
    public FileTransferCompleteEventArgs(bool success, int fileCount)
    {
        Success = success;
        FileCount = fileCount;
    }

    [ScriptVisible]
    public bool Success { get; }
    [ScriptVisible]
    public int FileCount { get; }
}
