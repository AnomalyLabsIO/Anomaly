using Anomaly.Shared.Networking.Messages;

namespace Anomaly.Shared.Networking;

/// <summary>Closed built-in allowlist, independent of third-party message registration.</summary>
internal static class ContentBootstrapPolicy
{
    internal static bool IsServerMessage(IAnomalyMessage message) =>
        message?.GetType() == typeof(FacilitySnapshotMessage) || message?.GetType() == typeof(ContentCatalogMessage) ||
        message?.GetType() == typeof(FileManifestMessage) || message?.GetType() == typeof(FileChunkMessage) ||
        message?.GetType() == typeof(FileTransferCompleteMessage) || message?.GetType() == typeof(ContentBootstrapReadyMessage);

    internal static bool IsClientMessage(IAnomalyMessage message) =>
        message?.GetType() == typeof(ContentCatalogDecisionMessage) || message?.GetType() == typeof(ContentCatalogAckMessage) ||
        message?.GetType() == typeof(FileRequestMessage) || message?.GetType() == typeof(FileAckMessage);
}
