using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Handlers;

/// <summary>
/// CASSIE announcer playback events. Use these to react to specific lines
/// (flash subtitles, replace audio, trigger custom HUDs).
/// </summary>
/// <remarks>
/// <see cref="LineScheduled"/> fires before audio playback begins (in the
/// Harmony prefix on <c>CassieLineSource.Schedule</c>); useful for last-minute
/// substitution or pre-roll UI. <see cref="LineStarted"/> fires when audio
/// becomes audible (engine's static <c>OnStartedPlaying</c> event); useful
/// for sync-with-audio HUD effects.
/// </remarks>
public static class AnnouncerEvents
{
    [ScriptEvent]
    public static event AnomalyEventHandler<AnnouncerLineScheduledEventArgs> LineScheduled;
    [ScriptEvent]
    public static event AnomalyEventHandler<AnnouncerLineStartedEventArgs> LineStarted;

    internal static void OnLineScheduled(AnnouncerLineScheduledEventArgs args) =>
        EventInvoker.Invoke(LineScheduled, args);

    internal static void OnLineStarted(AnnouncerLineStartedEventArgs args) =>
        EventInvoker.Invoke(LineStarted, args);
}
