using Anomaly.Client.Api.Features;
using Il2CppPlayerRoles;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Events.Args;

[ScriptType]
public class PlayerRoleChangedEventArgs : PlayerEventArgs
{
    public PlayerRoleChangedEventArgs(Player player, PlayerRoleBase previousRole, PlayerRoleBase newRole) : base(player)
    {
        PreviousRole= Role.Get(previousRole);
        NewRole = Role.Get(newRole);
    }

    [ScriptVisible]
    public Role PreviousRole { get; }

    [ScriptVisible]
    public Role NewRole { get; }
}
