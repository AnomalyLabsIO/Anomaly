using Anomaly.Client.Api.Features;
using Anomaly.Client.Api.Scripting;
using Anomaly.Shared.Networking.Messages;
using MoonSharp.Interpreter;

namespace Anomaly.Client.Scripting.Bindings;

/// <summary>Read-only local queries. Resolve current objects afresh rather than retaining native handles.</summary>
[ScriptType("GameContext")]
internal sealed class ScriptGameContext
{
    [ScriptVisible] public Player? LocalPlayer => Api.Features.LocalPlayer.Instance;
    [ScriptVisible] public Player? SpectatedPlayer => Spectating.CurrentPlayer;
    [ScriptVisible] public int PlayerCount => Player.PlayerList.Count;
    [ScriptVisible] public RoundPhase RoundPhase => Round.CurrentPhase;
    [ScriptVisible] public bool IsRoundStarted => Round.IsStarted;
    [ScriptVisible] public bool IsRoundEnded => Round.IsEnded;

    [ScriptVisible]
    public Player? GetPlayer(int playerId) => Player.Get(playerId);

    [ScriptVisible]
    public Player? GetPlayerAt(int index)
    {
        var players = Player.PlayerList;
        return index >= 0 && index < players.Count ? players[index] : null;
    }

    [ScriptVisible]
    public Item? GetCurrentItem(int playerId)
    {
        var player = GetPlayer(playerId);
        return player == null || player.IsDestroyed ? null : player.CurrentItem;
    }

    [ScriptVisible]
    public Room? GetRoom(int playerId)
    {
        var player = GetPlayer(playerId);
        return player == null || player.IsDestroyed ? null : player.Room;
    }

    [ScriptVisible]
    public FirearmState? GetFirearmState(int playerId) => (GetCurrentItem(playerId) as FirearmItem)?.GetState();

    [ScriptVisible]
    public float? GetHealth(int playerId) => GetStat<Il2CppPlayerStatsSystem.HealthStat>(playerId);

    [ScriptVisible]
    public float? GetHumeShield(int playerId) => GetStat<Il2CppPlayerStatsSystem.HumeShieldStat>(playerId);

    [ScriptVisible]
    public float? GetStamina(int playerId) => GetStat<Il2CppPlayerStatsSystem.StaminaStat>(playerId);

    private float? GetStat<T>(int playerId) where T : Il2CppPlayerStatsSystem.StatBase
    {
        var player = GetPlayer(playerId);
        return player != null && !player.IsDestroyed && player.Stats.TryGetValue<T>(out var value) ? value : null;
    }

    [ScriptVisible]
    public ScriptPosition? GetPosition(int playerId)
    {
        var player = GetPlayer(playerId);
        if (player == null || player.IsDestroyed) return null;
        var position = player.Position;
        return MemoryBudget.Allocate(64, () => new ScriptPosition(position.x, position.y, position.z));
    }
}
