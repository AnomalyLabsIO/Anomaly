using Anomaly.Client.Api.Features;
using Il2CppPlayerStatsSystem;

namespace Anomaly.Client.Api.Events.Args;

public class PlayerDamagedEventArgs : PlayerEventArgs
{
    public PlayerDamagedEventArgs(Player player, DamageHandlerBase damageHandler) : base(player)
    {
        DamageHandler = damageHandler;
    }

    public DamageHandlerBase DamageHandler { get; }
}
