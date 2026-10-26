using Il2CppCustomPlayerEffects;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Features;

[ScriptType]
public class PlayerEffect
{
    private static readonly Dictionary<StatusEffectBase, PlayerEffect> _effects = new();

    private PlayerEffect(StatusEffectBase effectBase)
    {
        Base = effectBase;
    }

    public StatusEffectBase Base { get; }

    public Player Player => Player.Get(Base.Hub);

    [ScriptVisible]
    public string Name => Base.GetType().Name;

    [ScriptVisible]
    public byte Intensity => Base.Intensity;

    [ScriptVisible]
    public byte MaxIntensity => Base.MaxIntensity;

    [ScriptVisible]
    public bool IsEnabled => Base.IsEnabled;

    [ScriptVisible]
    public bool CanEnable => Base.AllowEnabling;

    [ScriptVisible]
    public bool IsLocalPlayerEffect => Base.IsLocalPlayer;

    [ScriptVisible]
    public bool IsSpectatedEffect => Base.IsSpectated;

    [ScriptVisible]
    public float Duration => Base.Duration;

    [ScriptVisible]
    public float TimeLeft => Base.TimeLeft;

    [ScriptVisible]
    public string Classification => Base.Classification.ToString();

    public static PlayerEffect Get(StatusEffectBase effectBase)
    {
        if (effectBase == null)
            return null;

        if (_effects.TryGetValue(effectBase, out PlayerEffect effect))
            return effect;

        effect = new PlayerEffect(effectBase);
        _effects[effectBase] = effect;
        return effect;
    }

    internal static bool Unregister(StatusEffectBase effectBase) =>
        effectBase != null && _effects.Remove(effectBase);

    internal static void ClearAll() => _effects.Clear();

    public override string ToString() => $"PlayerEffect({Name}, Enabled={IsEnabled}, Intensity={Intensity})";
}
