using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Events.Args;
using Anomaly.Client.Api.Events.Handlers;
using Anomaly.Client.Api.Internal.Wrappers;
using Il2Cpp;
using Il2CppPlayerStatsSystem;

namespace Anomaly.Client.Internal.Stats;

internal static class HealthStateTracker
{
    private const float StaminaChangeEpsilon = 0.005f;     // ~0.5% of a normalized stamina
    private const float HumeShieldChangeEpsilon = 0.5f;    // raw HP units

    private static readonly Dictionary<ReferenceHub, StatSnapshot> _snapshots = new();
    private static readonly HashSet<ReferenceHub> _observedHubs = new();
    private static readonly List<ReferenceHub> _staleHubs = new();

    internal static void Update()
    {
        _observedHubs.Clear();

        foreach (Player player in PlayerRegistry.PlayerList)
        {
            ReferenceHub hub = player?.ReferenceHub;
            if (hub == null || !TryReadSnapshot(player, out StatSnapshot current))
                continue;

            _observedHubs.Add(hub);

            if (_snapshots.TryGetValue(hub, out StatSnapshot previous))
            {
                if (previous.LifeIdentifier != current.LifeIdentifier)
                {
                    _snapshots[hub] = current;
                    continue;
                }

                if (current.Health > previous.Health)
                    StatsEvents.OnHealed(new PlayerHealedEventArgs(player, previous.Health, current.Health));

                if (System.Math.Abs(current.Stamina - previous.Stamina) >= StaminaChangeEpsilon)
                    StatsEvents.OnStaminaChanged(new PlayerStaminaChangedEventArgs(player, previous.Stamina, current.Stamina));

                if (System.Math.Abs(current.HumeShield - previous.HumeShield) >= HumeShieldChangeEpsilon
                    || (previous.HumeShield > 0f && current.HumeShield <= 0f))
                {
                    StatsEvents.OnHumeShieldChanged(
                        new PlayerHumeShieldChangedEventArgs(player, previous.HumeShield, current.HumeShield));
                }

                _snapshots[hub] = current;
                continue;
            }

            _snapshots[hub] = current;
        }

        RemoveStale();
    }

    internal static void Clear()
    {
        _snapshots.Clear();
        _observedHubs.Clear();
        _staleHubs.Clear();
    }

    internal static void Reset(ReferenceHub hub)
    {
        if (hub != null)
            _snapshots.Remove(hub);
    }

    private static bool TryReadSnapshot(Player player, out StatSnapshot snapshot)
    {
        snapshot = default;

        if (player?.PlayerStats == null)
            return false;

        try
        {
            float health = 0f;
            float stamina = 0f;
            float humeShield = 0f;

            if (player.PlayerStats.TryGetModule<HealthStat>(out HealthStat healthStat) && healthStat != null)
                health = healthStat.CurValue;

            if (player.PlayerStats.TryGetModule<StaminaStat>(out StaminaStat staminaStat) && staminaStat != null)
                stamina = staminaStat.CurValue;

            if (player.PlayerStats.TryGetModule<HumeShieldStat>(out HumeShieldStat shieldStat) && shieldStat != null)
                humeShield = shieldStat.CurValue;

            snapshot = new StatSnapshot(health, stamina, humeShield, player.Role?.LifeIdentifier ?? 0);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void RemoveStale()
    {
        if (_snapshots.Count == 0)
            return;

        _staleHubs.Clear();

        foreach (KeyValuePair<ReferenceHub, StatSnapshot> pair in _snapshots)
        {
            if (pair.Key == null || !_observedHubs.Contains(pair.Key))
                _staleHubs.Add(pair.Key);
        }

        foreach (ReferenceHub staleHub in _staleHubs)
            _snapshots.Remove(staleHub);
    }

    private readonly record struct StatSnapshot(float Health, float Stamina, float HumeShield, int LifeIdentifier);
}
