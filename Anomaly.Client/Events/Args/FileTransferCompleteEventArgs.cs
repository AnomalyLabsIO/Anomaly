namespace Anomaly.Client.Events.Args;

public class FileTransferCompleteEventArgs : EventArgs
{
    public FileTransferCompleteEventArgs(bool success, int fileCount)
    {
        Success = success;
        FileCount = fileCount;
    }

    public bool Success { get; }
    public int FileCount { get; }
}
