using System.Collections.ObjectModel;
using Anomaly.Client.Api.Features;
using Il2Cpp;
using UnityEngine;

namespace Anomaly.Client.Api.Internal.Wrappers;

internal static class PlayerRegistry
{
    private static readonly Dictionary<ReferenceHub, Player> _hubToPlayer = new();
    private static readonly Dictionary<GameObject, Player> _gameObjectToPlayer = new();
    private static readonly List<Player> _players = new();
    private static readonly List<ReferenceHub> _staleHubs = new();
    private static readonly ReadOnlyCollection<Player> _readonlyPlayers = new(_players);
    private static readonly ReadOnlyDictionary<GameObject, Player> _readonlyGameObjectToPlayer = new(_gameObjectToPlayer);

    internal static ReadOnlyCollection<Player> PlayerList => _readonlyPlayers;
    internal static ReadOnlyDictionary<GameObject, Player> GameObjectToPlayer => _readonlyGameObjectToPlayer;

    internal static Player Get(ReferenceHub hub)
    {
        if (hub == null)
            return null;

        if (TryGetExisting(hub, out Player existing))
        {
            if (existing is LocalPlayer || !IsLocalHub(hub))
                return existing;

            return PromoteLocalPlayer(hub, existing);
        }

        return AddWrapper(IsLocalHub(hub) ? new LocalPlayer(hub) : new Player(hub));
    }

    internal static Player Get(GameObject gameObject)
    {
        if (gameObject == null)
            return null;

        if (_gameObjectToPlayer.TryGetValue(gameObject, out Player player))
            return player;

        if (!ReferenceHub.TryGetHub(gameObject, out ReferenceHub hub))
            return null;

        return Get(hub);
    }

    internal static Player Get(int playerId)
    {
        if (!ReferenceHub.TryGetHub(playerId, out ReferenceHub hub))
            return null;

        return Get(hub);
    }

    internal static bool TryGet(ReferenceHub hub, out Player player)
    {
        player = Get(hub);
        return player != null;
    }

    internal static bool TryGet(GameObject gameObject, out Player player)
    {
        player = Get(gameObject);
        return player != null;
    }

    internal static LocalPlayer GetLocal()
    {
        if (TryGetLocalHub(out ReferenceHub localHub))
        {
            if (TryGetExisting(localHub, out Player existing))
                return existing as LocalPlayer ?? PromoteLocalPlayer(localHub, existing);

            return AddWrapper(new LocalPlayer(localHub)) as LocalPlayer;
        }

        foreach (Player player in _players)
        {
            if (player is LocalPlayer localPlayer && player.ReferenceHub != null)
                return localPlayer;
        }

        return null;
    }

    internal static void Resync()
    {
        HashSet<ReferenceHub> observedHubs = new();

        if (ReferenceHub.AllHubs != null)
        {
            foreach (ReferenceHub hub in ReferenceHub.AllHubs)
            {
                if (hub == null)
                    continue;

                observedHubs.Add(hub);
                Get(hub);
            }
        }

        if (TryGetLocalHub(out ReferenceHub localHub))
        {
            observedHubs.Add(localHub);
            Get(localHub);
        }

        RemoveMissing(observedHubs);
    }

    internal static bool Remove(ReferenceHub hub)
    {
        if (hub == null || !TryGetExisting(hub, out Player player))
            return false;

        RemoveHubMappings(player);
        _players.Remove(player);

        if (hub.gameObject != null)
            _gameObjectToPlayer.Remove(hub.gameObject);

        if (hub.gameObject == null)
            RemoveGameObjectMapping(player);

        return true;
    }

    internal static void Clear()
    {
        _hubToPlayer.Clear();
        _gameObjectToPlayer.Clear();
        _players.Clear();
    }

    private static Player AddWrapper(Player player)
    {
        _hubToPlayer[player.ReferenceHub] = player;
        _players.Add(player);

        if (player.GameObject != null)
            _gameObjectToPlayer[player.GameObject] = player;

        return player;
    }

    private static LocalPlayer PromoteLocalPlayer(ReferenceHub hub, Player existing)
    {
        LocalPlayer localPlayer = new(hub);
        int index = _players.IndexOf(existing);

        if (index >= 0)
            _players[index] = localPlayer;
        else
            _players.Add(localPlayer);

        RemoveHubMappings(existing);
        _hubToPlayer[hub] = localPlayer;

        if (hub.gameObject != null)
            _gameObjectToPlayer[hub.gameObject] = localPlayer;
        else
            RemoveGameObjectMapping(existing);

        return localPlayer;
    }

