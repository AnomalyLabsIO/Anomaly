using System;
using Anomaly.Server.FileTransfer;
using Anomaly.Server.Networking;
using Anomaly.Shared.Networking.Messages;
using LabApi.Events.Arguments.ServerEvents;
using LabApi.Events.Handlers;
using Logger = LabApi.Features.Console.Logger;

namespace Anomaly.Server.Round
{
    /// <summary>
    /// Subscribes to LabAPI's round lifecycle events and rebroadcasts the current
    /// <see cref="RoundPhase"/> to all Anomaly clients via <see cref="RoundStateMessage"/>.
    /// The server side stays entirely on LabAPI — this class exists solely so client-side
    /// plugins can react to the same moments.
    /// </summary>
    /// <remarks>
    /// Late-join replay: when a new hub connects, <c>FileTransferSession.SessionCompleted</c>
    /// already fires after initial assets are ready; we hook that in
    /// <c>RoundStateBroadcaster.BroadcastCurrentTo</c> so clients joining mid-round
    /// get the current phase as well.
    /// </remarks>
    internal static class RoundStateBroadcaster
    {
        private static RoundPhase _currentPhase = RoundPhase.WaitingForPlayers;
        private static bool _hooked;

        internal static RoundPhase CurrentPhase => _currentPhase;

        internal static void Init()
        {
            if (_hooked) return;
            _hooked = true;

            ServerEvents.WaitingForPlayers += OnWaitingForPlayers;
            ServerEvents.RoundStarting     += OnRoundStarting;
            ServerEvents.RoundStarted      += OnRoundStarted;
            ServerEvents.RoundEnding       += OnRoundEnding;
            ServerEvents.RoundEnded        += OnRoundEnded;
            ServerEvents.RoundRestarted    += OnRoundRestarted;

            // Late-join: when a hub finishes its initial file transfer, replay the
            // current phase to them so they don't miss the transition they would
            // otherwise have seen. Matches the self-contained pattern used by
            // AssetSpawner and AssetOverrides.
            FileTransferSession.SessionCompleted += BroadcastCurrentTo;

            Logger.Info("RoundStateBroadcaster: subscribed to LabAPI round events.");
        }

        internal static void Disable()
        {
            if (!_hooked) return;
            _hooked = false;

            ServerEvents.WaitingForPlayers -= OnWaitingForPlayers;
            ServerEvents.RoundStarting     -= OnRoundStarting;
            ServerEvents.RoundStarted      -= OnRoundStarted;
            ServerEvents.RoundEnding       -= OnRoundEnding;
            ServerEvents.RoundEnded        -= OnRoundEnded;
            ServerEvents.RoundRestarted    -= OnRoundRestarted;

            FileTransferSession.SessionCompleted -= BroadcastCurrentTo;
        }

        /// <summary>
        /// Sends the current phase to a specific hub. Used for late-join replay so
        /// clients connecting mid-round don't miss the state transition events they
        /// would otherwise have observed.
        /// </summary>
        internal static void BroadcastCurrentTo(ReferenceHub hub)
        {
            if (hub == null) return;
            try
            {
                AnomalyMessaging.SendToClient(hub, new RoundStateMessage { Phase = _currentPhase });
            }
            catch (Exception ex)
            {
                Logger.Error($"RoundStateBroadcaster.BroadcastCurrentTo: {ex}");
            }
        }

        private static void SetAndBroadcast(RoundPhase phase)
        {
            _currentPhase = phase;
            try
            {
                AnomalyMessaging.SendToAll(new RoundStateMessage { Phase = phase });
            }
            catch (Exception ex)
            {
                Logger.Error($"RoundStateBroadcaster.SetAndBroadcast({phase}): {ex}");
            }
        }

        private static void OnWaitingForPlayers()     => SetAndBroadcast(RoundPhase.WaitingForPlayers);
        private static void OnRoundStarting(RoundStartingEventArgs _) => SetAndBroadcast(RoundPhase.Starting);
        private static void OnRoundStarted()          => SetAndBroadcast(RoundPhase.InProgress);
        private static void OnRoundEnding(RoundEndingEventArgs _)     => SetAndBroadcast(RoundPhase.Ending);
        private static void OnRoundEnded(RoundEndedEventArgs _)       => SetAndBroadcast(RoundPhase.Ended);
        private static void OnRoundRestarted()         => SetAndBroadcast(RoundPhase.Restarted);
    }
}
