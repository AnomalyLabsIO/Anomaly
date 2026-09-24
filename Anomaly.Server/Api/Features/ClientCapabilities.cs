using System;
using System.Collections.Generic;
using System.Linq;
using Anomaly.Server.Networking;
using Anomaly.Shared.Capabilities;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using LabApi.Events.Handlers;
using LabApi.Features.Wrappers;
using Anomaly.Server.Api.Events.Handlers;
using Logger = LabApi.Features.Console.Logger;

namespace Anomaly.Server.Api.Features
{
    /// <summary>
    /// Server-owned, round-scoped client capability snapshots.
    /// </summary>
    public static class ClientCapabilities
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<ReferenceHub, Dictionary<string, CapabilityDescriptor>> Snapshots =
            new Dictionary<ReferenceHub, Dictionary<string, CapabilityDescriptor>>();

        private static bool _initialized;
        private static uint _roundEpoch = 1;
        private static uint _challengeSequence;

        internal static uint CurrentRoundEpoch
        {
            get
            {
                lock (Sync)
                    return _roundEpoch;
            }
        }

        /// <summary>Looks up a capability advertised for this player's current round.</summary>
        public static bool TryGet(Player player, string id, out CapabilityDescriptor descriptor) =>
            TryGetCore(player?.ReferenceHub, id, out descriptor);

        private static bool TryGetCore(
            ReferenceHub player,
            string id,
            out CapabilityDescriptor descriptor)
        {
            lock (Sync)
            {
                if (player != null &&
                    id != null &&
                    Snapshots.TryGetValue(player, out var snapshot) &&
                    snapshot.TryGetValue(id, out descriptor))
                {
                    return true;
                }
            }

            descriptor = null;
            return false;
        }

        /// <summary>Checks compatible major/minor/patch support against the advertised snapshot.</summary>
        public static bool Supports(
            Player player,
            CapabilityDescriptor requirement)
        {
            if (requirement == null) return false;
            return TryGet(player, requirement.Id, out var advertised) &&
                   advertised.Satisfies(requirement);
        }

        /// <summary>Returns detached, ID-sorted immutable capability descriptors.</summary>
        public static IReadOnlyCollection<CapabilityDescriptor> GetSnapshot(Player player)
        {
            ReferenceHub hub = player?.ReferenceHub;
            lock (Sync)
            {
                if (hub != null && Snapshots.TryGetValue(hub, out var snapshot))
                    return snapshot.Values.OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();
            }

            return Array.Empty<CapabilityDescriptor>();
        }

        internal static void Init()
        {
            lock (Sync)
            {
                if (_initialized) return;
                _initialized = true;
                AnomalyMessaging.CurrentRoundEpoch = _roundEpoch;
            }

            AnomalyMessaging.MessageReceived += OnMessageReceived;
            AnomalyMessaging.ClientReady += OnClientReady;
            PlayerEvents.Left += OnPlayerLeft;
            ServerEvents.WaitingForPlayers += OnWaitingForPlayers;
        }

        internal static void Disable()
        {
            lock (Sync)
            {
                if (!_initialized) return;
                _initialized = false;
                Snapshots.Clear();
            }

            AnomalyMessaging.MessageReceived -= OnMessageReceived;
            AnomalyMessaging.ClientReady -= OnClientReady;
            PlayerEvents.Left -= OnPlayerLeft;
            ServerEvents.WaitingForPlayers -= OnWaitingForPlayers;
        }

        internal static void Remove(ReferenceHub player)
        {
            if (player == null) return;
            lock (Sync)
                Snapshots.Remove(player);
        }

        private static void OnMessageReceived(ReferenceHub player, IAnomalyMessage message)
        {
            var snapshotMessage = message as CapabilitySnapshotMessage;
            if (snapshotMessage == null)
                return;

            var replacement = new Dictionary<string, CapabilityDescriptor>(StringComparer.Ordinal);
            for (int i = 0; i < snapshotMessage.Capabilities.Count; i++)
            {
                CapabilityDescriptor descriptor = snapshotMessage.Capabilities[i];
                if (descriptor == null || replacement.ContainsKey(descriptor.Id))
                {
                    Remove(player);
                    NotifyCapabilitiesChanged(player);
                    try
                    {
                        Logger.Warn(
                            $"ClientCapabilities: rejected duplicate/invalid capability snapshot from " +
                            $"{player.LoggedNameFromRefHub()}.");
                    }
                    catch { }
                    return;
                }

                replacement.Add(descriptor.Id, descriptor);
            }

            lock (Sync)
                Snapshots[player] = replacement;

            NotifyCapabilitiesChanged(player);
        }

        private static void NotifyCapabilitiesChanged(ReferenceHub player)
        {
            ClientEvents.OnCapabilitiesChanged(player);
        }

        private static void OnClientReady(ReferenceHub player)
        {
            SendChallenge(player);
        }

        private static void OnPlayerLeft(LabApi.Events.Arguments.PlayerEvents.PlayerLeftEventArgs ev)
        {
            Remove(ev.Player.ReferenceHub);
        }

        private static void OnWaitingForPlayers()
        {
            lock (Sync)
            {
                _roundEpoch = unchecked(_roundEpoch + 1);
                if (_roundEpoch == 0)
                    _roundEpoch = 1;
                Snapshots.Clear();
                AnomalyMessaging.CurrentRoundEpoch = _roundEpoch;
            }

            PlayerMotion.ResetAll();
            foreach (ReferenceHub hub in ReferenceHub.AllHubs)
            {
                try
                {
                    if (AnomalyMessaging.IsAnomalyTarget(hub))
                        SendChallenge(hub);
                }
                catch (Exception exception)
                {
                    try
                    {
                        Logger.Error(
                            $"ClientCapabilities: Isolated round-start challenge failure: {exception}");
                    }
                    catch { }
                }
            }
        }

        private static void SendChallenge(ReferenceHub player)
        {
            uint epoch;
            uint sequence;
            lock (Sync)
            {
                epoch = _roundEpoch;
                sequence = unchecked(++_challengeSequence);
                if (sequence == 0)
                    sequence = unchecked(++_challengeSequence);
            }

            AnomalyMessaging.SendToClient(player, new CapabilityChallengeMessage
            {
                RoundEpoch = epoch,
                Sequence = sequence
            });
        }
    }
}
