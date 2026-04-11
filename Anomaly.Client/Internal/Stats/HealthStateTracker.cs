using Anomaly.Client.API.Features;
using Anomaly.Client.Events.Args;
using Anomaly.Client.Events.Handlers;
using Anomaly.Client.Internal.Wrappers;
using Il2Cpp;
using Il2CppPlayerStatsSystem;

namespace Anomaly.Client.Internal.Stats;

internal static class HealthStateTracker
{
    private static readonly Dictionary<ReferenceHub, HealthSnapshot> _snapshots = new();
    private static readonly HashSet<ReferenceHub> _observedHubs = new();
    private static readonly List<ReferenceHub> _staleHubs = new();

    internal static void Update()
    {
        _observedHubs.Clear();

        foreach (Player player in PlayerRegistry.PlayerList)
        {
            ReferenceHub hub = player?.ReferenceHub;
            if (hub == null || !TryReadSnapshot(player, out HealthSnapshot current))
                continue;

            _observedHubs.Add(hub);

            if (_snapshots.TryGetValue(hub, out HealthSnapshot previous))
            {
                if (previous.LifeIdentifier != current.LifeIdentifier)
                {
                    _snapshots[hub] = current;
                    continue;
                }

                if (current.Health > previous.Health)
                    StatsEvents.OnHealed(new PlayerHealedEventArgs(player, previous.Health, current.Health));

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

    private static bool TryReadSnapshot(Player player, out HealthSnapshot snapshot)
    {
        snapshot = default;

        if (player?.PlayerStats == null)
            return false;

        try
        {
            if (!player.PlayerStats.TryGetModule<HealthStat>(out HealthStat healthStat) || healthStat == null)
                return false;

            snapshot = new HealthSnapshot(healthStat.CurValue, player.Role?.LifeIdentifier ?? 0);
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

        foreach (KeyValuePair<ReferenceHub, HealthSnapshot> pair in _snapshots)
        {
            if (pair.Key == null || !_observedHubs.Contains(pair.Key))
                _staleHubs.Add(pair.Key);
        }

        foreach (ReferenceHub staleHub in _staleHubs)
            _snapshots.Remove(staleHub);
    }

    private readonly record struct HealthSnapshot(float Health, int LifeIdentifier);
}
