using Anomaly.Client.Internal.Hosting;
using Xunit;

namespace Anomaly.Client.Api.Tests;

public sealed class LocalHostGrantCoordinatorTests {
    private static LocalHostIdentitySnapshot LocalHost => new() {
        ServerActive = true,
        HostClientActive = true,
        IsLocalPlayer = true,
        Hub = new IntPtr(1),
        LocalHub = new IntPtr(1),
        HostHub = new IntPtr(1),
        Identity = new IntPtr(2),
        LocalPlayerIdentity = new IntPtr(2),
        ServerConnectionIdentity = new IntPtr(2),
        ConnectionToClient = new IntPtr(3),
        ServerLocalConnection = new IntPtr(3),
        ReciprocalConnectionToClient = new IntPtr(3),
        ConnectionToServer = new IntPtr(4),
        ClientConnection = new IntPtr(4),
    };

    [Fact]
    public void LocalHostGetsExactlyOneAttemptPerSession() {
        var coordinator = new LocalHostGrantCoordinator();

        Assert.True(coordinator.TrySchedule(LocalHost, out var ticket));
        Assert.False(coordinator.TrySchedule(LocalHost, out _));
        Assert.True(coordinator.TryBeginGrant(ticket, LocalHost));
        Assert.False(coordinator.TryBeginGrant(ticket, LocalHost));
        // A later callback must not restore permissions after the user changes them.
        Assert.False(coordinator.TrySchedule(LocalHost, out _));
    }

    [Theory]
    [InlineData("server-inactive")]
    [InlineData("remote-client")]
    [InlineData("dedicated-server")]
    [InlineData("remote-player")]
    [InlineData("missing-hub")]
    [InlineData("different-local-hub")]
    [InlineData("different-host-hub")]
    [InlineData("missing-identity")]
    [InlineData("different-local-identity")]
    [InlineData("different-server-identity")]
    [InlineData("missing-server-connection")]
    [InlineData("lan-or-loopback-peer")]
    [InlineData("different-reciprocal-connection")]
    [InlineData("missing-client-connection")]
    [InlineData("different-client-connection")]
    public void InvalidIdentityCannotScheduleOrExecute(string invalidState) {
        var snapshot = InvalidSnapshot(invalidState);
        var coordinator = new LocalHostGrantCoordinator();

        Assert.False(snapshot.IsEligible);
        Assert.False(coordinator.TrySchedule(snapshot, out _));
        // Rejected preflight must not reserve the hub if its initialization later completes.
        Assert.True(coordinator.TrySchedule(LocalHost, out var ticket));
        Assert.False(coordinator.TryBeginGrant(ticket, snapshot));
        // Failed execution validation consumes this session's only attempt.
        Assert.False(coordinator.TryBeginGrant(ticket, LocalHost));
        Assert.False(coordinator.TrySchedule(LocalHost, out _));
    }

