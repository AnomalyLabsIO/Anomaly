namespace Anomaly.Client.Events.Args;

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

    public string FileName { get; }
    public int CompletedFiles { get; }
    public int TotalFiles { get; }
    public long ReceivedBytes { get; }
    public long TotalBytes { get; }
    public float Percent { get; }
}
