using System.Collections.ObjectModel;
using Il2CppCustomPlayerEffects;
using PlayerEffectsControllerBase = Il2Cpp.PlayerEffectsController;

namespace Anomaly.Client.Api.Features;

public class PlayerEffects
{
    private static readonly Dictionary<PlayerEffectsControllerBase, PlayerEffects> _wrappers = new();

    private PlayerEffects(PlayerEffectsControllerBase controller)
    {
        Base = controller;
    }

    public PlayerEffectsControllerBase Base { get; }

    public Player Player => Player.Get(Base._hub);

    public int Count => Base.EffectsLength;

    public ReadOnlyCollection<PlayerEffect> All
    {
        get
        {
            List<PlayerEffect> effects = new();
            foreach (StatusEffectBase effectBase in Base.AllEffects)
            {
                PlayerEffect effect = PlayerEffect.Get(effectBase);
                if (effect != null)
                    effects.Add(effect);
            }

            return effects.AsReadOnly();
        }
    }

    public ReadOnlyCollection<PlayerEffect> Enabled
    {
        get
        {
            List<PlayerEffect> effects = new();
            foreach (PlayerEffect effect in All)
            {
                if (effect.IsEnabled)
                    effects.Add(effect);
            }

            return effects.AsReadOnly();
        }
    }

    public PlayerEffect Get(string effectName)
    {
        foreach (PlayerEffect effect in All)
        {
            if (effect.Name.Equals(effectName, StringComparison.OrdinalIgnoreCase))
                return effect;
        }

        return null;
    }

    public static PlayerEffects Get(PlayerEffectsControllerBase controller)
    {
        if (controller == null)
            return null;

        if (_wrappers.TryGetValue(controller, out PlayerEffects wrapper))
            return wrapper;

        wrapper = new PlayerEffects(controller);
        _wrappers[controller] = wrapper;
        return wrapper;
    }

    public override string ToString() => $"PlayerEffects(Count={Count})";
}
