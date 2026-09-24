using System;
using System.Linq;
using System.Net;
using Anomaly.Server.Auth;
using Anomaly.Server.Networking;
using Anomaly.Shared.Auth;
using Anomaly.Shared.Networking;
using Mirror;
using Xunit;

namespace Anomaly.Shared.Tests;

public sealed class AuthenticatedConnectionPromotionTests : IDisposable
{
    [Fact]
    public void PromotionPreservesCipherAndBothReplayDirections()
    {
        var connection = new NetworkConnectionToClient();
        var player = CreatePlayer();
        var key = (byte[])player.ServerToClientKey.Clone();
        var sessionId = (byte[])player.SessionId.Clone();
        var transcript = (byte[])player.TranscriptHash.Clone();
        var originalKey = player.ServerToClientKey;
        AuthenticatedTransportSessions.RegisterConnection(connection, player);
        Assert.Null(player.ServerToClientKey);
        Assert.All(originalKey, value => Assert.Equal(0, value));
        var before = AuthenticatedTransportSessions.GetReplayState(connection);
        Assert.Equal(1U, before.NextTxCounter(MessageChannel.ReliableOrdered));
        Assert.Equal(AnomalyReplayState.RxResult.Accepted,
            before.TryAcceptRx(MessageChannel.ReliableOrdered, 17, out _));
        Assert.True(AuthenticatedTransportSessions.TryEncryptToConnection(connection, new byte[] { 1, 2 }, out var envelope));
        Assert.Equal(new byte[] { 1, 2 }, AuthTransportCrypto.Decrypt(key, sessionId, transcript,
            AuthTransportDirection.ServerToClient, envelope));

        var hub = new ReferenceHub { connectionToClient = connection };
        Assert.True(AuthenticatedTransportSessions.TryAdoptConnection(hub, out var adopted));
        Assert.Same(player, adopted);
        Assert.Same(before, AnomalyReplayTable.GetOrCreate(hub));
        Assert.Equal(2U, AnomalyReplayTable.GetOrCreate(hub).NextTxCounter(MessageChannel.ReliableOrdered));
        Assert.Equal(AnomalyReplayState.RxResult.RejectedReplay,
            AnomalyReplayTable.GetOrCreate(hub).TryAcceptRx(MessageChannel.ReliableOrdered, 17, out _));
        Assert.True(AuthenticatedTransportSessions.TryEncryptToClient(hub, new byte[] { 3 }, out envelope));
        Assert.Equal(new byte[] { 3 }, AuthTransportCrypto.Decrypt(key, sessionId, transcript,
            AuthTransportDirection.ServerToClient, envelope));
        Assert.False(AuthenticatedTransportSessions.TryAdoptConnection(new ReferenceHub { connectionToClient = connection }, out _));
    }

