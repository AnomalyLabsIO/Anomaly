using System;
using System.Collections.Generic;
using System.IO;
using Anomaly.Server.Content;
using Anomaly.Server.FileTransfer;
using Anomaly.Shared.Auth;
using Anomaly.Shared.Content;
using Anomaly.Shared.Networking;
using Anomaly.Shared.Networking.Messages;
using MEC;
using Mirror;
using NetworkManagerUtils.Dummies;

namespace Anomaly.Server.Networking;

/// <summary>Owns required content before a player object or client-ready event exists.</summary>
internal static class ConnectionContentBootstrap
{
    private sealed class Admission
    {
        internal readonly ConnectionContentState Gate;
        internal FileTransferSession Transfer;
        internal ContentCatalog Catalog;
        internal CoroutineHandle Watchdog;
        internal ulong Generation;
        internal bool AwaitingGeneration;
        internal ReferenceHub AdoptedHub;
        internal Admission(ConnectionContentState gate = null) => Gate = gate ?? new ConnectionContentState();
        internal bool Ready => Gate.Complete && Generation != 0 && !AwaitingGeneration;
        internal bool Expired => Gate.DeadlineElapsed && !Ready;
    }

    private static readonly Dictionary<NetworkConnectionToClient, Admission> Admissions =
        new Dictionary<NetworkConnectionToClient, Admission>();

    internal static bool IsLocal(NetworkConnectionToClient connection) =>
        connection is LocalConnectionToClient || connection is DummyNetworkConnection;

    internal static void Begin(NetworkConnectionToClient connection)
    {
        if (IsLocal(connection)) return;
        var admission = new Admission();
        Admissions.Add(connection, admission);
        admission.Watchdog = Timing.RunCoroutine(Watch(connection, admission));
    }

    internal static bool IsComplete(NetworkConnectionToClient connection) =>
        IsLocal(connection) || connection != null &&
        AuthenticatedTransportSessions.IsActiveConnection(connection) &&
        Admissions.TryGetValue(connection, out var admission) && admission.Ready;

    internal static bool RequireComplete(NetworkConnectionToClient connection)
    {
        if (IsComplete(connection)) return true;
        ConnectionBootstrap.Reject(connection, "Player readiness was requested before authenticated content bootstrap completed.");
        return false;
    }

    internal static bool MarkRejected(NetworkConnectionToClient connection)
    {
        if (connection == null || !Admissions.TryGetValue(connection, out var admission)) return true;
        return admission.Gate.Reject();
    }

    internal static void OnGenerationStarted()
    {
        foreach (var connection in new List<NetworkConnectionToClient>(Admissions.Keys))
        {
            try
            {
                if (!Admissions.TryGetValue(connection, out var previous) || previous.Gate.Rejected) continue;
                // A new map invalidates approval even while its frozen snapshot is pending.
                var nextGate = previous.Ready ? new ConnectionContentState() : previous.Gate.PreserveDeadline();
                Cleanup(connection);
                if (!AuthenticatedTransportSessions.IsActiveConnection(connection)) continue;
                var admission = new Admission(nextGate) { AwaitingGeneration = true };
                Admissions.Add(connection, admission);
                connection.isReady = false;
                admission.Watchdog = Timing.RunCoroutine(Watch(connection, admission));
            }
            catch (Exception exception) { ConnectionBootstrap.Reject(connection, "Content generation reset failed: " + exception.Message); }
        }
    }

    internal static void OnSnapshotDelivered(NetworkConnectionToClient connection, ulong generation)
    {
        if (!Admissions.TryGetValue(connection, out var admission)) return;
        if (admission.Expired)
        {
            ConnectionBootstrap.Reject(connection, "Facility delivery exceeded the content admission deadline.");
            return;
        }
        if (admission.Generation != 0 && admission.Generation != generation)
        {
            // Defensive fallback if a caller publishes a successor without BeginAuthoring.
            var nextGate = admission.Ready ? new ConnectionContentState() : admission.Gate.PreserveDeadline();
            Cleanup(connection);
            admission = new Admission(nextGate);
            Admissions.Add(connection, admission);
            connection.isReady = false;
            admission.Watchdog = Timing.RunCoroutine(Watch(connection, admission));
            if (!Admissions.TryGetValue(connection, out admission)) return;
        }
        admission.Generation = generation;
        admission.AwaitingGeneration = false;
        TryOffer(connection, admission);
    }

