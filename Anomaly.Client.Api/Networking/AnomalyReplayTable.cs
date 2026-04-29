using Anomaly.Shared.Networking;

namespace Anomaly.Client.Api.Networking;

/// <summary>
/// Client-side replay-protection state for the Anomaly 0xFE channel. There's
/// only ever one remote (the server), so state is a single static instance.
/// <see cref="Reset"/> is wired on <c>ClientEvents.Disconnected</c> alongside
/// the other per-session cleanup so a reconnect starts fresh.
/// </summary>
public static class AnomalyReplayTable
{
    public static AnomalyReplayState Instance { get; private set; } = new AnomalyReplayState();

    public static void Reset()
    {
        Instance = new AnomalyReplayState();
    }
}
