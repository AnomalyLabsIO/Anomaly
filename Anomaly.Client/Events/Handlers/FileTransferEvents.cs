using Anomaly.Client.Events.Args;

namespace Anomaly.Client.Events.Handlers;

public static class FileTransferEvents
{
    public static event AnEventHandler<FileTransferProgressEventArgs> Progress;
    public static event AnEventHandler<FileTransferCompleteEventArgs> Complete;

    internal static void OnProgress(FileTransferProgressEventArgs args) => EventInvoker.Invoke(Progress, args);
    internal static void OnComplete(FileTransferCompleteEventArgs args) => EventInvoker.Invoke(Complete, args);
}