    private static IEnumerator<float> Watch(NetworkConnectionToClient connection, Admission admission)
    {
        // Install the handle before a first send or failure can revoke its owner.
        yield return Timing.WaitForOneFrame;
        while (IsCurrent(connection, admission) && !admission.Gate.Rejected)
        {
            if (!AuthenticatedTransportSessions.IsActiveConnection(connection)) { Cleanup(connection); yield break; }
            if (admission.Expired)
            {
                ConnectionBootstrap.Reject(connection, "Authenticated content bootstrap exceeded its fixed 120 second deadline.");
                yield break;
            }
            TryOffer(connection, admission);
            if (admission.Ready) yield break;
            yield return Timing.WaitForOneFrame;
        }
    }

    private static void TryOffer(NetworkConnectionToClient connection, Admission admission)
    {
        if (!IsCurrent(connection, admission) || admission.Gate.HasOffer || admission.Gate.Rejected ||
            admission.AwaitingGeneration || !ServerAssetManager.IsReady) return;
        if (admission.Expired) { ConnectionBootstrap.Reject(connection, "Content indexing exceeded the admission deadline."); return; }
        AssetPackSnapshot snapshot = null;
        try
        {
            if (!ServerAssetManager.TryAcquireOfferSnapshot(out snapshot)) return;
            admission.Transfer = FileTransferSession.StartForConnection(connection, snapshot,
                () => OnTransferComplete(connection, admission),
                reason => ConnectionBootstrap.Reject(connection, "Content transfer failed: " + reason));
            snapshot = null; // Transfer now owns the retained immutable snapshot.
            admission.Catalog = ContentRegistry.CreateOfferCatalog(admission.Transfer.Manifest.ManifestSha256, admission.Transfer.Snapshot);
            if (!admission.Gate.Offer(admission.Transfer.Manifest.SessionId,
                admission.Catalog.CatalogSha256, admission.Catalog.ManifestSha256))
                throw new InvalidOperationException("Content offer could not enter the connection gate.");
            if (!AnomalyMessaging.TrySendToConnection(connection, new ContentCatalogMessage { Catalog = admission.Catalog }))
                throw new IOException("The authenticated catalog could not be delivered.");
            admission.Transfer.SendManifest();
        }
        catch (Exception exception) { ConnectionBootstrap.Reject(connection, "Content offer failed: " + exception.Message); }
        finally { snapshot?.Dispose(); }
    }

    private static void OnTransferComplete(NetworkConnectionToClient connection, Admission admission)
    {
        if (!IsCurrent(connection, admission)) return;
        if (!admission.Gate.FinishTransfer())
            ConnectionBootstrap.Reject(connection, "Content transfer completed outside its exact approved offer.");
    }

    internal static bool HandleMessage(NetworkConnectionToClient connection, IAnomalyMessage message)
    {
        if (!ContentBootstrapPolicy.IsClientMessage(message)) return false;
        if (!Admissions.TryGetValue(connection, out var admission) || admission.Gate.Rejected) return true;
        try
        {
            if (!IsCurrent(connection, admission) || admission.Expired || !admission.Gate.HasOffer)
                throw new InvalidDataException("Content response arrived without a live offer.");
            switch (message)
            {
                case ContentCatalogDecisionMessage decision:
                    if (decision.Decision != ContentCatalogDecision.Approved ||
                        !admission.Gate.Approve(decision.CatalogSha256, decision.ManifestSha256) ||
                        !admission.Transfer.Approve())
                        throw new InvalidDataException("Content consent was declined, stale, or out of order.");
                    break;
                case FileRequestMessage request:
                    if (!admission.Gate.BeginTransfer(request.SessionId))
                        throw new InvalidDataException("File request arrived before exact consent or repeated an old request.");
                    admission.Transfer.HandleMessage(request);
                    break;
                case FileAckMessage acknowledgement:
                    admission.Transfer.HandleMessage(acknowledgement);
                    break;
                case ContentCatalogAckMessage acknowledgement:
                    if (acknowledgement.Status != ContentCatalogActivationStatus.Activated ||
                        acknowledgement.FailedKeys.Count != 0 || !admission.Transfer.IsComplete ||
                        !admission.Gate.Activate(acknowledgement.CatalogSha256, acknowledgement.ManifestSha256))
                        throw new InvalidDataException("Activation failed or did not match a completely transferred offer.");
                    // Commit before sending; Ready/AddPlayer may follow the authenticated acknowledgement immediately.
                    if (!AnomalyMessaging.TrySendToConnection(connection, new ContentBootstrapReadyMessage
                    {
                        SessionId = admission.Gate.SessionId,
                        CatalogSha256 = admission.Gate.CatalogHash,
                        ManifestSha256 = admission.Gate.ManifestHash,
                    })) throw new IOException("The authenticated content-ready confirmation could not be delivered.");
                    break;
            }
        }
        catch (Exception exception) { ConnectionBootstrap.Reject(connection, "Content bootstrap rejected: " + exception.Message); }
        return true;
    }

