using Il2CppPlayerStatsSystem;

namespace Anomaly.Client.API.Features;

public class PlayerStat
{
    private static readonly Dictionary<StatBase, PlayerStat> _stats = new();

    protected PlayerStat(StatBase statBase)
    {
        Base = statBase;
    }

    public StatBase Base { get; }

    public Player Player => Player.Get(Base.Hub);

    public virtual float Value => Base.CurValue;

    public float MinValue => Base.MinValue;

    public virtual float MaxValue => Base.MaxValue;

    public float NormalizedValue => Base.NormalizedValue;

    public string Name => Base.GetType().Name;

    public static PlayerStat Get(StatBase statBase)
    {
        if (statBase == null)
            return null;

        if (_stats.TryGetValue(statBase, out PlayerStat stat))
            return stat;

        stat = new PlayerStat(statBase);
        _stats[statBase] = stat;
        return stat;
    }

    public override string ToString() => $"PlayerStat({Name}, {Value}/{MaxValue})";
}
