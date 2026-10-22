using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Client.Api.Internal;
using Anomaly.Client.Api.Internal.Wrappers;
using Il2Cpp;
using Il2CppPlayerStatsSystem;

namespace Anomaly.Client.Internal.Stats;

internal static class HealthStateTracker
{
    private const float StaminaChangeEpsilon = 0.005f;
    private const float HumeShieldChangeEpsilon = 0.5f;
    private static readonly Dictionary<StatBase, StatObservation> _snapshots = new();
    private static readonly HashSet<StatBase> _observed = new();
    private static readonly List<StatBase> _stale = new();
    private static int _generation;

    internal static void Update()
    {
        LocalPlayer viewer = LocalPlayer.Instance;
        if (viewer?.CurrentRole == null)
        {
            Clear();
            return;
        }

        int generation = _generation;
        int viewerLife = viewer.CurrentRole.UniqueLifeIdentifier;
        _observed.Clear();
        foreach (Player player in PlayerRegistry.PlayerList)
        {
            if (player?.ReferenceHub == null || player.PlayerStats == null || player.CurrentRole == null)
                continue;

            foreach (StatBase module in player.PlayerStats.StatModules)
            {
                if (module == null)
                    continue;
                PlayerStat stat = PlayerStat.Get(module);
                if (!stat.TryGetValue(out float value))
                    continue;
                float maximum;
                try { maximum = stat.MaxValue; }
                catch { continue; }

                _observed.Add(module);
                if (!_snapshots.TryGetValue(module, out StatObservation observation))
                    _snapshots[module] = observation = new StatObservation();
                if (!observation.Sample(player.CurrentRole.UniqueLifeIdentifier, viewerLife, true,
                    value, maximum, out float previous, out float previousMaximum))
                    continue;

                var args = new PlayerStatChangedEventArgs(player, stat, previous, value, previousMaximum, maximum);
                StatsEvents.OnChanged(args);
                if (generation != _generation) return;
                if (args.MaximumChanged)
                    StatsEvents.OnMaximumChanged(args);
                if (generation != _generation) return;
                if (!args.ValueChanged) continue;

                if (module.TryCast<HealthStat>() != null)
                {
                    StatsEvents.OnHealthChanged(args);
                    if (generation != _generation) return;
                    if (value > previous)
                        StatsEvents.OnHealed(new PlayerHealedEventArgs(player, previous, value));
                }
                else if (module.TryCast<AhpStat>() != null)
                    StatsEvents.OnArtificialHealthChanged(args);
                else if (module.TryCast<StaminaStat>() != null && System.Math.Abs(value - previous) >= StaminaChangeEpsilon)
                    StatsEvents.OnStaminaChanged(new PlayerStaminaChangedEventArgs(player, previous, value));
                else if (module.TryCast<HumeShieldStat>() != null &&
                    (System.Math.Abs(value - previous) >= HumeShieldChangeEpsilon || (previous > 0f && value <= 0f)))
                    StatsEvents.OnHumeShieldChanged(new PlayerHumeShieldChangedEventArgs(player, previous, value));
                if (generation != _generation) return;
            }
        }

        _stale.Clear();
        foreach (StatBase module in _snapshots.Keys)
            if (!_observed.Contains(module)) _stale.Add(module);
        foreach (StatBase module in _stale)
            _snapshots.Remove(module);
    }

    internal static void Clear()
    {
        _generation++;
        _snapshots.Clear();
        _observed.Clear();
        _stale.Clear();
    }

    internal static void Reset(ReferenceHub hub)
    {
        _generation++;
        if (hub == null) return;
        _stale.Clear();
        foreach (StatBase module in _snapshots.Keys)
            if (module == null || module.Hub == null || module.Hub == hub) _stale.Add(module);
        foreach (StatBase module in _stale)
            _snapshots.Remove(module);
    }
}
