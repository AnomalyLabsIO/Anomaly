using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

/// <summary>An observed change to the local spectator's player target.</summary>
[ScriptType]
public sealed class SpectatedPlayerChangedEventArgs : PlayerEventArgs
{
    public SpectatedPlayerChangedEventArgs(Player player, Player previousPlayer, Player currentPlayer) : base(player)
    {
        PreviousPlayer = previousPlayer;
        CurrentPlayer = currentPlayer;
    }

    /// <summary>The previous target, or null when no player was being spectated.</summary>
    [ScriptVisible]
    public Player PreviousPlayer { get; }

    /// <summary>The new target, or null when the player target was cleared.</summary>
    [ScriptVisible]
    public Player CurrentPlayer { get; }
}
