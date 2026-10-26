using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Handlers;

public static class FileTransferEvents
{
    [ScriptEvent]
    public static event AnomalyEventHandler<FileTransferProgressEventArgs> Progress;
    [ScriptEvent]
    public static event AnomalyEventHandler<FileTransferCompleteEventArgs> Complete;

    internal static void OnProgress(FileTransferProgressEventArgs args) => EventInvoker.Invoke(Progress, args);
    internal static void OnComplete(FileTransferCompleteEventArgs args) => EventInvoker.Invoke(Complete, args);
}
