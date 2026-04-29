using System.Collections.ObjectModel;
using Anomaly.Client.Api.Internal.Wrappers;
using Il2Cpp;
using Il2CppHints;
using Il2CppInteractables;
using Il2CppInventorySystem;
using Il2CppInventorySystem.Items;
using Il2CppInventorySystem.Searching;
using Il2CppMapGeneration;
using Il2CppPlayerRoles;
using Il2CppPlayerRoles.FirstPersonControl;
using Il2CppPlayerRoles.FirstPersonControl.Thirdperson;
using Il2CppPlayerRoles.FirstPersonControl.Thirdperson.Subcontrollers;
using Il2CppPlayerStatsSystem;
using Il2CppRemoteAdmin;
using UnityEngine;

namespace Anomaly.Client.Api.Features;

public class Player
{
    internal Player(ReferenceHub hub)
    {
        ReferenceHub = hub;
    }

    internal Player(GameObject gameObject) : this(ReferenceHub.GetHub(gameObject)) { }

    public static ReadOnlyCollection<Player> PlayerList => PlayerRegistry.PlayerList;

    public static ReadOnlyDictionary<GameObject, Player> GameObjectToPlayer => PlayerRegistry.GameObjectToPlayer;

    public ReferenceHub ReferenceHub { get; }

    public GameObject GameObject => ReferenceHub.gameObject;

    public bool IsLocal => this is LocalPlayer;

    public Vector3 Position => Transform.position;
    public Transform Transform => GameObject.transform;
    public CharacterClassManager CharacterClassManager => ReferenceHub.characterClassManager;
    public PlayerRoleManager RoleManager => ReferenceHub.roleManager;
    public PlayerRoleBase CurrentRole => RoleManager.CurrentRole;
    public Role Role => Role.Get(CurrentRole);
    public PlayerStats PlayerStats => ReferenceHub.playerStats;
    public PlayerStatsWrapper Stats => PlayerStatsWrapper.Get(PlayerStats);
    public Inventory InventoryBase => ReferenceHub.inventory;
    public PlayerInventory Inventory => PlayerInventory.Get(InventoryBase);
    public SearchCoordinator SearchCoordinator => ReferenceHub.searchCoordinator;
    public ServerRoles ServerRoles => ReferenceHub.serverRoles;

    public NicknameSync NicknameSync => ReferenceHub.nicknameSync;
    public InteractionCoordinator InteractionCoordinator => ReferenceHub.interCoordinator;
    public PlayerEffectsController PlayerEffectsController => ReferenceHub.playerEffectsController;
    public PlayerEffects Effects => PlayerEffects.Get(PlayerEffectsController);
    public CurrentRoomPlayerCache CurrentRoomPlayerCache => ReferenceHub.CurrentRoomPlayerCache;
    public Room Room => Room.Get(this);

    public ItemIdentifier CurrentItemIdentifier => ReferenceHub.inventory.CurItem;
    public Item CurrentItem => Inventory.CurrentItem;

    public FirstPersonMovementModule FirstPersonMovementModule =>
        (ReferenceHub.roleManager.CurrentRole.Cast<IFpcRole>())?.FpcModule;

    public CharacterModel CharacterModel => FirstPersonMovementModule?.CharacterModelInstance;
    
    public EmotionSubcontroller EmotionSubcontroller
    {
        get
        {
            if(CharacterModel is not AnimatedCharacterModel) return null;
            EmotionSubcontroller subcontroller = null;
            if(!(CharacterModel as AnimatedCharacterModel).TryGetSubcontroller<EmotionSubcontroller>(out subcontroller)) return null;
            return subcontroller;
        }
    }

    public ReadOnlyCollection<ItemBase> ItemBases
    {
        get
        {
            List<ItemBase> itemBases = new();
            foreach (var userInventoryItem in ReferenceHub.inventory.UserInventory.Items)
            {
                itemBases.Add(userInventoryItem.Value);
            }

            return itemBases.AsReadOnly();
        }
    }

    public ReadOnlyCollection<Item> Items => Item.Get(ItemBases);

    public Transform GetAnchor(PlayerAnchor anchor) => PlayerAnchors.Get(this, anchor);

    public bool TryGetAnchor(PlayerAnchor anchor, out Transform transform) =>
        PlayerAnchors.TryGet(this, anchor, out transform);

    /// <summary>
    /// The current movement-state byte from the player's FPC module
    /// (<c>Crouching</c>, <c>Sneaking</c>, <c>Walking</c>, <c>Sprinting</c>).
    /// Returns <see cref="PlayerMovementState.Walking"/> if the player has no
    /// FPC module (e.g. SCP-079, dead, spectator).
    /// </summary>
    public PlayerMovementState MovementState
    {
        get
        {
            FirstPersonMovementModule module = FirstPersonMovementModule;
            if (module == null)
                return PlayerMovementState.Walking;
            return module.CurrentMovementState;
        }
    }

    /// <summary>True if the player is in the crouching or sneaking state.</summary>
    public bool IsCrouching
    {
        get
        {
            PlayerMovementState s = MovementState;
            return s == PlayerMovementState.Crouching || s == PlayerMovementState.Sneaking;
        }
    }

    /// <summary>True if the player is in the sprinting state.</summary>
    public bool IsSprinting => MovementState == PlayerMovementState.Sprinting;

    /// <summary>True if the player is in the walking state (the default ground state).</summary>
    public bool IsWalking => MovementState == PlayerMovementState.Walking;

    /// <summary>
    /// True if the player's FPC module reports being grounded. Returns false
    /// when the module is unavailable (non-FPC roles).
    /// </summary>
    public bool IsGrounded
    {
        get
        {
            FirstPersonMovementModule module = FirstPersonMovementModule;
            return module != null && module.IsGrounded;
        }
    }

    /// <summary>
    /// The facility zone the player is currently in. Returns
    /// <see cref="FacilityZone.None"/> if the player isn't in any tracked room.
    /// </summary>
    public FacilityZone Zone
    {
        get
        {
            Room room = Room;
            return room != null ? room.Zone : FacilityZone.None;
        }
    }

    public float Health => Stats.Health;

    public float MaxHealth => Stats.MaxHealth;

    public float HumeShield => Stats.HumeShield;

    public float MaxHumeShield => Stats.MaxHumeShield;

    public static Player Get(ReferenceHub hub) => PlayerRegistry.Get(hub);

    public static Player Get(GameObject gameObject)
    {
        return PlayerRegistry.Get(gameObject);
    }

    public static Player Get(int playerId) => PlayerRegistry.Get(playerId);

    public static bool TryGet(ReferenceHub hub, out Player player) => PlayerRegistry.TryGet(hub, out player);

    public static bool TryGet(GameObject gameObject, out Player player) => PlayerRegistry.TryGet(gameObject, out player);

    public override string ToString() => $"{GetType().Name}({GameObject?.name ?? "null"})";
}
