using Anomaly.Shared.Networking;
using LabApi.Features.Wrappers;

namespace Anomaly.Server.Api.Events.Arguments;

/// <summary>A validated message received over the authenticated client transport.</summary>
public sealed class ClientMessageReceivedEventArgs : ClientEventArgs
{
    internal ClientMessageReceivedEventArgs(Player player, IAnomalyMessage message) : base(player) => Message = message;
    /// <summary>The decoded message. Treat received message data as read-only.</summary>
    public IAnomalyMessage Message { get; }
}
