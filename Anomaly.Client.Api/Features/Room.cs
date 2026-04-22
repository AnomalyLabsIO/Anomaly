using System.Collections.ObjectModel;
using Il2CppMapGeneration;
using UnityEngine;

namespace Anomaly.Client.Api.Features;

public class Room
{
    private static readonly Dictionary<RoomIdentifier, Room> _rooms = new();

    private Room(RoomIdentifier roomIdentifier)
    {
        Base = roomIdentifier;
    }

    public RoomIdentifier Base { get; }

    public string Name => Base.Name.ToString();

    public RoomName RoomName => Base.Name;

    public FacilityZone Zone => Base.Zone;

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

    public static Room Get(Vector3 position)
    {
        if (!RoomUtils.TryGetRoom(position, out RoomIdentifier roomIdentifier))
            return null;

        return Get(roomIdentifier);
    }

    public override string ToString() => $"Room({RoomName}, {Zone})";
}
