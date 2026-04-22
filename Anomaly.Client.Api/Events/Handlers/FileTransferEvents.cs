using Anomaly.Client.Api.Events.Args;

namespace Anomaly.Client.Api.Events.Handlers;

public static class FileTransferEvents
{
    public static event AnomalyEventHandler<FileTransferProgressEventArgs> Progress;
    public static event AnomalyEventHandler<FileTransferCompleteEventArgs> Complete;

    internal static void OnProgress(FileTransferProgressEventArgs args) => EventInvoker.Invoke(Progress, args);
    internal static void OnComplete(FileTransferCompleteEventArgs args) => EventInvoker.Invoke(Complete, args);
}
