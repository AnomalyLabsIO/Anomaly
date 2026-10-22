namespace Anomaly.Client.Api.Internal;

internal static class StatVisibility
{
    // Mirrors SyncedStatBase.SyncMode without loading the native runtime in tests.
    internal static bool CanObserve(bool hasViewer, bool isOwner, bool viewerAlive, int syncMode)
    {
        if (!hasViewer)
            return false;

        return syncMode switch
        {
            0 => isOwner,
            1 => isOwner || !viewerAlive,
            2 => true,
            _ => false,
        };
    }
}
