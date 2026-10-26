using Anomaly.Client.Api.Features;
using Il2CppCassie;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

/// <summary>
/// Fired when a CASSIE line begins audible playback (engine's
/// <c>CassieLineSource.OnStartedPlaying</c> static event). Useful for mods
/// that want to flash subtitles in sync with audio.
/// </summary>
[ScriptType]
public class AnnouncerLineStartedEventArgs : EventArgs
{
    public AnnouncerLineStartedEventArgs(CassieLineSource source)
    {
        Source = source;
        Line = source?.Line;
        Word = Line != null ? new AnnouncerWord(Line) : null;
    }

    /// <summary>The raw <see cref="CassieLineSource"/> driving playback.</summary>
    public CassieLineSource Source { get; }

    /// <summary>The line being played.</summary>
    public CassieLine Line { get; }

    /// <summary>Wrapped <see cref="AnnouncerWord"/> for ergonomic access.</summary>
    [ScriptVisible]
    public AnnouncerWord Word { get; }

    /// <summary>Convenience: API name (e.g. "ALERT") of the line.</summary>
    [ScriptVisible]
    public string ApiName => Line?.ApiName;
}
