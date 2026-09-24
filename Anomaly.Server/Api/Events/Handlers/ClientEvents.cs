using System;
using Anomaly.Server.Api.Events.Arguments;
using Anomaly.Server.Api.Features;
using Anomaly.Shared.Networking;
using LabApi.Events;
using LabApi.Features.Wrappers;
using Logger = LabApi.Features.Console.Logger;

namespace Anomaly.Server.Api.Events.Handlers;

/// <summary>
/// Anomaly-specific notifications using LabAPI event conventions. Subscribe in your
/// LabAPI plugin's Enable and unsubscribe in Disable. Ordinary game events remain in LabAPI.
/// </summary>
public static class ClientEvents
{
    /// <summary>The encrypted client session has completed Anomaly's initial setup.</summary>
    public static event LabEventHandler<ClientEventArgs> Connected;
    /// <summary>A validated client message was processed by Anomaly's internal handlers.</summary>
    public static event LabEventHandler<ClientMessageReceivedEventArgs> MessageReceived;
    /// <summary>The player's advertised capability snapshot changed.</summary>
    public static event LabEventHandler<ClientEventArgs> CapabilitiesChanged;
    /// <summary>The exact catalog is activated and custom item grants are available.</summary>
    public static event LabEventHandler<ClientEventArgs> ContentReady;
    /// <summary>An owned movement discontinuity was applied to a player.</summary>
    public static event LabEventHandler<PlayerMotionAppliedEventArgs> MotionApplied;

    internal static void OnConnected(ReferenceHub hub) => Publish(() => Connected.InvokeEvent(new ClientEventArgs(Player.Get(hub))));
    internal static void OnMessageReceived(ReferenceHub hub, IAnomalyMessage message) =>
        Publish(() => MessageReceived.InvokeEvent(new ClientMessageReceivedEventArgs(Player.Get(hub), message)));
    internal static void OnCapabilitiesChanged(ReferenceHub hub) =>
        Publish(() => CapabilitiesChanged.InvokeEvent(new ClientEventArgs(Player.Get(hub))));
    internal static bool OnContentReady(ReferenceHub hub)
    {
        return Publish(() =>
        {
            Player player = Player.Get(hub);
            if (player == null) throw new InvalidOperationException("The activated client's LabAPI wrapper is unavailable.");
            ContentReady.InvokeEvent(new ClientEventArgs(player));
        });
    }
    internal static void OnMotionApplied(ReferenceHub hub, string ownerId, MotionDiscontinuity motion) =>
        Publish(() => MotionApplied.InvokeEvent(new PlayerMotionAppliedEventArgs(Player.Get(hub), ownerId, motion)));

    private static bool Publish(Action invoke)
    {
        try
        {
            invoke();
            return true;
        }
        catch (Exception exception)
        {
            // EventManager owns per-subscriber isolation. Its diagnostics or wrapper
            // resolution must not turn an already-committed operation into a failure.
            try { Logger.Error($"Anomaly client event publication failed: {exception}"); }
            catch { }
            return false;
        }
    }
}
