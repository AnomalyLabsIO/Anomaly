using Anomaly.Shared.Networking;
using LabApi.Features.Wrappers;
using ServerTransport = Anomaly.Server.Networking.AnomalyMessaging;

namespace Anomaly.Server.Api.Networking;

/// <summary>Authenticated delivery to real Anomaly clients using native LabAPI players.</summary>
public static class ClientMessaging
{
    /// <summary>
    /// Gets the current recipient-safe capacity for a registered outgoing message contract.
    /// Does not serialize or send the message, advance counters, or change the session.
    /// False indicates an unavailable recipient, transport, or registered contract.
    /// </summary>
    public static bool TryGetPayloadBudget(Player player, IAnomalyMessage contract, out MessagePayloadBudget budget) =>
        ServerTransport.TryGetPayloadBudget(player?.ReferenceHub, contract, out budget);

    /// <summary>Attempts delivery; false indicates an unavailable client or rejected message policy.</summary>
    public static bool TrySend(Player player, IAnomalyMessage message) =>
        player != null && message != null && ServerTransport.TrySendToClient(player.ReferenceHub, message);

    /// <summary>Sends to a client; throws if the message cannot be delivered.</summary>
    public static void Send(Player player, IAnomalyMessage message)
    {
        if (player == null) throw new System.ArgumentNullException(nameof(player));
        if (message == null) throw new System.ArgumentNullException(nameof(message));
        ServerTransport.SendToClient(player.ReferenceHub, message);
    }

    /// <summary>Attempts delivery independently to every connected Anomaly client.</summary>
    public static void SendToAll(IAnomalyMessage message)
    {
        if (message == null) throw new System.ArgumentNullException(nameof(message));
        ServerTransport.SendToAll(message);
    }

    /// <summary>Attempts delivery to all clients except the given player.</summary>
    public static void SendToAllExcept(Player excludedPlayer, IAnomalyMessage message)
    {
        if (excludedPlayer == null) throw new System.ArgumentNullException(nameof(excludedPlayer));
        if (message == null) throw new System.ArgumentNullException(nameof(message));
        ServerTransport.SendToAllExcept(excludedPlayer.Connection, message);
    }
}
