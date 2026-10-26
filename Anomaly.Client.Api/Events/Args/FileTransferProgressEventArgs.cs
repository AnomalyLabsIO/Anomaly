using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

[ScriptType]
public class FileTransferProgressEventArgs : EventArgs
{
    public FileTransferProgressEventArgs(string fileName, int completedFiles, int totalFiles,
        long receivedBytes, long totalBytes, float percent)
    {
        FileName = fileName;
        CompletedFiles = completedFiles;
        TotalFiles = totalFiles;
        ReceivedBytes = receivedBytes;
        TotalBytes = totalBytes;
        Percent = percent;
    }

    [ScriptVisible]
    public string FileName { get; }
    [ScriptVisible]
    public int CompletedFiles { get; }
    [ScriptVisible]
    public int TotalFiles { get; }
    [ScriptVisible]
    public long ReceivedBytes { get; }
    [ScriptVisible]
    public long TotalBytes { get; }
    [ScriptVisible]
    public float Percent { get; }
}
