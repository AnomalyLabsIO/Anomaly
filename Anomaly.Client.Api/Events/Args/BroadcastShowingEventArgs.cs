namespace Anomaly.Client.Api.Events.Args;

/// <summary>
/// Fires when a server-sent broadcast is about to be rendered on the local
/// HUD. Cancelling suppresses the local render; the server's view is unaffected.
/// </summary>
public class BroadcastShowingEventArgs : EventArgs, ICancellableEvent
{
    public BroadcastShowingEventArgs(string text, ushort durationSeconds, byte flags)
    {
        Text = text;
        DurationSeconds = durationSeconds;
        Flags = flags;
    }

    /// <summary>The broadcast text the server sent.</summary>
    public string Text { get; }

    /// <summary>How long the broadcast was intended to display, in seconds.</summary>
    public ushort DurationSeconds { get; }

    /// <summary>Raw <c>Broadcast.BroadcastFlags</c> byte (Truncated, AdminChat, etc.).</summary>
    public byte Flags { get; }

    public bool IsAllowed { get; set; } = true;
}
