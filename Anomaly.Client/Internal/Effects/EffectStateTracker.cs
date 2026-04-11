using Anomaly.Client.API.Features;
using Anomaly.Client.Events.Args;
using Anomaly.Client.Events.Handlers;
using Anomaly.Client.Internal.Wrappers;
using Il2CppCustomPlayerEffects;
using PlayerEffectsControllerBase = Il2Cpp.PlayerEffectsController;

namespace Anomaly.Client.Internal.Effects;

internal static class EffectStateTracker
{
    private static readonly Dictionary<StatusEffectBase, EffectSnapshot> _snapshots = new();
    private static readonly HashSet<StatusEffectBase> _observedEffects = new();
    private static readonly List<StatusEffectBase> _staleEffects = new();

    internal static void Update()
    {
        _observedEffects.Clear();

        foreach (Player player in PlayerRegistry.PlayerList)
        {
            PlayerEffectsControllerBase controller = player?.PlayerEffectsController;
            if (controller?.AllEffects == null)
                continue;

            foreach (StatusEffectBase effectBase in controller.AllEffects)
            {
                if (effectBase == null || !TryReadSnapshot(effectBase, out EffectSnapshot current))
                    continue;

                _observedEffects.Add(effectBase);

                if (_snapshots.TryGetValue(effectBase, out EffectSnapshot previous))
                {
                    DispatchChanges(effectBase, previous, current);
                    _snapshots[effectBase] = current;
                    continue;
                }

                _snapshots[effectBase] = current;
            }
        }

        RemoveStaleEffects();
    }

    internal static void Clear()
    {
        _snapshots.Clear();
        _observedEffects.Clear();
        _staleEffects.Clear();
    }

    private static void DispatchChanges(StatusEffectBase effectBase, EffectSnapshot previous, EffectSnapshot current)
    {
        PlayerEffect effect = PlayerEffect.Get(effectBase);
        if (effect == null)
            return;

        if (previous.IsEnabled != current.IsEnabled)
        {
            if (current.IsEnabled)
                EffectEvents.OnEnabled(new EffectEnabledEventArgs(effect));
            else
                EffectEvents.OnDisabled(new EffectDisabledEventArgs(effect));
        }

        if (previous.Intensity != current.Intensity)
            EffectEvents.OnIntensityChanged(new EffectIntensityChangedEventArgs(effect, previous.Intensity, current.Intensity));
    }

    private static void RemoveStaleEffects()
    {
        if (_snapshots.Count == 0)
            return;

        _staleEffects.Clear();

        foreach (KeyValuePair<StatusEffectBase, EffectSnapshot> pair in _snapshots)
        {
            if (pair.Key == null || !_observedEffects.Contains(pair.Key))
                _staleEffects.Add(pair.Key);
        }

        foreach (StatusEffectBase staleEffect in _staleEffects)
            _snapshots.Remove(staleEffect);
    }

    private static bool TryReadSnapshot(StatusEffectBase effectBase, out EffectSnapshot snapshot)
    {
        try
        {
            snapshot = new EffectSnapshot(effectBase.IsEnabled, effectBase.Intensity);
            return true;
        }
        catch
        {
            snapshot = default;
            return false;
        }
    }

    private readonly record struct EffectSnapshot(bool IsEnabled, byte Intensity);
}