    private static void RemoveMissing(HashSet<ReferenceHub> observedHubs)
    {
        if (_hubToPlayer.Count == 0)
            return;

        _staleHubs.Clear();

        foreach (KeyValuePair<ReferenceHub, Player> pair in _hubToPlayer)
        {
            if (pair.Key == null || pair.Key.gameObject == null || !ContainsHub(observedHubs, pair.Key))
                _staleHubs.Add(pair.Key);
        }

        foreach (ReferenceHub staleHub in _staleHubs)
            Remove(staleHub);
    }

    private static void RemoveGameObjectMapping(Player player)
    {
        GameObject keyToRemove = null;

        foreach (KeyValuePair<GameObject, Player> pair in _gameObjectToPlayer)
        {
            if (ReferenceEquals(pair.Value, player))
            {
                keyToRemove = pair.Key;
                break;
            }
        }

        if (keyToRemove != null)
            _gameObjectToPlayer.Remove(keyToRemove);
    }

    private static void RemoveHubMappings(Player player)
    {
        _staleHubs.Clear();

        foreach (KeyValuePair<ReferenceHub, Player> pair in _hubToPlayer)
        {
            if (ReferenceEquals(pair.Value, player))
                _staleHubs.Add(pair.Key);
        }

        foreach (ReferenceHub staleHub in _staleHubs)
            _hubToPlayer.Remove(staleHub);
    }

    private static bool TryGetExisting(ReferenceHub hub, out Player player)
    {
        if (_hubToPlayer.TryGetValue(hub, out player))
            return true;

        if (hub.gameObject != null && _gameObjectToPlayer.TryGetValue(hub.gameObject, out player))
        {
            _hubToPlayer[hub] = player;
            return true;
        }

        foreach (KeyValuePair<ReferenceHub, Player> pair in _hubToPlayer)
        {
            if (!AreSameHub(pair.Key, hub))
                continue;

            player = pair.Value;
            _hubToPlayer[hub] = player;
            return true;
        }

        player = null;
        return false;
    }

    private static bool ContainsHub(HashSet<ReferenceHub> hubs, ReferenceHub target)
    {
        if (hubs.Contains(target))
            return true;

        foreach (ReferenceHub hub in hubs)
        {
            if (AreSameHub(hub, target))
                return true;
        }

        return false;
    }

    private static bool IsLocalHub(ReferenceHub hub)
    {
        return TryGetLocalHub(out ReferenceHub localHub) && AreSameHub(localHub, hub);
    }

    internal static bool TryGetLocalHub(out ReferenceHub localHub)
    {
        localHub = null;

        try
        {
            if (ReferenceHub.TryGetLocalHub(out localHub) && localHub != null)
                return true;
        }
        catch
        {
        }

        try
        {
#pragma warning disable CS0618
            if (ReferenceHub.LocalHub != null)
            {
                localHub = ReferenceHub.LocalHub;
                return true;
            }
#pragma warning restore CS0618

            if (ReferenceHub._localHub != null)
            {
                localHub = ReferenceHub._localHub;
                return true;
            }

            if (ReferenceHub.AllHubs == null)
                return false;

            foreach (ReferenceHub hub in ReferenceHub.AllHubs)
            {
                if (hub == null)
                    continue;

                if (IsMarkedLocal(hub))
                {
                    localHub = hub;
                    return true;
                }
            }
        }
        catch
        {
        }

        return false;
    }

    private static bool IsMarkedLocal(ReferenceHub hub)
    {
        try
        {
            if (hub.authManager != null && hub.authManager.isLocalPlayer)
                return true;
        }
        catch
        {
        }

        try
        {
            return hub.networkIdentity != null && hub.networkIdentity.isLocalPlayer;
        }
        catch
        {
            return false;
        }
    }

    private static bool AreSameHub(ReferenceHub left, ReferenceHub right)
    {
        if (left == null || right == null)
            return false;

        try
        {
            if (left == right)
                return true;
        }
        catch
        {
        }

        try
        {
            if (left.gameObject != null && right.gameObject != null && left.gameObject == right.gameObject)
                return true;
        }
        catch
        {
        }

        try
        {
            return left.networkIdentity != null && right.networkIdentity != null && left.networkIdentity == right.networkIdentity;
        }
        catch
        {
            return false;
        }
    }
}
