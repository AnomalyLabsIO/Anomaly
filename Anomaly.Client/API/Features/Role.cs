using Il2CppPlayerRoles;

namespace Anomaly.Client.API.Features;

public class Role
{
    private static readonly Dictionary<PlayerRoleBase, Role> _roles = new();

    private Role(PlayerRoleBase roleBase)
    {
        Base = roleBase;
    }

    public PlayerRoleBase Base { get; }

    public RoleTypeId Type => Base.RoleTypeId;

    public Team Team => Base.Team;

    public int LifeIdentifier => Base.UniqueLifeIdentifier;

    public string Name => Type.ToString();

    public bool IsAlive => Team != Team.Dead;

    public bool IsHuman => IsAlive && Team != Team.SCPs;

    public bool IsScp => Team == Team.SCPs;

    public static Role Get(PlayerRoleBase roleBase)
    {
        if (roleBase == null)
            return null;

        if (_roles.TryGetValue(roleBase, out Role role))
            return role;

        role = new Role(roleBase);
        _roles[roleBase] = role;
        return role;
    }

    public override string ToString() => $"Role({Type})";
}
