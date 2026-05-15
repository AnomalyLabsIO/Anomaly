using Anomaly.Client.Api.Features;

namespace Anomaly.Client.Api.Events.Args;

/// <summary>
/// Fires when the local player presses the noclip toggle key, before the
/// <c>FpcNoclipToggleMessage</c> is sent to the server. Server permission
/// gating still applies; this event lets local mods (e.g., admin tooling)
/// suppress the toggle attempt entirely.
/// </summary>
public class PlayerNoclipTogglingEventArgs : PlayerEventArgs, ICancellableEvent
{
    public PlayerNoclipTogglingEventArgs(Player player) : base(player)
    {
    }

    public bool IsAllowed { get; set; } = true;
}
