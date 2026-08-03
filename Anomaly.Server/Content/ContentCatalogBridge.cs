using System;
using System.Collections.Generic;
using Anomaly.Server.Networking;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using Logger = LabApi.Features.Console.Logger;

namespace Anomaly.Server.Content
{
    /// <summary>
    /// Server-side glue for the custom-content catalog. Mirrors <c>PlayerColorBridge</c>: on the
    /// first client becoming ready it freezes the catalog + injects server templates, then snapshots
    /// the catalog to each joining client; inbound acks update the grant gate. Late joiners get the
    /// frozen catalog replayed on their own ClientReady.
    /// </summary>
    internal static class ContentCatalogBridge
    {
        private static bool _frozen;
        private static bool _initialized;

        internal static void Init()
        {
            if (_initialized)
                return;

            _initialized = true;
            AnomalyMessaging.MessageReceived += HandleMessage;
            AnomalyMessaging.ClientReady += HandleClientReady;
        }

        internal static void Disable()
        {
            if (_initialized)
            {
                AnomalyMessaging.MessageReceived -= HandleMessage;
                AnomalyMessaging.ClientReady -= HandleClientReady;
                _initialized = false;
            }

            _frozen = false;
            ContentGrantGate.Reset();
        }

        /// <summary>
        /// Freeze the catalog and inject server templates exactly once, before any custom content
        /// goes out. Pilot trigger = first ClientReady (before that client spawns); good enough for
        /// items/roles. Effects (Phase 3) need this tied to round-restart, before any controller Awake.
        /// </summary>
        internal static void EnsureFrozen()
        {
            if (_frozen)
                return;
            _frozen = true;
            ContentRegistry.Freeze();
            ServerContentRegistrar.RegisterItems();
        }

        private static void HandleClientReady(ReferenceHub hub)
        {
            try
            {
                EnsureFrozen();
                if (ContentRegistry.Catalog.Entries.Count == 0)
                    return; // no custom content this session — nothing to sync

                AnomalyMessaging.SendToClient(hub, new ContentCatalogMessage { Catalog = ContentRegistry.Catalog });
            }
            catch (Exception ex)
            {
                Logger.Error($"ContentCatalogBridge.HandleClientReady: {ex}");
            }
        }

        private static void HandleMessage(ReferenceHub sender, IAnomalyMessage msg)
        {
            if (msg is not ContentCatalogAckMessage ack)
                return;

            try
            {
                bool match = ack.CatalogHash == ContentRegistry.Catalog.CatalogHash;
                var knownKeys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var entry in ContentRegistry.Catalog.Entries)
                    knownKeys.Add(entry.Key);

                bool invalidFailedKey = false;
                foreach (var key in ack.FailedKeys)
                {
                    if (!knownKeys.Contains(key))
                    {
                        invalidFailedKey = true;
                        break;
                    }
                }

                if (invalidFailedKey)
                {
                    Logger.Warn(
                        $"Anomaly.Content: rejected catalog ACK from {sender.LoggedNameFromRefHub()} " +
                        "because it named an entry outside the advertised catalog.");
                    return;
                }
                if (!ContentGrantGate.TryRecordAck(sender.netId, match, ack.FailedKeys))
                {
                    Logger.Warn(
                        $"Anomaly.Content: ignored duplicate catalog ACK from {sender.LoggedNameFromRefHub()}.");
                    return;
                }

                if (!match)
                {
                    Logger.Warn($"Anomaly.Content: catalog hash mismatch from {sender.LoggedNameFromRefHub()} " +
                                $"(client=0x{ack.CatalogHash:X8}, server=0x{ContentRegistry.Catalog.CatalogHash:X8}).");
                    if (Plugin.Instance != null && Plugin.Instance.Config.ContentMismatchKick)
                        sender.connectionToClient?.Disconnect();
                }
                else if (ack.FailedKeys.Count > 0)
                {
                    Logger.Warn($"Anomaly.Content: {sender.LoggedNameFromRefHub()} could not register " +
                                $"{ack.FailedKeys.Count} entr(ies): {string.Join(", ", ack.FailedKeys)}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"ContentCatalogBridge.HandleMessage: {ex}");
            }
        }
    }
}
