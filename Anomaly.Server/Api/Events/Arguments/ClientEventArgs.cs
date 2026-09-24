using System;
using LabApi.Events.Arguments.Interfaces;
using LabApi.Features.Wrappers;

namespace Anomaly.Server.Api.Events.Arguments;

/// <summary>A notification concerning an authenticated Anomaly client.</summary>
public class ClientEventArgs : EventArgs, IPlayerEvent
{
    internal ClientEventArgs(Player player) => Player = player;
    /// <summary>The native LabAPI player.</summary>
    public Player Player { get; }
}
