namespace Anomaly.Client.Api.Features;

/// <summary>
/// Local-only broadcast display, mirroring LabAPI's <c>Server.SendBroadcast</c>
/// but routed through the client's broadcast queue without server cooperation.
/// Useful for mod-internal announcements (round summaries, custom event toasts,
/// debug overlays, etc.).
/// </summary>
public static class LocalBroadcast
{
    /// <summary>
    /// Broadcast presentation flags. Mirrors <c>Broadcast.BroadcastFlags</c>.
    /// </summary>
    [System.Flags]
    public enum Flags : byte
    {
        Normal = 0,
        Truncated = 1,
        AdminChat = 2
    }

    /// <summary>
    /// Adds a broadcast to the local queue.
    /// </summary>
    /// <param name="text">The text to display. Supports TextMeshPro rich text.</param>
    /// <param name="durationSeconds">How long the broadcast stays on screen.</param>
    /// <param name="flags">Presentation flags. Defaults to <see cref="Flags.Normal"/>.</param>
    /// <returns>True if the broadcast was queued, false if the broadcast singleton isn't initialized yet.</returns>
    public static bool Show(string text, ushort durationSeconds, Flags flags = Flags.Normal)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        try
        {
            Il2Cpp.Broadcast.AddElement(text, durationSeconds, (Il2Cpp.Broadcast.BroadcastFlags)flags);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Clears all queued broadcasts via the singleton's local clear path.
    /// </summary>
    public static bool Clear()
    {
        try
        {
            var singleton = Il2Cpp.Broadcast.Singleton;
            if (singleton == null)
                return false;
            singleton.UserCode_RpcClearElements();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
