using System.Collections.ObjectModel;
using Il2CppMapGeneration;
using UnityEngine;
using Anomaly.Client.Api.Scripting;

namespace Anomaly.Client.Api.Features;

[ScriptType]
public class Room
{
    private static readonly Dictionary<RoomIdentifier, Room> _rooms = new();

    private Room(RoomIdentifier roomIdentifier)
    {
        Base = roomIdentifier;
    }

    public RoomIdentifier Base { get; }

    [ScriptVisible]
    public bool IsDestroyed => Base == null;

    public GameObject GameObject => IsDestroyed ? null : Base.gameObject;

    public ReadOnlyCollection<Door> Doors => Door.Get(this).ToList().AsReadOnly();

    /// <summary>Players whose locally-known position resolves to this room.</summary>
    public ReadOnlyCollection<Player> Players => Player.PlayerList.Where(player => Get(player) == this).ToList().AsReadOnly();

    public ReadOnlyCollection<FacilityCamera> Cameras => FacilityCamera.GetForRoom(Base);

    public ReadOnlyCollection<LightsController> Lights
    {
        get
        {
            List<LightsController> result = new();
            if (!IsDestroyed && Base.LightControllers != null)
                foreach (var controller in Base.LightControllers)
                    if (LightsController.Get(controller) is { } lights)
                        result.Add(lights);
            return result.AsReadOnly();
        }
    }

    [ScriptVisible]
    public string Name => Base.Name.ToString();

    [ScriptVisible]
    public RoomName RoomName => Base.Name;

    [ScriptVisible]
    public FacilityZone Zone => Base.Zone;

    [ScriptVisible]
    public RoomShape Shape => Base.Shape;

    public Sprite Icon => Base.Icon;

    public Transform Transform => Base.transform;

    public Vector3 Position => Transform.position;

    public Vector3Int MainCoords => Base.MainCoords;

    public Bounds WorldspaceBounds => Base.WorldspaceBounds;

    public ReadOnlyCollection<Room> ConnectedRooms
    {
        get
        {
            List<Room> rooms = new();
            foreach (RoomIdentifier connectedRoom in Base.ConnectedRooms)
            {
                Room room = Get(connectedRoom);
                if (room != null)
                    rooms.Add(room);
            }

            return rooms.AsReadOnly();
        }
    }

    public static ReadOnlyCollection<Room> List
    {
        get
        {
            List<Room> rooms = new();
            foreach (RoomIdentifier roomIdentifier in RoomIdentifier.AllRoomIdentifiers)
            {
                Room room = Get(roomIdentifier);
                if (room != null)
                    rooms.Add(room);
            }

            return rooms.AsReadOnly();
        }
    }

    public static Room Get(RoomIdentifier roomIdentifier)
    {
        if (roomIdentifier == null)
            return null;

        if (_rooms.TryGetValue(roomIdentifier, out Room room))
            return room;

        ScriptHostAccess.ChargeRegistryEntries(_rooms.Count);
        ScriptHostAccess.ChargeWrapperCreation();
        room = new Room(roomIdentifier);
        _rooms[roomIdentifier] = room;
        return room;
    }

    public static Room Get(Player player)
    {
        if (player == null)
            return null;

        return Get(player.Position);
    }

    public static bool TryGet(RoomIdentifier roomIdentifier, out Room room) => (room = Get(roomIdentifier)) != null;
    public static bool TryGet(Player player, out Room room) => (room = Get(player)) != null;
    public static bool TryGet(Vector3 position, out Room room) => (room = Get(position)) != null;

    public static IEnumerable<Room> Get(RoomName name) => List.Where(room => room.RoomName == name);
    public static IEnumerable<Room> Get(FacilityZone zone) => List.Where(room => room.Zone == zone);
    public static IEnumerable<Room> Get(RoomShape shape) => List.Where(room => room.Shape == shape);

    public static Room Get(Vector3 position)
    {
        if (ScriptHostAccess.IsActive)
            ScriptHostAccess.ChargeRegistryEntries(RoomIdentifier.AllRoomIdentifiers.Count);
        if (!RoomUtils.TryGetRoom(position, out RoomIdentifier roomIdentifier))
            return null;

        return Get(roomIdentifier);
    }

    internal static bool Unregister(RoomIdentifier roomIdentifier) =>
        roomIdentifier != null && _rooms.Remove(roomIdentifier);

    internal static void ClearAll() => _rooms.Clear();

    public override string ToString() => $"Room({RoomName}, {Zone})";
}
