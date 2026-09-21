using System;
using System.Collections.Generic;
using Anomaly.Server.Auth;
using Anomaly.Server.FileTransfer;
using Anomaly.Server.Networking;
using Anomaly.Shared.Content;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using LabApi.Events.Arguments.PlayerEvents;
using PlayerEvents = LabApi.Events.Handlers.PlayerEvents;
using Logger = LabApi.Features.Console.Logger;

namespace Anomaly.Server.Content
{
    /// <summary>Offers an immutable catalog and commits explicit client decisions/activation.</summary>
    internal static class ContentCatalogBridge
    {
        private static bool _initialized;

        internal static void Init()
        {
            if (_initialized)
                return;
            _initialized = true;
            AnomalyMessaging.MessageReceived += HandleMessage;
            PlayerEvents.UsedItem += HandleUsedItem;
        }

        internal static void Disable()
        {
            if (_initialized)
            {
                AnomalyMessaging.MessageReceived -= HandleMessage;
                PlayerEvents.UsedItem -= HandleUsedItem;
                _initialized = false;
            }
            ContentGrantGate.Reset();
        }

        internal static bool EnsureFrozen() => ContentRegistry.Freeze();

        internal static void OfferForPlayer(ReferenceHub hub, byte[] manifestSha256)
        {
            try
            {
                EnsureFrozen();
                ContentCatalog catalog = ContentRegistry.CreateOfferCatalog(manifestSha256);
                ContentGrantGate.RecordOffer(hub.netId, catalog);
                if (!AnomalyMessaging.TrySendToClient(hub, new ContentCatalogMessage { Catalog = catalog }))
                {
                    ContentGrantGate.Remove(hub.netId);
                    throw new InvalidOperationException("The authenticated catalog offer could not be delivered.");
                }
            }
            catch (Exception exception)
            {
                if (hub != null)
                {
                    try { ContentGrantGate.Remove(hub.netId); }
                    catch { }
                    try { FileTransferSession.CleanupPlayer(hub); }
                    catch { }
                }
                Logger.Error($"ContentCatalogBridge.OfferForPlayer failed closed: {exception}");
                throw;
            }
        }

        private static void HandleMessage(ReferenceHub sender, IAnomalyMessage message)
        {
            try
            {
                switch (message)
                {
                    case ContentCatalogDecisionMessage decision:
                        HandleDecision(sender, decision);
                        break;
                    case ContentCatalogAckMessage acknowledgement:
                        HandleActivation(sender, acknowledgement);
                        break;
                }
            }
            catch (Exception exception)
            {
                FailProtocol(sender, "content message handling failed: " + exception.GetType().Name);
            }
        }

        private static void HandleDecision(ReferenceHub sender, ContentCatalogDecisionMessage decision)
        {
            bool approved = decision.Decision == ContentCatalogDecision.Approved;
            if (!ContentGrantGate.TryRecordDecision(
                    sender.netId,
                    decision.CatalogSha256,
                    decision.ManifestSha256,
                    approved))
            {
                RejectMismatch(sender, "catalog decision did not match the active offer");
                return;
            }

            if (!approved)
            {
                try { FileTransferSession.CleanupPlayer(sender); }
                catch (Exception exception)
                {
                    // Declining is an intentionally supported optional-content path.
                    // Cleanup failure is logged but does not turn a deliberate decline
                    // into a protocol violation.
                    try { Logger.Error($"Anomaly.Content decline cleanup failed in isolation: {exception}"); }
                    catch { }
                }
                try { Logger.Info($"Anomaly.Content: {sender.LoggedNameFromRefHub()} declined optional server content."); }
                catch { }
            }
        }

        private static void HandleActivation(ReferenceHub sender, ContentCatalogAckMessage acknowledgement)
        {
            var knownKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (CatalogEntry entry in ContentRegistry.Catalog.Entries)
                knownKeys.Add(entry.Key);
            foreach (string failedKey in acknowledgement.FailedKeys)
            {
                if (!knownKeys.Contains(failedKey))
                {
                    RejectMismatch(sender, "activation named an item outside the offered catalog");
                    return;
                }
            }

            bool activated = acknowledgement.Status == ContentCatalogActivationStatus.Activated &&
                             acknowledgement.FailedKeys.Count == 0;
            if (!ContentActivationCoordinator.TryCommit(
                    sender.netId,
                    acknowledgement.CatalogSha256,
                    acknowledgement.ManifestSha256,
                    activated,
                    () => Content.RaisePlayerReady(sender),
                    out bool publicationSucceeded))
            {
                RejectMismatch(sender, "activation did not match an approved catalog");
                return;
            }

            if (activated)
            {
                if (!publicationSucceeded)
                {
                    FailProtocol(sender, "activated content could not publish PlayerReady");
                    return;
                }
                try { Logger.Info($"Anomaly.Content: {sender.LoggedNameFromRefHub()} activated the complete item catalog."); }
                catch { }
                return;
            }

            FailProtocol(sender, "client reported failed transactional content activation");
            try
            {
                Logger.Warn(
                    $"Anomaly.Content: {sender.LoggedNameFromRefHub()} failed transactional activation" +
                    (acknowledgement.FailedKeys.Count == 0
                        ? "."
                        : $": {string.Join(", ", acknowledgement.FailedKeys)}"));
            }
            catch { }
        }

        private static void RejectMismatch(ReferenceHub sender, string reason)
        {
            FailProtocol(sender, reason);
        }

        private static void FailProtocol(ReferenceHub sender, string reason)
        {
            string playerName = "<unknown>";
            try
            {
                if (sender != null)
                    playerName = sender.LoggedNameFromRefHub();
            }
            catch { }

            // Revoke authorization and transfer ownership before any best-effort
            // logging or transport operation can fail.
            if (sender != null)
            {
                Plugin.QuarantineAuthenticatedPlayer(sender, "content-protocol-failure");
                try
                {
                    if (sender.connectionToClient == null)
                        throw new InvalidOperationException("The authenticated connection was unavailable.");
                    sender.connectionToClient.Disconnect();
                }
                catch (Exception exception)
                {
                    try { Logger.Error($"Anomaly.Content failed to disconnect {playerName}: {exception}"); }
                    catch { }
                }
            }

            try { Logger.Warn($"Anomaly.Content: rejected {playerName}: {reason}."); }
            catch { }
        }

        private static void HandleUsedItem(PlayerUsedItemEventArgs ev)
        {
            try
            {
                if (ev?.Player == null || ev.UsableItem == null ||
                    !ContentRegistry.TryGetItemLogic((int)ev.UsableItem.Type, out ItemLogicBase logic))
                    return;
                if (ContentGrantGate.GetReadiness(ev.Player.ReferenceHub.netId) != ContentReadiness.Activated)
                    return;
                logic.OnUsed(ev.Player);
            }
            catch (Exception exception)
            {
                Logger.Error($"Anomaly.Content custom item OnUsed handler failed in isolation: {exception}");
            }
        }
    }
}
