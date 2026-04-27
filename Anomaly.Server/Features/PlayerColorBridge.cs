using System;
using Anomaly.Server.Networking;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using LabApi.Events.Handlers;
using LabApi.Features.Console;

namespace Anomaly.Server.Features
{
    /// <summary>
    /// Server-side glue for the Class D jumpsuit color customization wire
    /// protocol. Listens for client-sent SetFavoriteColor messages, persists
    /// the value into <see cref="PlayerColorState"/> for the session, and
    /// rebroadcasts as PlayerColorChanged to all clients. On a new client's
    /// encrypted-channel ready event, replays every known color so the joiner
    /// paints the correct tints from first frame.
    /// </summary>
    internal static class PlayerColorBridge
    {
        internal static void Init()
        {
            AnomalyMessaging.MessageReceived += HandleMessage;
            AnomalyMessaging.ClientReady += HandleClientReady;

            PlayerEvents.Left += ev =>
            {
                try
                {
                    var userId = ev?.Player?.UserId;
                    if (!string.IsNullOrEmpty(userId))
                        PlayerColorState.Remove(userId);
                }
                catch (Exception ex)
                {
                    Logger.Error($"PlayerColorBridge.PlayerLeft: {ex}");
                }
            };
        }

        private static void HandleMessage(ReferenceHub sender, IAnomalyMessage msg)
        {
            if (msg is not SetFavoriteColorMessage set)
                return;

            try
            {
                var userId = sender?.authManager?.UserId;
                if (string.IsNullOrEmpty(userId))
                {
                    Logger.Warn($"PlayerColorBridge: SetFavoriteColor from {sender?.LoggedNameFromRefHub() ?? "<null>"} dropped; no UserId.");
                    return;
                }

                var color = new PlayerColorState.ColorRgb(set.R, set.G, set.B);
                PlayerColorState.Set(userId, color);

                AnomalyMessaging.SendToAll(new PlayerColorChangedMessage
                {
                    NetId = sender.netId,
                    R = color.R,
                    G = color.G,
                    B = color.B,
                });
            }
            catch (Exception ex)
            {
                Logger.Error($"PlayerColorBridge.HandleMessage: {ex}");
            }
        }

        private static void HandleClientReady(ReferenceHub newJoiner)
        {
            if (newJoiner == null)
                return;

            try
            {
                foreach (var entry in PlayerColorState.All)
                {
                    var ownerHub = FindHubByUserId(entry.Key);
                    // Skip ghosts: the player whose color we cached may have left
                    // between the cache write and this snapshot replay.
                    if (ownerHub == null || ownerHub.connectionToClient == null)
                        continue;

                    AnomalyMessaging.SendToClient(newJoiner, new PlayerColorChangedMessage
                    {
                        NetId = ownerHub.netId,
                        R = entry.Value.R,
                        G = entry.Value.G,
                        B = entry.Value.B,
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"PlayerColorBridge.HandleClientReady: {ex}");
            }
        }

        private static ReferenceHub FindHubByUserId(string userId)
        {
            foreach (var hub in ReferenceHub.AllHubs)
            {
                if (hub != null && hub.authManager != null && hub.authManager.UserId == userId)
                    return hub;
            }
            return null;
        }
    }
}
