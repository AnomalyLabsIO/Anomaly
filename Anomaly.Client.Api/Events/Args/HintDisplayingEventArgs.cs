namespace Anomaly.Client.Api.Events.Args;

/// <summary>
/// Fires when a server-sent hint is about to be displayed on the local HUD.
/// Cancelling suppresses the hint render; the server's view is unaffected.
/// </summary>
public class HintDisplayingEventArgs : EventArgs, ICancellableEvent
{
    public HintDisplayingEventArgs(string text, float durationSeconds)
    {
        Text = text;
        DurationSeconds = durationSeconds;
    }

    /// <summary>The raw hint text. May contain rich-text tags and translation tokens.</summary>
    public string Text { get; }

    /// <summary>Display duration in seconds (0 if not provided by the hint).</summary>
    public float DurationSeconds { get; }

    public bool IsAllowed { get; set; } = true;
}
