using Anomaly.Client.Api.Features;
using Il2CppCassie;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

/// <summary>
/// Fired when CASSIE schedules a line for playback (before audio starts).
/// Pair with <see cref="AnnouncerLineStartedEventArgs"/> for the started-playing
/// notification.
/// </summary>
[ScriptType]
public class AnnouncerLineScheduledEventArgs : EventArgs
{
    public AnnouncerLineScheduledEventArgs(CassieLine line, double scheduledStartDspTime)
    {
        Line = line;
        Word = line != null ? new AnnouncerWord(line) : null;
        ScheduledStartDspTime = scheduledStartDspTime;
    }

    /// <summary>The raw IL2CPP <see cref="CassieLine"/>.</summary>
    public CassieLine Line { get; }

    /// <summary>Wrapped <see cref="AnnouncerWord"/> for ergonomic access.</summary>
    [ScriptVisible]
    public AnnouncerWord Word { get; }

    /// <summary>
    /// The DSP-time at which playback is scheduled to begin. Compare against
    /// <see cref="UnityEngine.AudioSettings.dspTime"/> to compute lead time.
    /// </summary>
    [ScriptVisible]
    public double ScheduledStartDspTime { get; }

    /// <summary>Convenience: API name (e.g. "ALERT") of the scheduled line.</summary>
    [ScriptVisible]
    public string ApiName => Line?.ApiName;
}
