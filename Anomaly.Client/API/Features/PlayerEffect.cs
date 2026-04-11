using Il2CppCustomPlayerEffects;

namespace Anomaly.Client.API.Features;

public class PlayerEffect
{
    private static readonly Dictionary<StatusEffectBase, PlayerEffect> _effects = new();

    private PlayerEffect(StatusEffectBase effectBase)
    {
        Base = effectBase;
    }

    public StatusEffectBase Base { get; }

    public Player Player => Player.Get(Base.Hub);

    public string Name => Base.GetType().Name;

    public byte Intensity => Base.Intensity;

    public byte MaxIntensity => Base.MaxIntensity;

    public bool IsEnabled => Base.IsEnabled;

    public bool CanEnable => Base.AllowEnabling;

    public bool IsLocalPlayerEffect => Base.IsLocalPlayer;

    public bool IsSpectatedEffect => Base.IsSpectated;

    public float Duration => Base.Duration;

    public float TimeLeft => Base.TimeLeft;

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

    public override string ToString() => $"PlayerEffect({Name}, Enabled={IsEnabled}, Intensity={Intensity})";
}
