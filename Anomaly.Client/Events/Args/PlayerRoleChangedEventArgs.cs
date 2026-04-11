using Anomaly.Client.API.Features;
using Il2CppPlayerRoles;

namespace Anomaly.Client.Events.Args;

public class PlayerRoleChangedEventArgs : PlayerEventArgs
{
    public PlayerRoleChangedEventArgs(Player player, PlayerRoleBase previousRole, PlayerRoleBase newRole) : base(player)
    {
        PreviousRole= Role.Get(previousRole);
        NewRole = Role.Get(newRole);
    }

    public Role PreviousRole { get; }

    public Role NewRole { get; }
}
