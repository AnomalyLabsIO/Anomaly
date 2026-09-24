using System;
using System.Collections.Generic;

namespace Anomaly.Client.Internal.Hosting;

internal readonly record struct LocalHostIdentitySnapshot {
    internal bool ServerActive { get; init; }
    internal bool HostClientActive { get; init; }
    internal bool IsDedicated { get; init; }
    internal bool IsLocalPlayer { get; init; }
    internal IntPtr Hub { get; init; }
    internal IntPtr LocalHub { get; init; }
    internal IntPtr HostHub { get; init; }
    internal IntPtr Identity { get; init; }
    internal IntPtr LocalPlayerIdentity { get; init; }
    internal IntPtr ConnectionToClient { get; init; }
    internal IntPtr ServerLocalConnection { get; init; }
    internal IntPtr ServerConnectionIdentity { get; init; }
    internal IntPtr ConnectionToServer { get; init; }
    internal IntPtr ClientConnection { get; init; }
    internal IntPtr ReciprocalConnectionToClient { get; init; }

    internal bool IsEligible => ServerActive && HostClientActive && !IsDedicated && IsLocalPlayer
        && Hub != IntPtr.Zero && Hub == LocalHub && Hub == HostHub
        && Identity != IntPtr.Zero && Identity == LocalPlayerIdentity && Identity == ServerConnectionIdentity
        && ConnectionToClient != IntPtr.Zero && ConnectionToClient == ServerLocalConnection
        && ConnectionToClient == ReciprocalConnectionToClient
        && ConnectionToServer != IntPtr.Zero && ConnectionToServer == ClientConnection;
}

internal readonly record struct LocalHostGrantTicket {
    internal LocalHostGrantTicket(LocalHostIdentitySnapshot snapshot, object token) {
        Hub = snapshot.Hub;
        Identity = snapshot.Identity;
        ServerConnection = snapshot.ConnectionToClient;
        ClientConnection = snapshot.ConnectionToServer;
        Token = token;
    }

    internal IntPtr Hub { get; }
    internal IntPtr Identity { get; }
    internal IntPtr ServerConnection { get; }
    internal IntPtr ClientConnection { get; }
    internal object Token { get; }

    internal bool Matches(LocalHostIdentitySnapshot snapshot) => Hub == snapshot.Hub
        && Identity == snapshot.Identity
        && ServerConnection == snapshot.ConnectionToClient
        && ClientConnection == snapshot.ConnectionToServer;
}

// Accessed only on the Unity thread. Each hub session gets one deferred attempt.
internal sealed class LocalHostGrantCoordinator {
    private readonly Dictionary<IntPtr, PendingGrant> _grants = new();

    internal bool TrySchedule(LocalHostIdentitySnapshot snapshot, out LocalHostGrantTicket ticket) {
        ticket = default;
        if (!snapshot.IsEligible || _grants.ContainsKey(snapshot.Hub))
            return false;

        // The token remains unique across removal/reset, even if native pointers are reused.
        ticket = new LocalHostGrantTicket(snapshot, new object());
        _grants.Add(snapshot.Hub, new PendingGrant(ticket));
        return true;
    }

    internal bool TryBeginGrant(LocalHostGrantTicket ticket, LocalHostIdentitySnapshot currentSnapshot) {
        if (!_grants.TryGetValue(ticket.Hub, out var grant)
            || !ReferenceEquals(grant.Ticket.Token, ticket.Token) || !grant.IsPending)
            return false;

        // Consume before validation/command execution: failed readiness or command errors do not retry.
        grant.IsPending = false;
        return currentSnapshot.IsEligible && ticket.Matches(currentSnapshot);
    }

    internal void Remove(IntPtr hub) => _grants.Remove(hub);

    internal void Reset() => _grants.Clear();

    private sealed class PendingGrant {
        internal PendingGrant(LocalHostGrantTicket ticket) => Ticket = ticket;

        internal LocalHostGrantTicket Ticket { get; }
        internal bool IsPending { get; set; } = true;
    }
}
