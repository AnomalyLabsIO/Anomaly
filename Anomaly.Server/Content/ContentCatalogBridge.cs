using Anomaly.Server.Api.Content;
using Anomaly.Server.FileTransfer;
using Anomaly.Server.Networking;

namespace Anomaly.Server.Content;

/// <summary>Adopts the already activated connection offer into the authenticated player API.</summary>
internal static class ContentCatalogBridge
{
    internal static void Init() { }
    internal static void Disable() => ContentGrantGate.Reset();
    internal static bool EnsureFrozen() => ContentRegistry.Freeze();

    internal static bool AdoptForPlayer(ReferenceHub hub)
    {
        if (!ConnectionContentBootstrap.TryGetCompletedCatalog(hub, out var catalog) ||
            !FileTransferSession.AdoptForPlayer(hub)) return false;
        ContentGrantGate.RecordOffer(hub.netId, catalog);
        return ContentGrantGate.TryRecordDecision(hub.netId, catalog.CatalogSha256, catalog.ManifestSha256, true) &&
            ContentGrantGate.TryBeginTransfer(hub.netId) && ContentGrantGate.TryCompleteTransfer(hub.netId) &&
            ContentActivationCoordinator.TryCommit(hub.netId, catalog.CatalogSha256, catalog.ManifestSha256,
                true, () => CustomItems.RaisePlayerReady(hub), out bool published) && published;
    }
}
