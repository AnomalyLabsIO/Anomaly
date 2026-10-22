using System.Collections.ObjectModel;
using Anomaly.Client.Api.Internal.Wrappers;
using Anomaly.Client.Api.Scripting;
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
using Il2CppPlayerRoles.PlayableScps.Scp079;
using Il2CppPlayerRoles.Voice;
using Il2CppPlayerStatsSystem;
using Il2CppRemoteAdmin;
using Il2CppVoiceChat;
using UnityEngine;

namespace Anomaly.Client.Api.Features;

[ScriptType]
public class Player
{
    internal Player(ReferenceHub hub)
    {
        ReferenceHub = hub;
    }

    internal Player(GameObject gameObject) : this(ReferenceHub.GetHub(gameObject)) { }

    public static ReadOnlyCollection<Player> PlayerList => PlayerRegistry.PlayerList;

    /// <summary>All players currently known to this client. Alias of <see cref="PlayerList"/>.</summary>
    public static ReadOnlyCollection<Player> List => PlayerList;

    public static ReadOnlyDictionary<GameObject, Player> GameObjectToPlayer => PlayerRegistry.GameObjectToPlayer;

    public ReferenceHub ReferenceHub { get; }

    /// <summary>Whether the underlying player object has been destroyed.</summary>
    [ScriptVisible]
    public bool IsDestroyed => ReferenceHub == null || ReferenceHub.gameObject == null;

    /// <summary>The replicated player ID, or -1 after the player object is destroyed.</summary>
    [ScriptVisible]
    public int PlayerId => IsDestroyed ? -1 : ReferenceHub.PlayerId;

    /// <summary>The nickname known to this client, or an empty string when unavailable.</summary>
    [ScriptVisible]
    public string Nickname => !IsDestroyed && NicknameSync != null ? NicknameSync.MyNick ?? string.Empty : string.Empty;

    /// <summary>The game's display name, including a server-provided custom name when present.</summary>
    public string DisplayName => !IsDestroyed && NicknameSync != null ? NicknameSync.DisplayName ?? string.Empty : string.Empty;

    public GameObject GameObject => ReferenceHub.gameObject;

    public bool IsLocal => this is LocalPlayer;

    public Vector3 Position => Transform.position;
    public Transform Transform => GameObject.transform;
    public CharacterClassManager CharacterClassManager => ReferenceHub.characterClassManager;
    public PlayerRoleManager RoleManager => ReferenceHub.roleManager;
    public PlayerRoleBase CurrentRole => RoleManager.CurrentRole;

    /// <summary>The current raw game role. Alias provided for LabAPI-style role access.</summary>
    public PlayerRoleBase RoleBase => IsDestroyed || RoleManager == null ? null : CurrentRole;

    /// <summary>The current role ID, or <see cref="RoleTypeId.None"/> when unavailable.</summary>
    public RoleTypeId RoleType => RoleBase != null ? RoleBase.RoleTypeId : RoleTypeId.None;

    /// <summary>The current team, or <see cref="Il2CppPlayerRoles.Team.Dead"/> when no role is available.</summary>
    public Team Team => RoleBase != null ? RoleBase.Team : Team.Dead;

    public bool IsAlive => RoleBase != null && Team != Team.Dead;
    public bool IsHuman => IsAlive && Team != Team.SCPs;
    public bool IsScp => RoleBase != null && Team == Team.SCPs;

    /// <summary>Alias of <see cref="IsScp"/> using LabAPI's capitalization.</summary>
    public bool IsSCP => IsScp;

    /// <summary>The current role's life identifier, or -1 when no role is available.</summary>
    public int LifeId => RoleBase != null ? RoleBase.UniqueLifeIdentifier : -1;

    public Role Role => Role.Get(CurrentRole);

    /// <summary>Client-known SCP-079 state, or null when the player has another role.</summary>
    public Scp079 Scp079 => global::Anomaly.Client.Api.Features.Scp079.Get(GetRole<Scp079Role>());

    /// <summary>The native voice module, or null for a role without voice support.</summary>
    public VoiceModuleBase VoiceModule => RoleBase?.TryCast<IVoiceRole>()?.VoiceModule;

    /// <summary>
    /// The module's last observed voice channel. It may remain set after speech
    /// stops; use <see cref="IsSpeaking"/> to check current client voice activity.
    /// </summary>
    public VoiceChatChannel VoiceChannel => VoiceModule?.CurrentChannel ?? VoiceChatChannel.None;

    /// <summary>
    /// Whether this client's voice module reports activity. This does not imply
    /// that the player is audible to every other player on the server.
    /// </summary>
    public bool IsSpeaking => VoiceModule != null && VoiceModule.IsSpeaking;
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
        ReferenceHub?.roleManager?.CurrentRole?.TryCast<IFpcRole>()?.FpcModule;

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

    /// <summary>Resolves a player by the game's replicated network identity ID.</summary>
    public static Player Get(uint netId) =>
        ReferenceHub.TryGetHubNetID(netId, out ReferenceHub hub) ? Get(hub) : null;

    public static bool TryGet(ReferenceHub hub, out Player player) => PlayerRegistry.TryGet(hub, out player);

    public static bool TryGet(GameObject gameObject, out Player player) => PlayerRegistry.TryGet(gameObject, out player);

    public static bool TryGet(int playerId, out Player player)
    {
        player = Get(playerId);
        if (player != null && !player.IsDestroyed)
            return true;

        player = null;
        return false;
    }

    public static bool TryGet(uint netId, out Player player)
    {
        player = Get(netId);
        if (player != null && !player.IsDestroyed)
            return true;

        player = null;
        return false;
    }

    /// <summary>
    /// Gets the current native game role as <typeparamref name="T"/>, or null
    /// when its type does not match. Uses an IL2CPP cast rather than a managed cast.
    /// </summary>
    public T GetRole<T>() where T : PlayerRoleBase => RoleBase?.TryCast<T>();

    public bool TryGetRole<T>(out T role) where T : PlayerRoleBase
    {
        role = GetRole<T>();
        return role != null;
    }

    public override string ToString() => $"{GetType().Name}({GameObject?.name ?? "null"})";
}