    internal static void HandlePacked(NetworkConnectionToClient connection, EncryptedChannelManager.EncryptedMessageOutside packed)
    {
        try
        {
            if (!Admissions.TryGetValue(connection, out var admission) || !IsCurrent(connection, admission) ||
                admission.Gate.Rejected || admission.Expired || packed.Data == null ||
                packed.Data.Length == 0 || packed.Data.Length > AnomalyChannel.MaxEncryptedEnvelopeBytes ||
                AuthenticatedTransportSessions.GetRateLimiter(connection, raw: true) is not AnomalyInboundRateLimiter rawLimiter ||
                !rawLimiter.TryAccept(0, Math.Min(packed.Data.Length, AnomalyChannel.MaxEnvelopeBytes)).Allowed ||
                packed.Level != EncryptedChannelManager.SecurityLevel.EncryptedAndAuthenticated ||
                !AuthTransportCrypto.IsVersion2Envelope(packed.Data) ||
                !AuthenticatedTransportSessions.TryDecryptFromClient(connection, packed.Data, out byte[] data) ||
                data.Length < 9 || data.Length > AnomalyChannel.MaxEnvelopeBytes || data[0] != AnomalyChannel.ChannelId)
                throw new InvalidDataException("Pre-player traffic did not match the authenticated bootstrap envelope.");
            uint counter = BitConverter.ToUInt32(data, 1), hash = BitConverter.ToUInt32(data, 5);
            if (counter == 0 || counter == uint.MaxValue ||
                AuthenticatedTransportSessions.GetRateLimiter(connection, raw: false) is not AnomalyInboundRateLimiter messageLimiter ||
                !messageLimiter.TryAccept(hash, data.Length - 9).Allowed ||
                !AnomalyMessageRegistry.TryGetChannel(hash, out var channel) ||
                !AnomalyMessageRegistry.TryGetPolicy(hash, out var policy) ||
                policy.Direction != AnomalyMessageDirection.ClientToServer ||
                policy.AuthPhase != AnomalyMessageAuthPhase.AuthenticatedSession || data.Length - 9 > policy.MaxPayloadBytes ||
                !AnomalyMessageRegistry.TryCreate(hash, out var message) || !ContentBootstrapPolicy.IsClientMessage(message))
                throw new InvalidDataException("Pre-player message was outside the closed content bootstrap contract.");
            var replay = AuthenticatedTransportSessions.GetReplayState(connection);
            if (replay == null || replay.TryAcceptRx(channel, counter, out _) != AnomalyReplayState.RxResult.Accepted)
                throw new InvalidDataException("Pre-player message replay or counter mismatch.");
            using (var stream = new MemoryStream(data, 9, data.Length - 9, false))
            using (var reader = new BinaryReader(stream))
            {
                message.Deserialize(reader);
                if (stream.Position != stream.Length) throw new InvalidDataException("Bootstrap message contained trailing data.");
            }
            HandleMessage(connection, message);
        }
        catch (Exception exception) { ConnectionBootstrap.Reject(connection, "Pre-player content protocol rejected: " + exception.Message); }
    }

    internal static bool TryGetCompletedCatalog(ReferenceHub hub, out ContentCatalog catalog)
    {
        catalog = null;
        if (hub?.connectionToClient == null || !IsComplete(hub.connectionToClient) ||
            !Admissions.TryGetValue(hub.connectionToClient, out var admission) || admission.AdoptedHub != null)
            return false;
        admission.AdoptedHub = hub;
        catalog = admission.Catalog;
        return catalog != null;
    }

    private static bool IsCurrent(NetworkConnectionToClient connection, Admission admission) =>
        Admissions.TryGetValue(connection, out var current) && ReferenceEquals(current, admission);

    internal static void Cleanup(NetworkConnectionToClient connection)
    {
        if (connection == null) return;
        if (Admissions.TryGetValue(connection, out var admission))
        {
            Admissions.Remove(connection);
            admission.Gate.Reject();
            if (admission.AdoptedHub != null) ContentGrantGate.Remove(admission.AdoptedHub.netId);
            try { if (admission.Watchdog.IsRunning) Timing.KillCoroutines(admission.Watchdog); } catch { }
        }
        FileTransferSession.CleanupConnection(connection);
    }

    internal static void Reset()
    {
        foreach (var connection in new List<NetworkConnectionToClient>(Admissions.Keys)) Cleanup(connection);
    }
}
