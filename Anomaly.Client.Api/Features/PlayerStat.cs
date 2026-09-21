using Il2CppPlayerStatsSystem;

namespace Anomaly.Client.Api.Features;

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

    internal static bool Unregister(StatBase statBase) =>
        statBase != null && _stats.Remove(statBase);

    internal static void ClearAll() => _stats.Clear();

    public override string ToString() => $"PlayerStat({Name}, {Value}/{MaxValue})";
}
