using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Client.Api.Internal.Players;
using Il2CppPlayerRoles.PlayableScps.Scp079;

namespace Anomaly.Client.Api.Features;

/// <summary>Read-only SCP-079 state received by this client for one role life.</summary>
/// <remarks>
/// Auxiliary power is selectively replicated to the owner and spectators.
/// Nullable values remain unavailable until the corresponding game RPC has
/// been received. These properties never issue a command or change server state.
/// </remarks>
public sealed class Scp079
{
    private static readonly Dictionary<Scp079Role, Scp079> _instances = new();
    private readonly int _lifeIdentifier;
    private readonly Scp079ObservationState _observation;

    private Scp079(Scp079Role role)
    {
        Base = role;
        _lifeIdentifier = role.UniqueLifeIdentifier;
        _observation = new Scp079ObservationState(_lifeIdentifier);
    }

    public Scp079Role Base { get; }

    public bool IsDestroyed => _observation.IsInvalidated || Base == null || Base.Pooled || Base.UniqueLifeIdentifier != _lifeIdentifier;

    public Player Player => !IsDestroyed && Base.TryGetOwner(out var hub) ? Player.Get(hub) : null;

    /// <summary>The camera replicated for this SCP-079, or null when unavailable.</summary>
    public FacilityCamera CurrentCamera => IsDestroyed ? null : FacilityCamera.Get(Base.CurrentCamera);

    /// <summary>
    /// True after a power update was received for this life and current viewing
    /// context. Leaving or changing a spectator target invalidates that observation.
    /// </summary>
    public bool HasAuxiliaryPower
    {
        get
        {
            if (IsDestroyed || !_observation.AuxiliaryPower.HasValue)
                return false;
            if (Base.IsLocalPlayer)
                return true;

            Player owner = Player;
            Player target = Spectating.CurrentPlayer;
            return owner != null && target != null && owner.ReferenceHub == target.ReferenceHub;
        }
    }

    /// <summary>Whether an experience/tier update was received for this role life.</summary>
    public bool HasExperience => !IsDestroyed && _observation.Experience.HasValue;

    /// <summary>The last received power value, or null when it is not currently observable.</summary>
    public float? AuxiliaryPower => HasAuxiliaryPower ? _observation.AuxiliaryPower : null;

    /// <summary>
    /// The local game's current power limit, or null while power is unavailable.
    /// Server-only changes to tier settings are not inferred by this property.
    /// </summary>
    public float? MaximumAuxiliaryPower => ReadAuxiliaryPowerSetting(regeneration: false);

    /// <summary>
    /// The local game's computed regeneration per second while power is observable,
    /// or null when the native module is unavailable. Server-only modifiers may
    /// not be reflected in this client-computed value.
    /// </summary>
    public float? AuxiliaryPowerRegeneration => ReadAuxiliaryPowerSetting(regeneration: true);

    /// <summary>The last received total experience, or null before its first update.</summary>
    public int? Experience => HasExperience ? _observation.Experience : null;

    /// <summary>The last received access tier, numbered from one, or null before its first update.</summary>
    public int? Tier => HasExperience ? _observation.Tier : null;

    public static Scp079 Get(Player player) => Get(player?.GetRole<Scp079Role>());

    public static Scp079 Get(Scp079Role role)
    {
        if (role == null || role.Pooled)
            return null;
        if (_instances.TryGetValue(role, out Scp079 wrapper) && !wrapper.IsDestroyed)
            return wrapper;

        wrapper = new Scp079(role);
        _instances[role] = wrapper;
        return wrapper;
    }

    public static bool TryGet(Player player, out Scp079 scp079)
    {
        scp079 = Get(player);
        return scp079 != null;
    }

    internal static void ObserveAuxiliaryPower(Scp079AuxManager manager)
    {
        if (manager == null)
            return;
        Scp079 wrapper = Get(manager.CastRole);
        if (wrapper == null)
            return;

        float auxiliaryPower = manager.CurrentAux;
        if (wrapper._observation.TryObserveAuxiliaryPower(manager.CastRole.UniqueLifeIdentifier, auxiliaryPower, out float? previous) &&
            previous != auxiliaryPower && wrapper.HasAuxiliaryPower)
        {
            Scp079Events.OnAuxiliaryPowerChanged(new Scp079AuxiliaryPowerChangedEventArgs(wrapper.Player, previous, auxiliaryPower));
        }
    }

    internal static void ObserveExperience(Scp079TierManager manager)
    {
        if (manager == null)
            return;
        Scp079 wrapper = Get(manager.CastRole);
        if (wrapper == null)
            return;

        int experience = manager.TotalExp;
        int tier = manager.AccessTierLevel;
        if (!wrapper._observation.TryObserveExperience(manager.CastRole.UniqueLifeIdentifier, experience, tier,
            out int? previousExperience, out int? previousTier))
            return;

        Player player = wrapper.Player;
        wrapper._observation.DispatchExperienceChanges(() => !wrapper.IsDestroyed,
            previousExperience != experience
                ? () => Scp079Events.OnExperienceChanged(new Scp079ExperienceChangedEventArgs(player, previousExperience, experience))
                : null,
            previousTier != tier
                ? () => Scp079Events.OnTierChanged(new Scp079TierChangedEventArgs(player, previousTier, tier))
                : null);
    }

    internal static void InvalidateSpectatorPower()
    {
        foreach (Scp079 wrapper in _instances.Values)
        {
            if (wrapper.IsDestroyed || !wrapper.Base.IsLocalPlayer)
                wrapper._observation.InvalidateAuxiliaryPower();
        }
    }

    internal static void Unregister(Scp079Role role)
    {
        if (role == null || !_instances.TryGetValue(role, out Scp079 wrapper))
            return;
        wrapper._observation.Invalidate();
        _instances.Remove(role);
    }

    internal static void ClearAll()
    {
        foreach (Scp079 wrapper in _instances.Values)
            wrapper._observation.Invalidate();
        _instances.Clear();
    }

    private float? ReadAuxiliaryPowerSetting(bool regeneration)
    {
        if (!HasAuxiliaryPower)
            return null;

        try
        {
            if (Base.SubroutineModule == null ||
                !Base.SubroutineModule.TryGetSubroutine<Scp079AuxManager>(out var manager) || manager == null)
                return null;

            return regeneration ? manager.RegenSpeed : manager.MaxAux;
        }
        catch
        {
            // Native subroutines can be between SpawnObject and ResetObject
            // during a role transition. That means unavailable, not zero.
            return null;
        }
    }
}
