using System.Collections.ObjectModel;
using Il2CppPlayerStatsSystem;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Features;

/// <summary>
/// Access to locally held stat modules. Raw scalar properties may contain initial or
/// stale values for players outside the local synchronization scope; use
/// <see cref="TryGetValue{T}"/> when the distinction matters.
/// </summary>
public class PlayerStatsWrapper
{
    private static readonly Dictionary<PlayerStats, PlayerStatsWrapper> _wrappers = new();

    private PlayerStatsWrapper(PlayerStats playerStats)
    {
        Base = playerStats;
    }

    public PlayerStats Base { get; }

    public bool IsDestroyed => Base == null || Base._hub == null;

    public Player Player => Player.Get(Base._hub);

    public ReadOnlyCollection<PlayerStat> Modules
    {
        get
        {
            List<PlayerStat> modules = new();
            foreach (StatBase statBase in Base.StatModules)
            {
                PlayerStat stat = PlayerStat.Get(statBase);
                if (stat != null)
                    modules.Add(stat);
            }

            return modules.AsReadOnly();
        }
    }

    public float Health => TryGet<HealthStat>(out HealthStat healthStat) ? healthStat.CurValue : 0f;

    public float MaxHealth => TryGet<HealthStat>(out HealthStat healthStat) ? healthStat.MaxValue : 0f;

    public float HumeShield => TryGet<HumeShieldStat>(out HumeShieldStat humeShieldStat) ? humeShieldStat.CurValue : 0f;

    public float MaxHumeShield => TryGet<HumeShieldStat>(out HumeShieldStat humeShieldStat) ? humeShieldStat.MaxValue : 0f;

    public float ArtificialHealth => TryGet<AhpStat>(out AhpStat ahpStat) ? ahpStat.CurValue : 0f;

    public float MaxArtificialHealth => TryGet<AhpStat>(out AhpStat ahpStat) ? ahpStat.MaxValue : 0f;

    /// <summary>
    /// Last locally held stamina (0..MaxStamina). Stock synchronization sends
    /// health, stamina, Hume shield and AHP to the owner and non-alive viewers,
    /// not arbitrary alive observers. Values may be unavailable or stale.
    /// Use <see cref="TryGetValue{T}"/> for a policy-checked read.
    /// </summary>
    public float Stamina => TryGet<StaminaStat>(out StaminaStat staminaStat) ? staminaStat.CurValue : 0f;

    /// <inheritdoc cref="Stamina"/>
    public float MaxStamina => TryGet<StaminaStat>(out StaminaStat staminaStat) ? staminaStat.MaxValue : 0f;

    /// <summary>
    /// Reads a stat only when its local synchronization policy permits observation.
    /// This does not guarantee that a new network sample has arrived.
    /// </summary>
    public bool TryGetValue<T>(out float value) where T : StatBase
    {
        value = 0f;
        return TryGet<T>(out T module) && PlayerStat.Get(module).TryGetValue(out value);
    }

    public bool TryGet<T>(out T module) where T : StatBase
    {
        module = null;

        try
        {
            if (ScriptHostAccess.IsActive && Base != null)
                ScriptHostAccess.ChargeRegistryEntries(Base._statModules?.Length ?? PlayerStats.DefinedModules.Length);
            return Base.TryGetModule(out module) && module != null;
        }
        catch (Exception) when (!ScriptHostAccess.IsActive)
        {
            return false;
        }
    }

    public static PlayerStatsWrapper Get(PlayerStats playerStats)
    {
        if (playerStats == null)
            return null;

        if (_wrappers.TryGetValue(playerStats, out PlayerStatsWrapper wrapper))
            return wrapper;

        ScriptHostAccess.ChargeRegistryEntries(_wrappers.Count);
        ScriptHostAccess.ChargeWrapperCreation();
        wrapper = new PlayerStatsWrapper(playerStats);
        _wrappers[playerStats] = wrapper;
        return wrapper;
    }

    internal static bool Unregister(PlayerStats playerStats)
    {
        if (playerStats == null)
            return false;

        try
        {
            foreach (StatBase statBase in playerStats.StatModules)
                PlayerStat.Unregister(statBase);
        }
        catch
        {
            // The parent wrapper can still be evicted when an IL2CPP child
            // collection is already being torn down.
        }

        return _wrappers.Remove(playerStats);
    }

    internal static void ClearAll() => _wrappers.Clear();

    public override string ToString() => $"PlayerStatsWrapper(Health={Health}, Hume={HumeShield}, AHP={ArtificialHealth})";
}
