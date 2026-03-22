using System.Collections.ObjectModel;
using Il2Cpp;
using Il2CppHints;
using Il2CppInteractables;
using Il2CppInventorySystem;
using Il2CppInventorySystem.Items;
using Il2CppInventorySystem.Searching;
using Il2CppMapGeneration;
using Il2CppPlayerRoles;
using Il2CppPlayerStatsSystem;
using Il2CppRemoteAdmin;
using UnityEngine;

namespace Anomaly.Client.API.Features;

public class Player
{
    public static ReadOnlyCollection<Player> PlayerList { get; internal set; } = new(new List<Player>());
    public static ReadOnlyDictionary<GameObject, Player> GameObjectToPlayer { get; internal set; } = new(new Dictionary<GameObject, Player>());

    public Player(GameObject gameObject) : this(ReferenceHub.GetHub(gameObject)) { }
    public Player(ReferenceHub hub)
    {
        ReferenceHub = hub;

        var list = new List<Player>(PlayerList)
        {
            this
        };
        PlayerList = new(list);

        var dic = new Dictionary<GameObject, Player>(GameObjectToPlayer);
        dic[hub.gameObject] = this;
        GameObjectToPlayer = new(dic);
    }

    public ReferenceHub ReferenceHub;

    public Vector3 Position => ReferenceHub.gameObject.transform.position;
    public Transform Transform => ReferenceHub.gameObject.transform;
    public CharacterClassManager CharacterClassManager  => ReferenceHub.characterClassManager;
    public PlayerRoleManager RoleManager => ReferenceHub.roleManager;
    public PlayerStats PlayerStats => ReferenceHub.playerStats;
    public Inventory Inventory => ReferenceHub.inventory;
    public SearchCoordinator SearchCoordinator => ReferenceHub.searchCoordinator;
    public ServerRoles  ServerRoles => ReferenceHub.serverRoles;
    
    public NicknameSync NicknameSync => ReferenceHub.nicknameSync;
    public InteractionCoordinator InteractionCoordinator => ReferenceHub.interCoordinator;
    public PlayerEffectsController PlayerEffectsController => ReferenceHub.playerEffectsController;
    public CurrentRoomPlayerCache CurrentRoomPlayerCache => ReferenceHub.CurrentRoomPlayerCache;


    public Il2CppSystem.Collections.Generic.List<ItemBase> Items
    {
        get
        {
            var itemBases = new Il2CppSystem.Collections.Generic.List<ItemBase>();
            foreach (var userInventoryItem in ReferenceHub.inventory.UserInventory.Items)
            {
                itemBases.Add(userInventoryItem.Value);
            }
            return itemBases;
        }
    }

    public static Player Get(GameObject gameObject)
    {
        if (gameObject == null)
            return null;
        Player player;
        if (Player.GameObjectToPlayer.TryGetValue(gameObject, out player))
            return player;
        return player;
    }
}