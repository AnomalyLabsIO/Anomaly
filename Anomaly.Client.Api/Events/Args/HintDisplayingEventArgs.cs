using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

/// <summary>
/// Fires when a server-sent hint is about to be displayed on the local HUD.
/// Cancelling suppresses the hint render; the server's view is unaffected.
/// </summary>
[ScriptType]
public class HintDisplayingEventArgs : EventArgs, ICancellableEvent
{
    public HintDisplayingEventArgs(string text, float durationSeconds)
    {
        Text = text;
        DurationSeconds = durationSeconds;
    }

    /// <summary>The raw hint text. May contain rich-text tags and translation tokens.</summary>
    /// <remarks>Changes are applied to text hints before they are rendered.</remarks>
    [ScriptVisible]
    public string Text { get; set; }

    /// <summary>Display duration in seconds (0 if not provided by the hint).</summary>
    /// <remarks>Only finite, non-negative changes are applied.</remarks>
    [ScriptVisible]
    public float DurationSeconds { get; set; }

    [ScriptVisible]
    public bool IsAllowed { get; set; } = true;
}
