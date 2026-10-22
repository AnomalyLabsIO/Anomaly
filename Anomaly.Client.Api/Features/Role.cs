using Il2CppPlayerRoles;

namespace Anomaly.Client.Api.Features;

public class Role
{
    private static readonly Dictionary<PlayerRoleBase, Role> _roles = new();
    private readonly int _lifeIdentifier;

    private Role(PlayerRoleBase roleBase)
    {
        Base = roleBase;
        _lifeIdentifier = roleBase.UniqueLifeIdentifier;
    }

    public PlayerRoleBase Base { get; }

    /// <summary>
    /// Whether the native role was destroyed, returned to its pool, or reused
    /// for a different life. Role wrappers should not be retained across lives.
    /// </summary>
    public bool IsDestroyed => Base == null || Base.Pooled || Base.UniqueLifeIdentifier != _lifeIdentifier;

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

        if (_roles.TryGetValue(roleBase, out Role role) && !role.IsDestroyed)
            return role;

        role = new Role(roleBase);
        _roles[roleBase] = role;
        return role;
    }

    /// <summary>Gets the underlying native role as a specific game role type.</summary>
    public T As<T>() where T : PlayerRoleBase => IsDestroyed ? null : Base.TryCast<T>();

    public bool Is<T>() where T : PlayerRoleBase => As<T>() != null;

    public bool TryGet<T>(out T role) where T : PlayerRoleBase
    {
        role = As<T>();
        return role != null;
    }

    internal static bool Unregister(PlayerRoleBase roleBase) =>
        roleBase != null && _roles.Remove(roleBase);

    internal static void ClearAll() => _roles.Clear();

    public override string ToString() => $"Role({Type})";
}