    [Theory]
    [InlineData("hub")]
    [InlineData("identity")]
    [InlineData("server-connection")]
    [InlineData("client-connection")]
    public void CoherentButReplacedIdentityCannotUseOriginalTicket(string replacedPart) {
        var coordinator = new LocalHostGrantCoordinator();
        Assert.True(coordinator.TrySchedule(LocalHost, out var ticket));
        var replacement = ReplaceIdentity(LocalHost, replacedPart);

        Assert.True(replacement.IsEligible);
        Assert.False(coordinator.TryBeginGrant(ticket, replacement));
        Assert.False(coordinator.TryBeginGrant(ticket, LocalHost));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DestructionOrDisconnectInvalidatesPendingTicket(bool disconnect) {
        var coordinator = new LocalHostGrantCoordinator();
        Assert.True(coordinator.TrySchedule(LocalHost, out var ticket));

        if (disconnect)
            coordinator.Reset();
        else
            coordinator.Remove(LocalHost.Hub);

        Assert.False(coordinator.TryBeginGrant(ticket, LocalHost));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RehostWithReusedPointersGetsNewTicketAndRejectsOldOne(bool disconnect) {
        var coordinator = new LocalHostGrantCoordinator();
        Assert.True(coordinator.TrySchedule(LocalHost, out var oldTicket));

        if (disconnect)
            coordinator.Reset();
        else
            coordinator.Remove(LocalHost.Hub);

        Assert.True(coordinator.TrySchedule(LocalHost, out var newTicket));
        Assert.False(coordinator.TryBeginGrant(oldTicket, LocalHost));
        Assert.True(coordinator.TryBeginGrant(newTicket, LocalHost));
        Assert.False(coordinator.TryBeginGrant(oldTicket, LocalHost));
        Assert.False(coordinator.TryBeginGrant(newTicket, LocalHost));
    }

    [Fact]
    public void DisconnectAfterGrantAllowsNextHostSession() {
        var coordinator = new LocalHostGrantCoordinator();
        Assert.True(coordinator.TrySchedule(LocalHost, out var firstTicket));
        Assert.True(coordinator.TryBeginGrant(firstTicket, LocalHost));

        coordinator.Reset();
        var newHost = ReplaceIdentity(ReplaceIdentity(LocalHost, "hub"), "identity");
        Assert.True(coordinator.TrySchedule(newHost, out var secondTicket));
        Assert.True(coordinator.TryBeginGrant(secondTicket, newHost));
    }

    [Fact]
    public void RemovingOtherPlayerDoesNotInvalidateHostTicket() {
        var coordinator = new LocalHostGrantCoordinator();
        Assert.True(coordinator.TrySchedule(LocalHost, out var ticket));

        coordinator.Remove(new IntPtr(999));

        Assert.True(coordinator.TryBeginGrant(ticket, LocalHost));
    }

    [Fact]
    public void DefaultOrForeignTicketCannotConsumePendingGrant() {
        var coordinator = new LocalHostGrantCoordinator();
        var otherCoordinator = new LocalHostGrantCoordinator();
        Assert.True(coordinator.TrySchedule(LocalHost, out var ticket));
        Assert.True(otherCoordinator.TrySchedule(LocalHost, out var foreignTicket));

        Assert.False(coordinator.TryBeginGrant(default, LocalHost));
        Assert.False(coordinator.TryBeginGrant(foreignTicket, LocalHost));
        Assert.True(coordinator.TryBeginGrant(ticket, LocalHost));
    }

    private static LocalHostIdentitySnapshot InvalidSnapshot(string invalidState) => invalidState switch {
        "server-inactive" => LocalHost with { ServerActive = false },
        "remote-client" => LocalHost with { HostClientActive = false },
        "dedicated-server" => LocalHost with { IsDedicated = true },
        "remote-player" => LocalHost with { IsLocalPlayer = false },
        "missing-hub" => LocalHost with { Hub = IntPtr.Zero, LocalHub = IntPtr.Zero, HostHub = IntPtr.Zero },
        "different-local-hub" => LocalHost with { LocalHub = new IntPtr(999) },
        "different-host-hub" => LocalHost with { HostHub = new IntPtr(999) },
        "missing-identity" => LocalHost with { Identity = IntPtr.Zero, LocalPlayerIdentity = IntPtr.Zero, ServerConnectionIdentity = IntPtr.Zero },
        "different-local-identity" => LocalHost with { LocalPlayerIdentity = new IntPtr(999) },
        "different-server-identity" => LocalHost with { ServerConnectionIdentity = new IntPtr(999) },
        "missing-server-connection" => LocalHost with { ConnectionToClient = IntPtr.Zero, ServerLocalConnection = IntPtr.Zero, ReciprocalConnectionToClient = IntPtr.Zero },
        // IP/connection IDs never establish eligibility: a peer has a different native connection.
        "lan-or-loopback-peer" => LocalHost with { ConnectionToClient = new IntPtr(999) },
        "different-reciprocal-connection" => LocalHost with { ReciprocalConnectionToClient = new IntPtr(999) },
        "missing-client-connection" => LocalHost with { ConnectionToServer = IntPtr.Zero, ClientConnection = IntPtr.Zero },
        "different-client-connection" => LocalHost with { ClientConnection = new IntPtr(999) },
        _ => throw new ArgumentOutOfRangeException(nameof(invalidState)),
    };

    private static LocalHostIdentitySnapshot ReplaceIdentity(LocalHostIdentitySnapshot snapshot, string replacedPart) => replacedPart switch {
        "hub" => snapshot with { Hub = new IntPtr(101), LocalHub = new IntPtr(101), HostHub = new IntPtr(101) },
        "identity" => snapshot with { Identity = new IntPtr(102), LocalPlayerIdentity = new IntPtr(102), ServerConnectionIdentity = new IntPtr(102) },
        "server-connection" => snapshot with { ConnectionToClient = new IntPtr(103), ServerLocalConnection = new IntPtr(103), ReciprocalConnectionToClient = new IntPtr(103) },
        "client-connection" => snapshot with { ConnectionToServer = new IntPtr(104), ClientConnection = new IntPtr(104) },
        _ => throw new ArgumentOutOfRangeException(nameof(replacedPart)),
    };
}
