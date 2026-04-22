using Anomaly.Client.Api.Features;
using Il2CppPlayerRoles;

namespace Anomaly.Client.Api.Events.Args;

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
