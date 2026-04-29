using System.Collections.ObjectModel;
using Il2CppPlayerStatsSystem;

namespace Anomaly.Client.Api.Features;

public class PlayerStatsWrapper
{
    private static readonly Dictionary<PlayerStats, PlayerStatsWrapper> _wrappers = new();

    private PlayerStatsWrapper(PlayerStats playerStats)
    {
        Base = playerStats;
    }

    public PlayerStats Base { get; }

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
    /// Current stamina (0..MaxStamina). Available on both local and remote
    /// players because <see cref="StaminaStat"/> is replicated.
    /// </summary>
    public float Stamina => TryGet<StaminaStat>(out StaminaStat staminaStat) ? staminaStat.CurValue : 0f;

    /// <inheritdoc cref="Stamina"/>
    public float MaxStamina => TryGet<StaminaStat>(out StaminaStat staminaStat) ? staminaStat.MaxValue : 0f;

    public bool TryGet<T>(out T module) where T : StatBase
    {
        module = null;

        try
        {
            return Base.TryGetModule(out module) && module != null;
        }
        catch
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

        wrapper = new PlayerStatsWrapper(playerStats);
        _wrappers[playerStats] = wrapper;
        return wrapper;
    }

    public override string ToString() => $"PlayerStatsWrapper(Health={Health}, Hume={HumeShield}, AHP={ArtificialHealth})";
}
