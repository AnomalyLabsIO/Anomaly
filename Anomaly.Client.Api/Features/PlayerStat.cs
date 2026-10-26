using Il2CppPlayerStatsSystem;

using Anomaly.Client.Api.Internal;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Features;

[ScriptType]
public class PlayerStat
{
    private static readonly Dictionary<StatBase, PlayerStat> _stats = new();

    protected PlayerStat(StatBase statBase)
    {
        Base = statBase;
    }

    public StatBase Base { get; }

    [ScriptVisible]
    public bool IsDestroyed => Base == null || Base.Hub == null;

    /// <summary>
    /// Whether this stat's local synchronization policy permits observation by the
    /// current local player. This is not a receipt or freshness guarantee. Server
    /// plugins can customize synchronization; raw values remain the last local values.
    /// </summary>
    [ScriptVisible]
    public bool IsObservable
    {
        get
        {
            try
            {
                LocalPlayer viewer = LocalPlayer.Instance;
                if (IsDestroyed || viewer?.CurrentRole == null)
                    return false;
                SyncedStatBase synced = Base.TryCast<SyncedStatBase>();
                if (synced == null)
                    return Base.Hub == viewer.ReferenceHub;
                return StatVisibility.CanObserve(true, Base.Hub == viewer.ReferenceHub,
                    viewer.Role.IsAlive, (int)synced.Mode);
            }
            catch { return false; }
        }
    }

    /// <summary>Reads the last local value only when the local policy permits observation.</summary>
    public bool TryGetValue(out float value)
    {
        value = 0f;
        if (!IsObservable)
            return false;
        try
        {
            value = Value;
            return float.IsFinite(value);
        }
        catch { return false; }
    }

    public Player Player => Player.Get(Base.Hub);

    public virtual float Value => Base.CurValue;

    public float MinValue => Base.MinValue;

    public virtual float MaxValue => Base.MaxValue;

    public float NormalizedValue => Base.NormalizedValue;

    [ScriptVisible]
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