    [Fact]
    public void PendingConnectionCleanupRevokesKeysAndEnrollmentReservation()
    {
        var connection = new NetworkConnectionToClient();
        var player = CreatePlayer();
        player.EnrollmentReservation = EnrollmentSessionRegistry.Reserve(player.UserId, false);
        AuthenticatedTransportSessions.RegisterConnection(connection, player);
        Assert.Contains(connection, AuthenticatedTransportSessions.SnapshotConnections());
        AuthenticatedTransportSessions.QuarantineConnection(connection);
        Assert.False(AuthenticatedTransportSessions.TryEncryptToConnection(connection, new byte[] { 1 }, out _));
        Assert.False(AuthenticatedTransportSessions.TryAdoptConnection(new ReferenceHub { connectionToClient = connection }, out _));
        Assert.Equal(0, EnrollmentSessionRegistry.Count);
        Assert.Null(player.AcceptedPeer);
        AuthenticatedTransportSessions.RemoveConnection(connection);
        Assert.DoesNotContain(connection, AuthenticatedTransportSessions.SnapshotConnections());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FastRestartReplacesHubWhilePreservingConnectionCipherReplayAndEnrollment(bool allowSameAccountJoining)
    {
        var connection = new NetworkConnectionToClient();
        var player = CreatePlayer();
        player.EnrollmentReservation = EnrollmentSessionRegistry.Reserve(player.UserId, allowSameAccountJoining);
        var key = (byte[])player.ServerToClientKey.Clone();
        var sessionId = (byte[])player.SessionId.Clone();
        var transcript = (byte[])player.TranscriptHash.Clone();
        AuthenticatedTransportSessions.RegisterConnection(connection, player);
        var oldHub = new ReferenceHub { connectionToClient = connection };
        Assert.True(AuthenticatedTransportSessions.TryAdoptConnection(oldHub, out _));
        Assert.True(EnrollmentSessionRegistry.TryActivate(player.UserId, player.EnrollmentReservation,
            oldHub, allowSameAccountJoining, out _));
        var replay = AnomalyReplayTable.GetOrCreate(oldHub);
        Assert.Equal(1U, replay.NextTxCounter(MessageChannel.ReliableOrdered));
        Assert.Equal(AnomalyReplayState.RxResult.Accepted,
            replay.TryAcceptRx(MessageChannel.ReliableOrdered, 17, out _));

        Assert.True(AuthenticatedTransportSessions.TryReleaseHubForRestart(oldHub, allowSameAccountJoining));
        Assert.False(AuthenticatedTransportSessions.TryReleaseHubForRestart(oldHub, allowSameAccountJoining));
        Assert.False(AuthenticatedTransportSessions.IsActive(oldHub));
        Assert.True(AuthenticatedTransportSessions.IsActiveConnection(connection));

        // The ordinary old-hub cleanup still runs after release; it must not erase
        // the transport or enrollment reservation waiting for the new scene's hub.
        AuthenticatedTransportSessions.Remove(oldHub);
        EnrollmentSessionRegistry.Remove(oldHub);
        AnomalyReplayTable.Remove(oldHub);
        Assert.Same(replay, AuthenticatedTransportSessions.GetReplayState(connection));
        Assert.True(AuthenticatedTransportSessions.TryEncryptToConnection(connection, new byte[] { 4, 5 }, out var envelope));
        Assert.Equal(new byte[] { 4, 5 }, AuthTransportCrypto.Decrypt(key, sessionId, transcript,
            AuthTransportDirection.ServerToClient, envelope));

        var newHub = new ReferenceHub { connectionToClient = connection };
        Assert.True(AuthenticatedTransportSessions.TryAdoptConnection(newHub, out var adopted));
        Assert.Same(player, adopted);
        Assert.True(EnrollmentSessionRegistry.TryActivate(player.UserId, player.EnrollmentReservation,
            newHub, allowSameAccountJoining, out var previousHub));
        Assert.Null(previousHub);
        Assert.Same(replay, AnomalyReplayTable.GetOrCreate(newHub));
        Assert.Equal(2U, replay.NextTxCounter(MessageChannel.ReliableOrdered));
        Assert.Equal(AnomalyReplayState.RxResult.RejectedReplay,
            replay.TryAcceptRx(MessageChannel.ReliableOrdered, 17, out _));
        Assert.True(AuthenticatedTransportSessions.TryEncryptToClient(newHub, new byte[] { 6 }, out envelope));
        Assert.Equal(new byte[] { 6 }, AuthTransportCrypto.Decrypt(key, sessionId, transcript,
            AuthTransportDirection.ServerToClient, envelope));
        Assert.False(AuthenticatedTransportSessions.TryAdoptConnection(oldHub, out _));
    }

    [Fact]
    public void FastRestartReservationCannotSupersedeNewerPendingAuthentication()
    {
        var connection = new NetworkConnectionToClient();
        var player = CreatePlayer();
        player.EnrollmentReservation = EnrollmentSessionRegistry.Reserve(player.UserId, false);
        AuthenticatedTransportSessions.RegisterConnection(connection, player);
        var oldHub = new ReferenceHub { connectionToClient = connection };
        Assert.True(AuthenticatedTransportSessions.TryAdoptConnection(oldHub, out _));
        Assert.True(EnrollmentSessionRegistry.TryActivate(player.UserId, player.EnrollmentReservation,
            oldHub, false, out _));
        ulong newerReservation = EnrollmentSessionRegistry.Reserve(player.UserId, false);

        Assert.True(AuthenticatedTransportSessions.TryReleaseHubForRestart(oldHub, false));
        var restartingHub = new ReferenceHub { connectionToClient = connection };
        Assert.True(AuthenticatedTransportSessions.TryAdoptConnection(restartingHub, out _));
        Assert.False(EnrollmentSessionRegistry.TryActivate(player.UserId, player.EnrollmentReservation,
            restartingHub, false, out _));

        var newerHub = new ReferenceHub();
        Assert.True(EnrollmentSessionRegistry.TryActivate(player.UserId, newerReservation,
            newerHub, false, out var previousHub));
        Assert.Null(previousHub);
        AuthenticatedTransportSessions.Quarantine(restartingHub);
        EnrollmentSessionRegistry.Remove(oldHub);
        Assert.Equal(1, EnrollmentSessionRegistry.Count);
        EnrollmentSessionRegistry.Remove(newerHub);
        Assert.Equal(0, EnrollmentSessionRegistry.Count);
    }

    [Fact]
    public void FastRestartCannotSuspendAnEnrollmentAlreadyOwnedByAnotherHub()
    {
        var oldHub = new ReferenceHub();
        var player = CreatePlayer();
        player.EnrollmentReservation = EnrollmentSessionRegistry.Reserve(player.UserId, false);
        AuthenticatedTransportSessions.Register(oldHub, player);
        Assert.True(EnrollmentSessionRegistry.TryActivate(player.UserId, player.EnrollmentReservation,
            oldHub, false, out _));
        var newerHub = new ReferenceHub();
        ulong newerReservation = EnrollmentSessionRegistry.Reserve(player.UserId, false);
        Assert.True(EnrollmentSessionRegistry.TryActivate(player.UserId, newerReservation,
            newerHub, false, out _));

        Assert.False(AuthenticatedTransportSessions.TryReleaseHubForRestart(oldHub, false));
        AuthenticatedTransportSessions.Remove(oldHub);
        EnrollmentSessionRegistry.Remove(oldHub);
        Assert.Equal(1, EnrollmentSessionRegistry.Count);
        EnrollmentSessionRegistry.Remove(newerHub);
        Assert.Equal(0, EnrollmentSessionRegistry.Count);
    }

    [Fact]
    public void DifferentPeerCannotClaimOrConsumeAcceptedHandoffAtSameEndpoint()
    {
        var endpoint = new IPEndPoint(IPAddress.Loopback, 29382);
        var player = CreatePlayer();
        var peer = player.AcceptedPeer;
        Assert.True(AuthHandler.StorePlayer(endpoint, player));
        Assert.False(AuthHandler.TryTakePlayerForPeer(endpoint, new object(), out _));
        Assert.Equal(1, AuthHandler.StoredPlayerCount);
        Assert.True(AuthHandler.TryTakePlayerForPeer(endpoint, peer, out var accepted));
        Assert.Same(player, accepted);
        Assert.False(AuthHandler.TryTakePlayerForPeer(endpoint, peer, out _));
        accepted.ClearSecrets();
    }

    private static PlayerStorage CreatePlayer() => new PlayerStorage
    {
        UserId = "promotion@anomaly",
        AcceptedPeer = new object(),
        ClientToServerKey = Enumerable.Repeat((byte)0x11, 32).ToArray(),
        ServerToClientKey = Enumerable.Repeat((byte)0x22, 32).ToArray(),
        SessionId = Enumerable.Repeat((byte)0x33, AuthTransportCrypto.SessionIdSize).ToArray(),
        TranscriptHash = Enumerable.Repeat((byte)0x44, AuthTransportCrypto.TranscriptHashSize).ToArray(),
    };

    public void Dispose()
    {
        foreach (var hub in AuthenticatedTransportSessions.SnapshotHubs())
            AuthenticatedTransportSessions.Remove(hub);
        foreach (var connection in AuthenticatedTransportSessions.SnapshotConnections())
            AuthenticatedTransportSessions.RemoveConnection(connection);
        AuthenticatedTransportSessions.Reset();
        AnomalyReplayTable.Reset();
        AuthHandler.Reset();
        EnrollmentSessionRegistry.Reset();
    }
}
